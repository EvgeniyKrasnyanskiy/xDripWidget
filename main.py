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
import uuid
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
                type      TEXT    NOT NULL DEFAULT 'sgv', -- sgv or mbg
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
        conn.execute(
            """
            CREATE TABLE IF NOT EXISTS treatments (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                uuid      TEXT    NOT NULL DEFAULT '',
                eventType TEXT    NOT NULL DEFAULT 'Meal Bolus',
                insulin   REAL    NOT NULL DEFAULT 0.0,
                carbs     REAL    NOT NULL DEFAULT 0.0,
                notes     TEXT    NOT NULL DEFAULT '',
                timestamp INTEGER NOT NULL,
                glucose   REAL    DEFAULT NULL,
                is_voided INTEGER NOT NULL DEFAULT 0
            )
            """
        )
        conn.execute("CREATE INDEX IF NOT EXISTS idx_entries_ts    ON entries(timestamp DESC)")
        conn.execute("CREATE INDEX IF NOT EXISTS idx_devstatus_ts  ON devicestatus(timestamp DESC)")
        conn.execute("CREATE INDEX IF NOT EXISTS idx_treatments_ts ON treatments(timestamp DESC)")
        # --- migrations ---
        try:
            conn.execute("ALTER TABLE entries ADD COLUMN type TEXT NOT NULL DEFAULT 'sgv'")
            log.info("Migration: added type column to entries")
        except sqlite3.OperationalError:
            pass
        try:
            conn.execute("DELETE FROM entries WHERE type = 'mbg'")
            log.info("Migration: cleaned up mbg entries from entries table")
        except sqlite3.OperationalError:
            pass
        try:
            conn.execute("ALTER TABLE devicestatus ADD COLUMN battery INTEGER NOT NULL DEFAULT -1")
            log.info("Migration: added battery column to devicestatus")
        except sqlite3.OperationalError:
            pass
        try:
            conn.execute("ALTER TABLE treatments ADD COLUMN uuid TEXT NOT NULL DEFAULT ''")
            log.info("Migration: added uuid column to treatments")
        except sqlite3.OperationalError:
            pass
        try:
            conn.execute("ALTER TABLE treatments ADD COLUMN is_voided INTEGER NOT NULL DEFAULT 0")
            log.info("Migration: added is_voided column to treatments")
        except sqlite3.OperationalError:
            pass
        try:
            conn.execute("ALTER TABLE treatments ADD COLUMN glucose REAL DEFAULT NULL")
            log.info("Migration: added glucose column to treatments")
        except sqlite3.OperationalError:
            pass
        # Assign UUIDs to any existing treatments that lack one
        try:
            rows_no_uuid = conn.execute("SELECT id FROM treatments WHERE uuid = '' OR uuid IS NULL").fetchall()
            for r in rows_no_uuid:
                conn.execute("UPDATE treatments SET uuid = ? WHERE id = ?", (str(uuid.uuid4()), r["id"]))
            if rows_no_uuid:
                log.info("Migration: assigned UUIDs to %d existing treatments", len(rows_no_uuid))
        except Exception as e:
            log.warning("Migration uuid backfill error: %s", e)
        try:
            conn.execute("CREATE INDEX IF NOT EXISTS idx_treatments_uuid ON treatments(uuid)")
        except sqlite3.OperationalError:
            pass
    conn.close()
    log.info("Database initialised at %s", DB_PATH)


def prune_old_records(conn: sqlite3.Connection) -> None:
    cutoff = int(time.time()) - PRUNE_HOURS * 3600
    conn.execute("DELETE FROM entries       WHERE timestamp < ?", (cutoff,))
    conn.execute("DELETE FROM devicestatus  WHERE timestamp < ?", (cutoff,))
    conn.execute("DELETE FROM treatments   WHERE timestamp < ?", (cutoff,))


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


class TreatmentIn(BaseModel):
    eventType: str = "Meal Bolus"
    insulin: float = 0.0
    carbs: float = 0.0
    notes: str = ""
    created_at: Optional[Union[str, int]] = None
    date: Optional[int] = None
    # Optional manual blood glucose value (mmol/L)
    glucose: Optional[float] = None
    units: Optional[str] = None  # "mmol" or "mgdl"
    uuid: Optional[str] = None
    _id: Optional[str] = None


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


