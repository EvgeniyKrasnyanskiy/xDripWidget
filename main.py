"""
Glycemia Micro-Backend — Nightscout-compatible REST API
Ultra-light: FastAPI + SQLite, target RAM usage < 20 MB.

Endpoints:
  POST /api/v1/entries.json       — receive glucose from xDrip+
  POST /api/v1/entries            — alias (no .json suffix)
  POST /api/v1/devicestatus.json  — receive IoB / CoB from AAPS
  POST /api/v1/devicestatus       — alias (no .json suffix)
  GET  /api/v1/status[.json]      — Nightscout status probe for xDrip+
  GET  /api/v1/treatments[.json]  — stub: returns [] (required by xDrip+)
  GET  /api/v1/current            — latest reading for the desktop widget
  GET  /health                    — liveness probe
"""

import hashlib
import logging
import os
import sqlite3
import time
from contextlib import asynccontextmanager
from typing import Any, List, Optional, Union

from fastapi import Depends, FastAPI, Header, HTTPException, Query, Request, status
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field, field_validator

# ---------------------------------------------------------------------------
# Configuration (override via environment variables)
# ---------------------------------------------------------------------------
API_SECRET: str = os.environ.get("API_SECRET", "changeme")  # plain-text secret
DB_PATH: str = os.environ.get("DB_PATH", "./data/glycemia.db")
PRUNE_HOURS: int = int(os.environ.get("PRUNE_HOURS", "48"))  # keep last N hours
LOG_LEVEL: str = os.environ.get("LOG_LEVEL", "INFO")