@app.get("/api/v1/entries.json", tags=["nightscout"], dependencies=[Depends(verify_api_key)])
@app.get("/api/v1/entries", tags=["nightscout"], include_in_schema=False, dependencies=[Depends(verify_api_key)])
def get_entries(request: Request, count: int = Query(default=100, le=1000)):
    """Nightscout-compatible entries endpoint — returns recent SGV/MBG entries for followers/xDrip+."""
    params = dict(request.query_params)
    # Support ?find[type]=mbg or ?count=N
    entry_type = params.get("find[type]") or params.get("type")  # e.g. "mbg" or "sgv"
    min_ts_ms = params.get("find[date][$gte]")
    min_ts = None
    if min_ts_ms and str(min_ts_ms).isdigit():
        raw = int(min_ts_ms)
        min_ts = raw // 1000 if raw > 1e10 else raw

    conn = get_db()
    try:
        if min_ts:
            rows = conn.execute(
                "SELECT id, sgv, direction, type, timestamp FROM entries WHERE timestamp >= ? ORDER BY timestamp DESC LIMIT ?",
                (min_ts, count),
            ).fetchall()
        else:
            rows = conn.execute(
                "SELECT id, sgv, direction, type, timestamp FROM entries ORDER BY timestamp DESC LIMIT ?",
                (count,),
            ).fetchall()
    finally:
        conn.close()

    result = []
    for r in rows:
        rec_type = (r["type"] if r["type"] else "sgv").lower()
        if entry_type and entry_type.lower() != rec_type:
            continue

        ts_ms = r["timestamp"] * 1000
        iso = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(r["timestamp"]))
        val = round(r["sgv"], 1)
        entry = {
            "_id": str(r["id"]),
            "type": rec_type,
            "dateString": iso,
            "date": ts_ms,
            "mills": ts_ms,
            "device": "xDripWidget",
            "glucose": val,
        }
        if rec_type == "mbg":
            entry["mbg"] = val
        else:
            entry["sgv"] = val
            entry["direction"] = r["direction"]
        result.append(entry)
    return result


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

                e_type = str(raw.get("type", "sgv")).lower()
                if entry.mbg is not None and entry.sgv is None:
                    e_type = "mbg"

                conn.execute(
                    "INSERT INTO entries (sgv, direction, type, timestamp) VALUES (?, ?, ?, ?)",
                    (sgv_mgdl, entry.direction, e_type, ts),
                )
                inserted += 1
                log.info("Entry saved (%s): %.1f mg/dL  %s  @%s", e_type, sgv_mgdl, entry.direction, ts)
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

    # DEBUG: log raw payload so we can inspect what xDrip+ / AAPS actually sends
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
def get_treatments(request: Request, limit: int = 50):
    """Nightscout-compatible treatments endpoint (returns recent boluses & carbs for xDrip+)."""
    params = dict(request.query_params)
    target_uuid = params.get("find[uuid]") or params.get("find[_id]") or params.get("find[sysid]") or params.get("uuid") or params.get("_id")
    min_ts_raw = (
        params.get("find[createdAt][$gte]")
        or params.get("find[created_at][$gte]")
        or params.get("find[date][$gte]")
        or params.get("find[mills][$gte]")
        or params.get("find[timestamp][$gte]")
    )
    min_ts = None
    if min_ts_raw and str(min_ts_raw).isdigit():
        val_i = int(min_ts_raw)
        min_ts = val_i // 1000 if val_i > 1e10 else val_i

    conn = get_db()
    try:
        if target_uuid:
            rows = conn.execute(
                "SELECT id, uuid, eventType, insulin, carbs, notes, timestamp, glucose, is_voided FROM treatments WHERE uuid = ? OR id = ?",
                (str(target_uuid), str(target_uuid)),
            ).fetchall()
            log.debug("GET treatments filter uuid=%s → %d rows", target_uuid, len(rows))
        elif min_ts:
            rows = conn.execute(
                "SELECT id, uuid, eventType, insulin, carbs, notes, timestamp, glucose, is_voided FROM treatments WHERE timestamp >= ? ORDER BY timestamp DESC LIMIT ?",
                (min_ts, limit),
            ).fetchall()
        else:
            rows = conn.execute(
                "SELECT id, uuid, eventType, insulin, carbs, notes, timestamp, glucose, is_voided FROM treatments ORDER BY timestamp DESC LIMIT ?",
                (limit,),
            ).fetchall()
    finally:
        conn.close()

    result = []
    for r in rows:
        ts_ms = r["timestamp"] * 1000
        item_uuid = r["uuid"] if r["uuid"] else str(r["id"])
        is_void = bool(r["is_voided"] or r["eventType"] == "Void")
        iso_time = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(r["timestamp"]))
        item = {
            "_id": item_uuid,
            "uuid": item_uuid,
            "sysid": item_uuid,
            "eventType": "Void" if is_void else r["eventType"],
            "isVoided": is_void,
            "isValidated": not is_void,
            "insulin": 0.0 if is_void else r["insulin"],
            "carbs": 0.0 if is_void else r["carbs"],
            "notes": "Voided" if is_void else r["notes"],
            "created_at": iso_time,
            "createdAt": iso_time,
            "mills": ts_ms,
            "timestamp": ts_ms,
            "enteredBy": "xDripWidget",
        }
        if r["glucose"] is not None and not is_void:
            g_mgdl = r["glucose"]
            item["glucose"] = g_mgdl
            item["mgdl"] = g_mgdl
            item["mbg"] = g_mgdl
            item["units"] = "mg/dl"
            item["glucoseType"] = "Finger"
        result.append(item)
    return result