logging.basicConfig(level=getattr(logging, LOG_LEVEL), format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Database helpers
# ---------------------------------------------------------------------------

def _sha1(text: str) -> str:
    return hashlib.sha1(text.encode()).hexdigest()

SECRET_HASH: str = _sha1(API_SECRET)


def get_db() -> sqlite3.Connection:
    """Open a SQLite connection (used per-request, thread-local)."""
    os.makedirs(os.path.dirname(DB_PATH) or ".", exist_ok=True)
    conn = sqlite3.connect(DB_PATH, check_same_thread=False)
    conn.row_factory = sqlite3.Row
    return conn


def init_db() -> None:
    conn = get_db()
    with conn:
        conn.execute(
            """
            CREATE TABLE IF NOT EXISTS entries (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                sgv       REAL    NOT NULL,          -- mg/dL
                direction TEXT    NOT NULL DEFAULT 'Unknown',
                timestamp INTEGER NOT NULL            -- unix seconds
            )
            """
        )
        conn.execute(
            """
            CREATE TABLE IF NOT EXISTS devicestatus (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                iob       REAL    NOT NULL DEFAULT 0.0,
                cob       REAL    NOT NULL DEFAULT 0.0,
                battery   INTEGER NOT NULL DEFAULT -1,   -- phone battery %, -1 = unknown
                timestamp INTEGER NOT NULL
            )
            """
        )
        conn.execute("CREATE INDEX IF NOT EXISTS idx_entries_ts    ON entries(timestamp DESC)")
        conn.execute("CREATE INDEX IF NOT EXISTS idx_devstatus_ts  ON devicestatus(timestamp DESC)")
        # --- migration: add battery column if upgrading from older schema ---
        try:
            conn.execute("ALTER TABLE devicestatus ADD COLUMN battery INTEGER NOT NULL DEFAULT -1")
            log.info("Migration: added battery column to devicestatus")
        except sqlite3.OperationalError:
            pass  # column already exists
    conn.close()
    log.info("Database initialised at %s", DB_PATH)


def prune_old_records(conn: sqlite3.Connection) -> None:
    cutoff = int(time.time()) - PRUNE_HOURS * 3600
    conn.execute("DELETE FROM entries       WHERE timestamp < ?", (cutoff,))
    conn.execute("DELETE FROM devicestatus  WHERE timestamp < ?", (cutoff,))


# ---------------------------------------------------------------------------
# Security
# ---------------------------------------------------------------------------

def verify_api_key(
    api_secret: Optional[str] = Header(default=None, alias="api-secret"),
    token: Optional[str] = Query(default=None, alias="token"),
) -> None:
    """
    Accept:
      - Header  api-secret: <sha1(plain)>  (Nightscout standard)
      - Header  api-secret: <plain text>   (convenience)
      - Query   ?token=<plain text>        (convenience)
    """
    candidates = []
    if api_secret:
        candidates.append(api_secret)
        candidates.append(_sha1(api_secret))
    if token:
        candidates.append(token)
        candidates.append(_sha1(token))

    if SECRET_HASH in candidates or API_SECRET in candidates:
        return

    raise HTTPException(
        status_code=status.HTTP_401_UNAUTHORIZED,
        detail="Invalid or missing api-secret",
    )


# ---------------------------------------------------------------------------
# Pydantic models
# ---------------------------------------------------------------------------

MG_DL_THRESHOLD = 40.0  # below this value we treat it as mmol/L already entered wrong


def _to_mgdl(value: float) -> float:
    """Heuristic: if value < 40 assume mmol/L → convert to mg/dL."""
    if value < MG_DL_THRESHOLD:
        return round(value * 18.0182, 1)
    return value


def _to_mmol(mgdl: float) -> float:
    return round(mgdl / 18.0182, 2)


class EntryIn(BaseModel):
    sgv: Optional[float] = None           # mg/dL (primary)
    mbg: Optional[float] = None           # manual BG (fallback)
    direction: str = "Unknown"
    date: Optional[int] = None            # unix ms
    dateString: Optional[str] = None
    # xDrip also sends 'glucose_value' in some builds
    glucose_value: Optional[float] = None

    @field_validator("sgv", "mbg", "glucose_value", mode="before")
    @classmethod
    def coerce_float(cls, v: Any) -> Optional[float]:
        if v is None:
            return None
        try:
            return float(v)
        except (TypeError, ValueError):
            return None


class DeviceStatusIn(BaseModel):
    # AAPS sends a nested structure; we flatten what we need
    openaps: Optional[dict] = None
    pump: Optional[dict] = None
    iob: Optional[float] = None
    cob: Optional[float] = None
    # raw top-level fields from some AAPS versions
    IOB: Optional[float] = Field(default=None, alias="IOB")
    COB: Optional[float] = Field(default=None, alias="COB")

    model_config = {"populate_by_name": True}


# ---------------------------------------------------------------------------
# App lifespan
# ---------------------------------------------------------------------------

@asynccontextmanager
async def lifespan(app: FastAPI):
    init_db()
    yield


app = FastAPI(
    title="Glycemia Micro-Backend",
    version="1.0.0",
    docs_url="/docs",
    lifespan=lifespan,
)


# ---------------------------------------------------------------------------
# Routes
# ---------------------------------------------------------------------------

@app.get("/health", tags=["misc"])
def health():
    return {"status": "ok", "ts": int(time.time())}


@app.post(
    "/api/v1/entries.json",
    status_code=status.HTTP_200_OK,
    tags=["nightscout"],
    dependencies=[Depends(verify_api_key)],
)
@app.post(
    "/api/v1/entries",
    status_code=status.HTTP_200_OK,
    tags=["nightscout"],
    dependencies=[Depends(verify_api_key)],
    include_in_schema=False,
)
async def post_entries(request: Request):
    """
    Nightscout-compatible entries endpoint.
    Accepts a JSON array OR a single object.
    """
    body = await request.json()
    if isinstance(body, dict):
        body = [body]

    conn = get_db()
    inserted = 0
    try:
        with conn:
            prune_old_records(conn)
            for raw in body:
                entry = EntryIn.model_validate(raw)
                # resolve glucose value
                raw_sgv = entry.sgv or entry.mbg or entry.glucose_value
                if raw_sgv is None:
                    log.warning("Entry without sgv/mbg/glucose_value, skipping: %s", raw)
                    continue
                sgv_mgdl = _to_mgdl(raw_sgv)

                # resolve timestamp (ms → s)
                ts = entry.date
                if ts and ts > 1e10:
                    ts = int(ts / 1000)
                if not ts:
                    ts = int(time.time())

                conn.execute(
                    "INSERT INTO entries (sgv, direction, timestamp) VALUES (?, ?, ?)",
                    (sgv_mgdl, entry.direction, ts),
                )
                inserted += 1
                log.info("Entry saved: %.1f mg/dL  %s  @%s", sgv_mgdl, entry.direction, ts)
    finally:
        conn.close()

    return {"saved": inserted}


@app.post(
    "/api/v1/devicestatus.json",
    status_code=status.HTTP_200_OK,
    tags=["nightscout"],
    dependencies=[Depends(verify_api_key)],
)
@app.post(
    "/api/v1/devicestatus",
    status_code=status.HTTP_200_OK,
    tags=["nightscout"],
    dependencies=[Depends(verify_api_key)],
    include_in_schema=False,
)
async def post_devicestatus(request: Request):
    """Nightscout-compatible devicestatus endpoint (AAPS IoB / CoB)."""
    body = await request.json()
    if isinstance(body, dict):
        body = [body]

    log.debug("devicestatus raw payload: %s", body)

    conn = get_db()
    inserted = 0
    try:
        with conn:
            prune_old_records(conn)
            for raw in body:
                ds = DeviceStatusIn.model_validate(raw)

                iob = ds.iob or ds.IOB or 0.0
                cob = ds.cob or ds.COB or 0.0

                # parse phone battery from xDrip+ uploader field
                battery = -1
                uploader = raw.get("uploader", {})
                if isinstance(uploader, dict):
                    bat = uploader.get("battery")
                    if bat is not None:
                        try:
                            battery = int(bat)
                        except (TypeError, ValueError):
                            pass

                # try nested openaps structure
                if iob == 0.0 and ds.openaps:
                    iob_data = ds.openaps.get("iob", {})
                    if isinstance(iob_data, dict):
                        iob = float(iob_data.get("iob", 0.0) or 0.0)
                        cob = float(iob_data.get("cob", 0.0) or 0.0)

                ts = int(time.time())
                conn.execute(
                    "INSERT INTO devicestatus (iob, cob, battery, timestamp) VALUES (?, ?, ?, ?)",
                    (iob, cob, battery, ts),
                )
                inserted += 1
                log.info("DeviceStatus saved: IoB=%.2f CoB=%.2f Battery=%d%%", iob, cob, battery)
    finally:
        conn.close()

    return {"saved": inserted}


@app.get("/api/v1/status.json", tags=["nightscout"])
@app.get("/api/v1/status", tags=["nightscout"], include_in_schema=False)
def nightscout_status():
    """Nightscout-compatible status endpoint — required by xDrip+ connection test."""
    return {
        "status": "ok",
        "name": "Micro-Nightscout",
        "version": "1.0.0",
        "settings": {"units": "mmol"},
    }


@app.get("/api/v1/treatments", tags=["nightscout"])
@app.get("/api/v1/treatments.json", tags=["nightscout"], include_in_schema=False)
def get_treatments():
    """Stub treatments endpoint — returns empty list so xDrip+ doesn't 404."""
    return []


@app.get("/api/v1/current", tags=["widget"])
def get_current(
    api_secret: Optional[str] = Header(default=None, alias="api-secret"),
    token: Optional[str] = Query(default=None, alias="token"),
):
    """
    Returns the latest glucose reading + IoB for the desktop widget.
    Authentication is optional for GET so the widget URL stays simple,
    but if any credential is sent it must be valid.
    """
    if api_secret or token:
        verify_api_key(api_secret=api_secret, token=token)

    conn = get_db()
    try:
        row_e = conn.execute(
            "SELECT sgv, direction, timestamp FROM entries ORDER BY timestamp DESC LIMIT 2"
        ).fetchall()
        row_d = conn.execute(
            "SELECT iob, cob, battery FROM devicestatus ORDER BY timestamp DESC LIMIT 1"
        ).fetchone()
    finally:
        conn.close()

    if not row_e:
        return JSONResponse(status_code=204, content={"detail": "No data yet"})

    latest = row_e[0]
    now = int(time.time())
    minutes_ago = max(0, (now - latest["timestamp"]) // 60)

    # delta
    delta_str = "?"
    if len(row_e) >= 2:
        diff = _to_mmol(latest["sgv"]) - _to_mmol(row_e[1]["sgv"])
        delta_str = f"{'+' if diff >= 0 else ''}{diff:.1f}"

    iob = round(float(row_d["iob"]), 2) if row_d else 0.0
    cob = round(float(row_d["cob"]), 2) if row_d else 0.0
    battery = int(row_d["battery"]) if row_d else -1

    return {
        "mmol": _to_mmol(latest["sgv"]),
        "mgdl": round(latest["sgv"], 1),
        "direction": latest["direction"],
        "delta": delta_str,
        "iob": iob,
        "cob": cob,
        "battery": battery,
        "minutes_ago": minutes_ago,
        "timestamp": latest["timestamp"],
    }


# ---------------------------------------------------------------------------
# Entrypoint (dev / direct run)
# ---------------------------------------------------------------------------
if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8080, workers=1)