def _mark_voided(conn: sqlite3.Connection, tid: str) -> int:
    row = conn.execute("SELECT timestamp, glucose FROM treatments WHERE uuid = ? OR id = ?", (tid, tid)).fetchone()
    if row and row["timestamp"]:
        ts = row["timestamp"]
        conn.execute("DELETE FROM entries WHERE type = 'mbg' AND timestamp >= ? AND timestamp <= ?", (ts - 10, ts + 10))

    cur = conn.execute(
        "UPDATE treatments SET is_voided = 1, eventType = 'Void', carbs = 0.0, insulin = 0.0, notes = 'Voided' WHERE uuid = ? OR id = ?",
        (tid, tid),
    )
    if cur.rowcount == 0:
        conn.execute(
            "INSERT INTO treatments (uuid, eventType, insulin, carbs, notes, timestamp, is_voided) VALUES (?, ?, ?, ?, ?, ?, ?)",
            (tid, "Void", 0.0, 0.0, "Voided", int(time.time()), 1),
        )
        return 1
    return cur.rowcount


@app.post(
    "/api/v1/treatments.json",
    status_code=status.HTTP_200_OK,
    tags=["nightscout"],
    dependencies=[Depends(verify_api_key)],
)
@app.post(
    "/api/v1/treatments",
    status_code=status.HTTP_200_OK,
    tags=["nightscout"],
    dependencies=[Depends(verify_api_key)],
    include_in_schema=False,
)
async def post_treatments(request: Request):
    """
    Nightscout-compatible endpoint to record treatments (carbs / insulin).
    Accepts JSON object or array.
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
                t = TreatmentIn.model_validate(raw)
                ts = t.date
                if ts and ts > 1e10:
                    ts = int(ts / 1000)
                if not ts:
                    ts = int(time.time())

                item_uuid = str(raw.get("uuid") or raw.get("_id") or t.uuid or t._id or uuid.uuid4())

                # Handle optional glucose value (convert mmol→mgdl if needed)
                glucose_mgdl: Optional[float] = None
                if t.glucose is not None and t.glucose > 0:
                    if t.units == "mmol":
                        glucose_mgdl = round(t.glucose * 18.0182, 1)
                    else:
                        glucose_mgdl = t.glucose

                conn.execute(
                    "INSERT INTO treatments (uuid, eventType, insulin, carbs, notes, timestamp, glucose, is_voided) VALUES (?, ?, ?, ?, ?, ?, ?, 0)",
                    (item_uuid, t.eventType, t.insulin, t.carbs, t.notes, ts, glucose_mgdl),
                )
                inserted += 1
                log.info(
                    "Treatment saved: uuid=%s %s Insulin=%.1f Carbs=%.1f Glucose=%s @%s",
                    item_uuid, t.eventType, t.insulin, t.carbs,
                    f"{glucose_mgdl:.1f}mg/dL" if glucose_mgdl else "—", ts
                )
    finally:
        conn.close()

    return {"saved": inserted}


@app.delete(
    "/api/v1/treatments/{treatment_id}",
    status_code=status.HTTP_200_OK,
    tags=["nightscout"],
    dependencies=[Depends(verify_api_key)],
)
@app.delete(
    "/api/v1/treatments/{treatment_id}.json",
    status_code=status.HTTP_200_OK,
    tags=["nightscout"],
    dependencies=[Depends(verify_api_key)],
    include_in_schema=False,
)
def delete_treatment_by_path(treatment_id: str):
    """Delete treatment by ID or UUID (Nightscout REST API)."""
    tid = treatment_id.replace(".json", "")
    conn = get_db()
    deleted = 0
    try:
        with conn:
            deleted = _mark_voided(conn, tid)
            log.info("Treatment voided via DELETE path: uuid=%s (rows=%d)", tid, deleted)
    finally:
        conn.close()
    return {"deleted": deleted}


@app.delete(
    "/api/v1/treatments",
    status_code=status.HTTP_200_OK,
    tags=["nightscout"],
    dependencies=[Depends(verify_api_key)],
)
@app.delete(
    "/api/v1/treatments.json",
    status_code=status.HTTP_200_OK,
    tags=["nightscout"],
    dependencies=[Depends(verify_api_key)],
    include_in_schema=False,
)
def delete_treatment_by_query(request: Request):
    """Delete treatment by query params (e.g. ?find[uuid]=xxx or ?find[_id]=xxx or ?id=xxx)."""
    params = dict(request.query_params)
    tid = params.get("uuid") or params.get("_id") or params.get("id") or params.get("find[uuid]") or params.get("find[_id]") or params.get("find[sysid]")
    deleted = 0
    conn = get_db()
    try:
        with conn:
            if tid:
                deleted = _mark_voided(conn, str(tid))
                log.info("Treatment voided via DELETE query: uuid=%s (rows=%d)", tid, deleted)
            else:
                log.warning("DELETE /treatments called without id/uuid in params: %s", params)
    finally:
        conn.close()
    return {"deleted": deleted}


@app.put(
    "/api/v1/treatments.json",
    status_code=status.HTTP_200_OK,
    tags=["nightscout"],
    dependencies=[Depends(verify_api_key)],
)
@app.put(
    "/api/v1/treatments",
    status_code=status.HTTP_200_OK,
    tags=["nightscout"],
    dependencies=[Depends(verify_api_key)],
    include_in_schema=False,
)
@app.put(
    "/api/v1/treatments/{treatment_id}",
    status_code=status.HTTP_200_OK,
    tags=["nightscout"],
    dependencies=[Depends(verify_api_key)],
    include_in_schema=False,
)
@app.put(
    "/api/v1/treatments/{treatment_id}.json",
    status_code=status.HTTP_200_OK,
    tags=["nightscout"],
    dependencies=[Depends(verify_api_key)],
    include_in_schema=False,
)
async def put_treatments(request: Request, treatment_id: Optional[str] = None):
    """
    Nightscout-compatible PUT endpoint to update or void treatments.
    xDrip+ sends PUT when editing or deleting/voiding treatments.
    """
    body = await request.json()
    if isinstance(body, dict):
        body = [body]

    conn = get_db()
    updated = 0
    try:
        with conn:
            for raw in body:
                tid = treatment_id or raw.get("uuid") or raw.get("_id") or raw.get("id")
                if tid:
                    tid = str(tid).replace(".json", "")
                    insulin = float(raw.get("insulin", 0.0) or 0.0)
                    carbs = float(raw.get("carbs", 0.0) or 0.0)
                    notes = str(raw.get("notes", "") or "")
                    event_type = str(raw.get("eventType", "Meal Bolus") or "Meal Bolus")

                    if raw.get("isVoided") or raw.get("eventType") == "Void" or raw.get("notes") == "Voided":
                        updated += _mark_voided(conn, tid)
                        log.info("Treatment voided via PUT: uuid=%s", tid)
                    else:
                        cur = conn.execute(
                            "UPDATE treatments SET eventType = ?, insulin = ?, carbs = ?, notes = ? WHERE uuid = ?",
                            (event_type, insulin, carbs, notes, tid),
                        )
                        updated += cur.rowcount
                        log.info("Treatment updated via PUT: uuid=%s Insulin=%.1f Carbs=%.1f", tid, insulin, carbs)
    finally:
        conn.close()

    return {"updated": updated}


@app.get("/api/v1/history", tags=["widget"])
def get_history(
    hours: float = Query(default=4.0, ge=0.5, le=24.0),
    api_secret: Optional[str] = Header(default=None, alias="api-secret"),
    token: Optional[str] = Query(default=None, alias="token"),
):
    """Returns glucose history for the last N hours for the desktop widget sparkline."""
    if api_secret or token:
        verify_api_key(api_secret=api_secret, token=token)

    cutoff = int(time.time()) - int(hours * 3600)
    conn = get_db()
    try:
        rows = conn.execute(
            "SELECT sgv, direction, timestamp FROM entries WHERE timestamp >= ? AND (type = 'sgv' OR type IS NULL) ORDER BY timestamp ASC",
            (cutoff,),
        ).fetchall()
    finally:
        conn.close()

    return [
        {
            "mmol": _to_mmol(r["sgv"]),
            "timestamp": r["timestamp"],
            "direction": r["direction"],
        }
        for r in rows
    ]


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
            "SELECT sgv, direction, timestamp FROM entries WHERE (type = 'sgv' OR type IS NULL) ORDER BY timestamp DESC LIMIT 2"
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
