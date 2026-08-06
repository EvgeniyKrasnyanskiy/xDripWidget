"""
Glycemia Desktop Widget — PyQt6
Always-on-top translucent overlay, polls /api/v1/current every 60 s.
Works on Windows, macOS and Linux.

Features:
  - Graphical battery bar indicator (horizontal, colored fill)
  - Glucose alerts: hypo < 4.5, hyper > 9.0, critical > 14.0 mmol/L
  - 1-hour cooldown per alert type
  - Single-instance protection via QLocalServer
  - Opacity control in settings (live preview)
  - Config storage in config.ini (with fallback & hot-reload)
  - Dynamic blood-drop tray icon (color-coded by glucose state)
  - 4-hour glucose history sparkline graph
  - Treatments logging (Carbs / Insulin / Blood Glucose) to server
  - Treatments history viewer & delete dialog
  - GitHub update checker (in About dialog only)
  - Exit confirmation dialog
  - Debug logging with 1MB rotating file handler (widget.log)

Requirements: widget_requirements.txt
"""

import json
import logging
from logging.handlers import RotatingFileHandler
import os
import sys
import time
import urllib.error
import urllib.request
import uuid
from datetime import datetime
from typing import Optional

from PyQt6.QtCore import (
    QDateTime,
    QPoint,
    QSettings,
    Qt,
    QThread,
    QTimer,
    pyqtSignal,
)
from PyQt6.QtGui import (
    QAction,
    QBrush,
    QColor,
    QFont,
    QIcon,
    QPainter,
    QPainterPath,
    QPen,
    QPixmap,
)
from PyQt6.QtNetwork import QLocalServer, QLocalSocket
from PyQt6.QtWidgets import (
    QAbstractItemView,
    QApplication,
    QComboBox,
    QDateTimeEdit,
    QDialog,
    QDialogButtonBox,
    QDoubleSpinBox,
    QFormLayout,
    QHBoxLayout,
    QHeaderView,
    QLabel,
    QLineEdit,
    QMenu,
    QMessageBox,
    QPushButton,
    QSlider,
    QSystemTrayIcon,
    QTableWidget,
    QTableWidgetItem,
    QTextBrowser,
    QVBoxLayout,
    QWidget,
)

# ---------------------------------------------------------------------------
# Logging Setup (Rotating File Handler: 1 MB max, 2 backup files)
# ---------------------------------------------------------------------------
LOG_FILE = "widget.log"
logger = logging.getLogger("xDripWidget")
logger.setLevel(logging.DEBUG)

if not logger.handlers:
    try:
        file_handler = RotatingFileHandler(
            LOG_FILE, maxBytes=1_000_000, backupCount=2, encoding="utf-8"
        )
        formatter = logging.Formatter("%(asctime)s [%(levelname)s] %(message)s")
        file_handler.setFormatter(formatter)
        logger.addHandler(file_handler)

        console_handler = logging.StreamHandler()
        console_handler.setFormatter(formatter)
        logger.addHandler(console_handler)
    except Exception as exc:
        print(f"Failed to initialize log file handler: {exc}")

logger.info("=================== xDrip Widget Initializing ===================")

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
APP_NAME     = "xDrip Widget"
APP_VERSION  = "1.4.4"
ORG_NAME     = "xdripwidget"
INSTANCE_KEY = "xDripWidgetSingleInstance"
DEFAULT_URL  = "http://localhost:8080"
CONFIG_FILE  = "config.ini"
POLL_INTERVAL_MS = 60_000   # 60 s
ALERT_COOLDOWN_S = 3_600    # 1 h between same-type alerts

# Thresholds (mmol/L)
HYPO_SEVERE  = 3.3
HYPO_MILD    = 3.9
HYPER_MILD   = 9.0
HYPER_SEVERE = 11.0
STALE_MINUTES = 15

# Alert thresholds
ALERT_HYPO      = 4.5
ALERT_HYPER     = 9.0
ALERT_CRITICAL  = 14.0

TREND_ARROWS: dict[str, str] = {
    "DoubleUp":          "⇈",
    "SingleUp":          "↑",
    "FortyFiveUp":       "↗",
    "Flat":              "→",
    "FortyFiveDown":     "↘",
    "SingleDown":        "↓",
    "DoubleDown":        "⇊",
    "NOT COMPUTABLE":    "?",
    "RATE OUT OF RANGE": "⚡",
    "Unknown":           "?",
}

# ---------------------------------------------------------------------------
# Colors
# ---------------------------------------------------------------------------
COLOR_GREEN  = QColor("#27ae60")
COLOR_YELLOW = QColor("#f39c12")
COLOR_RED    = QColor("#e74c3c")
COLOR_GRAY   = QColor("#7f8c8d")
COLOR_BG     = QColor(20, 20, 30, 210)
COLOR_SUB    = QColor("#bdc3c7")


def get_settings() -> QSettings:
    """Return QSettings instance bound to config.ini, gracefully creating defaults if missing."""
    if not os.path.exists(CONFIG_FILE):
        try:
            with open(CONFIG_FILE, "w", encoding="utf-8") as f:
                f.write("[General]\nserver_url = http://localhost:8080\napi_secret = \nopacity = 90\n")
            logger.info("Created default config.ini file")
        except Exception as e:
            logger.error(f"Error creating config.ini: {e}")
    return QSettings(CONFIG_FILE, QSettings.Format.IniFormat)


def glucose_color(mmol: float, stale: bool) -> QColor:
    if stale:
        return COLOR_GRAY
    if mmol < HYPO_SEVERE or mmol > HYPER_SEVERE:
        return COLOR_RED
    if mmol < HYPO_MILD or mmol > HYPER_MILD:
        return COLOR_YELLOW
    return COLOR_GREEN


def battery_color(pct: int) -> QColor:
    if pct < 0:
        return COLOR_GRAY
    if pct <= 20:
        return COLOR_RED
    if pct <= 50:
        return COLOR_YELLOW
    return COLOR_GREEN


def create_blood_drop_icon(color: QColor, size: int = 32) -> QIcon:
    """Generate a high-DPI vector teardrop/blood-drop tray icon with status color."""
    pixmap = QPixmap(size, size)
    pixmap.fill(Qt.GlobalColor.transparent)

    painter = QPainter(pixmap)
    painter.setRenderHint(QPainter.RenderHint.Antialiasing)

    path = QPainterPath()
    cx = size / 2.0
    cy = size * 0.60
    r  = size * 0.33

    # Top pointed tip down to rounded bottom
    path.moveTo(cx, 2)
    path.cubicTo(cx + r * 1.25, cy - r * 0.2, cx + r, cy + r, cx, cy + r)
    path.cubicTo(cx - r, cy + r, cx - r * 1.25, cy - r * 0.2, cx, 2)

    painter.setPen(QPen(color.darker(125), 1.5))
    painter.setBrush(QBrush(color))
    painter.drawPath(path)
    painter.end()

    return QIcon(pixmap)


# ---------------------------------------------------------------------------
# Worker threads
# ---------------------------------------------------------------------------
class FetchWorker(QThread):
    data_ready  = pyqtSignal(dict, list)  # current_data, history_list
    fetch_error = pyqtSignal(str)

    def __init__(self, base_url: str, api_secret: str = ""):
        super().__init__()
        self._base_url   = base_url
        self._api_secret = api_secret
        self._cancelled  = False

    def cancel(self):
        logger.debug("FetchWorker.cancel() called")
        self._cancelled = True

    def run(self):
        url_curr = self._base_url.rstrip("/") + "/api/v1/current"
        url_hist = self._base_url.rstrip("/") + "/api/v1/history?hours=4"
        if self._api_secret:
            url_curr += f"?token={self._api_secret}"
            url_hist += f"&token={self._api_secret}"

        logger.debug(f"FetchWorker: requesting {url_curr}")
        try:
            req_curr = urllib.request.Request(
                url_curr,
                headers={"Accept": "application/json", "Connection": "close"}
            )
            with urllib.request.urlopen(req_curr, timeout=8) as resp:
                data_curr = json.loads(resp.read().decode())

            if self._cancelled:
                logger.debug("FetchWorker cancelled after current fetch")
                return

            data_hist = []
            try:
                logger.debug(f"FetchWorker: requesting {url_hist}")
                req_hist = urllib.request.Request(
                    url_hist,
                    headers={"Accept": "application/json", "Connection": "close"}
                )
                with urllib.request.urlopen(req_hist, timeout=8) as resp:
                    data_hist = json.loads(resp.read().decode())
            except Exception as e_hist:
                logger.warning(f"FetchWorker: history fetch failed: {e_hist}")

            if not self._cancelled:
                logger.debug(f"FetchWorker success: mmol={data_curr.get('mmol')}, hist_len={len(data_hist)}")
                self.data_ready.emit(data_curr, data_hist)
        except urllib.error.HTTPError as e:
            if not self._cancelled:
                logger.error(f"FetchWorker HTTP error: {e.code}")
                self.fetch_error.emit(f"HTTP {e.code}")
        except Exception as exc:
            if not self._cancelled:
                logger.error(f"FetchWorker error: {exc}")
                self.fetch_error.emit(str(exc))


class UpdateCheckerWorker(QThread):
    update_result = pyqtSignal(bool, str, str, str)  # has_update, tag, body, url
    error_signal  = pyqtSignal(str)

    def __init__(self, current_version: str):
        super().__init__()
        self._current_version = current_version

    def run(self):
        url = "https://api.github.com/repos/EvgeniyKrasnyanskiy/xDripWidget/releases/latest"
        logger.debug(f"UpdateCheckerWorker: checking {url}")
        try:
            req = urllib.request.Request(
                url,
                headers={"Accept": "application/vnd.github.v3+json", "User-Agent": "xDripWidget"}
            )
            with urllib.request.urlopen(req, timeout=8) as resp:
                data = json.loads(resp.read().decode())
            tag = data.get("tag_name", "").lstrip("v")
            body = data.get("body", "")
            html_url = data.get("html_url", "https://github.com/EvgeniyKrasnyanskiy/xDripWidget/releases")

            has_update = (tag != "" and tag != self._current_version and tag > self._current_version)
            logger.debug(f"UpdateCheckerWorker result: tag={tag}, has_update={has_update}")
            self.update_result.emit(has_update, tag, body, html_url)
        except Exception as exc:
            logger.error(f"UpdateCheckerWorker error: {exc}")
            self.error_signal.emit(str(exc))


EVENT_TYPES_MAP: dict[str, str] = {
    "Приём пищи (Углеводы + Инсулин)": "Meal Bolus",
    "Коррекция инсулином": "Correction Bolus",
    "Перекус / Углеводы": "Carb Intake",
    "Замер сахара крови": "BG Check",
    "Заметка": "Note",
}


# ---------------------------------------------------------------------------
# Treatments dialog (Insulin, Carbs, Blood Glucose input + datetime)
# ---------------------------------------------------------------------------
class TreatmentDialog(QDialog):
    def __init__(self, base_url: str, api_secret: str = "", parent=None):
        super().__init__(parent)
        self.setWindowTitle("Ввод данных терапии")
        self.setModal(True)
        self.resize(380, 300)
        self._base_url = base_url
        self._api_secret = api_secret

        # --- Glucose BG (optional) ---
        self._glucose_spin = QDoubleSpinBox()
        self._glucose_spin.setRange(0, 30.0)
        self._glucose_spin.setDecimals(1)
        self._glucose_spin.setSuffix(" ммоль/л")
        self._glucose_spin.setSpecialValueText("Не указано")

        # --- Carbs ---
        self._carbs_spin = QDoubleSpinBox()
        self._carbs_spin.setRange(0, 500)
        self._carbs_spin.setDecimals(1)
        self._carbs_spin.setSuffix(" г")

        # --- Insulin ---
        self._insulin_spin = QDoubleSpinBox()
        self._insulin_spin.setRange(0, 100)
        self._insulin_spin.setDecimals(2)
        self._insulin_spin.setSuffix(" ЕД")

        # --- Event type ---
        self._event_type_combo = QComboBox()
        self._event_type_combo.addItems(list(EVENT_TYPES_MAP.keys()))

        # --- Notes ---
        self._notes_edit = QLineEdit()
        self._notes_edit.setPlaceholderText("Необязательно")

        # --- DateTime ---
        self._datetime_edit = QDateTimeEdit()
        self._datetime_edit.setDisplayFormat("dd.MM.yyyy  HH:mm")
        self._datetime_edit.setDateTime(QDateTime.currentDateTime())
        self._datetime_edit.setCalendarPopup(True)

        form = QFormLayout()
        form.addRow("Тип события:", self._event_type_combo)
        form.addRow("Глюкоза крови:", self._glucose_spin)
        form.addRow("Углеводы:", self._carbs_spin)
        form.addRow("Инсулин:", self._insulin_spin)
        form.addRow("Дата/Время:", self._datetime_edit)
        form.addRow("Заметка:", self._notes_edit)

        buttons = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel
        )
        buttons.accepted.connect(self._submit)
        buttons.rejected.connect(self.reject)

        layout = QVBoxLayout(self)
        layout.addLayout(form)
        layout.addWidget(buttons)

        self._event_type_combo.currentTextChanged.connect(self._on_event_type_changed)

    def _on_event_type_changed(self, label: str):
        event_type = EVENT_TYPES_MAP.get(label, "Meal Bolus")
        if event_type == "BG Check":
            self._carbs_spin.setEnabled(False)
            self._carbs_spin.setValue(0)
            self._insulin_spin.setEnabled(False)
            self._insulin_spin.setValue(0)
            self._glucose_spin.setEnabled(True)
        elif event_type == "Note":
            self._carbs_spin.setEnabled(False)
            self._carbs_spin.setValue(0)
            self._insulin_spin.setEnabled(False)
            self._insulin_spin.setValue(0)
            self._glucose_spin.setEnabled(False)
            self._glucose_spin.setValue(0)
        elif event_type == "Correction Bolus":
            self._carbs_spin.setEnabled(False)
            self._carbs_spin.setValue(0)
            self._insulin_spin.setEnabled(True)
            self._glucose_spin.setEnabled(True)
        else:  # Meal Bolus / Carb Intake
            self._carbs_spin.setEnabled(True)
            self._insulin_spin.setEnabled(True)
            self._glucose_spin.setEnabled(True)

    def _submit(self):
        carbs   = self._carbs_spin.value()
        insulin = self._insulin_spin.value()
        glucose = self._glucose_spin.value()
        event_label = self._event_type_combo.currentText()
        event_type = EVENT_TYPES_MAP.get(event_label, "Meal Bolus")
        notes = self._notes_edit.text().strip()

        if event_type == "Note" and not notes:
            QMessageBox.warning(self, "Внимание", "Для типа 'Заметка' введите текст заметки.")
            return

        if carbs <= 0 and insulin <= 0 and glucose <= 0 and not notes:
            QMessageBox.warning(self, "Внимание", "Укажите хотя бы одно значение: глюкоза, углеводы, инсулин или заметку.")
            return

        qdt = self._datetime_edit.dateTime()
        ts = qdt.toSecsSinceEpoch()
        item_uuid = str(uuid.uuid4())

        payload: dict = {
            "uuid": item_uuid,
            "_id": item_uuid,
            "eventType": event_type,
            "carbs": carbs,
            "insulin": insulin,
            "notes": notes,
            "created_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(ts)),
            "date": ts * 1000,
        }
        if glucose > 0:
            payload["glucose"] = glucose
            payload["units"] = "mmol"

        url = self._base_url.rstrip("/") + "/api/v1/treatments"
        if self._api_secret:
            url += f"?token={self._api_secret}"

        logger.debug(f"Submitting treatment to {url}: {payload}")
        try:
            req_data = json.dumps(payload).encode("utf-8")
            req = urllib.request.Request(
                url, data=req_data,
                headers={"Content-Type": "application/json", "Accept": "application/json"}
            )
            with urllib.request.urlopen(req, timeout=8) as resp:
                pass
            logger.info(f"Treatment submitted successfully: {event_type}")
            QMessageBox.information(self, "Успешно", "Данные отправлены на сервер!")
            self.accept()
        except Exception as e:
            logger.error(f"Failed to submit treatment: {e}")
            QMessageBox.critical(self, "Ошибка", f"Не удалось отправить данные:\n{e}")


REVERSE_EVENT_TYPES_MAP: dict[str, str] = {
    "Meal Bolus": "Приём пищи",
    "Correction Bolus": "Коррекция",
    "Carb Intake": "Перекус",
    "BG Check": "Замер сахара",
    "Note": "Заметка",
}


# ---------------------------------------------------------------------------
# Treatment History Viewer & Deletion Dialog
# ---------------------------------------------------------------------------
class TreatmentHistoryDialog(QDialog):
    def __init__(self, base_url: str, api_secret: str = "", parent=None):
        super().__init__(parent)
        self.setWindowTitle("История терапий (Удаление с сервера)")
        self.setModal(True)
        self.resize(640, 340)
        self._base_url = base_url
        self._api_secret = api_secret

        self._table = QTableWidget()
        self._table.setColumnCount(6)
        self._table.setHorizontalHeaderLabels(["Дата / Время", "Тип события", "Глюкоза", "Углеводы", "Инсулин", "Действие"])
        self._table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        self._table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)

        btn_refresh = QPushButton("Обновить список")
        btn_refresh.clicked.connect(self._load_treatments)
        btn_close = QPushButton("Закрыть")
        btn_close.clicked.connect(self.accept)

        btn_layout = QHBoxLayout()
        btn_layout.addWidget(btn_refresh)
        btn_layout.addStretch()
        btn_layout.addWidget(btn_close)

        layout = QVBoxLayout(self)
        layout.addWidget(self._table)
        layout.addLayout(btn_layout)

        self._load_treatments()

    def _load_treatments(self):
        url = self._base_url.rstrip("/") + "/api/v1/treatments?limit=40"
        if self._api_secret:
            url += f"&token={self._api_secret}"

        try:
            req = urllib.request.Request(url, headers={"Accept": "application/json"})
            with urllib.request.urlopen(req, timeout=8) as resp:
                items = json.loads(resp.read().decode())
            self._populate_table(items)
        except Exception as e:
            logger.error(f"Error loading treatments history: {e}")
            QMessageBox.warning(self, "Ошибка", f"Не удалось загрузить историю терапий:\n{e}")

    def _populate_table(self, items: list):
        self._table.setRowCount(0)
        row_idx = 0
        for item in items:
            if item.get("isVoided") or item.get("eventType") == "Void":
                continue
            self._table.insertRow(row_idx)

            ts_ms = item.get("mills") or item.get("date") or 0
            ts = ts_ms // 1000 if ts_ms > 1e10 else (item.get("timestamp") or int(time.time()))
            dt_str = datetime.fromtimestamp(ts).strftime("%d.%m.%Y %H:%M")

            raw_event_type = item.get("eventType", "")
            event_type_ru = REVERSE_EVENT_TYPES_MAP.get(raw_event_type, raw_event_type)

            notes = item.get("notes", "")
            type_str = f"{event_type_ru}" + (f" ({notes})" if notes else "")

            # Format glucose value into dedicated column
            glucose_str = "—"
            raw_glucose = item.get("glucose")
            if raw_glucose is not None:
                try:
                    g_val = float(raw_glucose)
                    if g_val > 0:
                        units = str(item.get("units", "")).lower()
                        if "mg" in units or g_val > 35.0:
                            g_mmol = round(g_val / 18.0182, 1)
                        else:
                            g_mmol = round(g_val, 1)
                        glucose_str = f"{g_mmol:.1f} ммоль/л"
                except (ValueError, TypeError):
                    pass

            carbs = item.get("carbs", 0.0)
            carbs_str = f"{carbs:.1f} г" if carbs > 0 else "—"

            insulin = item.get("insulin", 0.0)
            insulin_str = f"{insulin:.2f} ЕД" if insulin > 0 else "—"

            item_uuid = str(item.get("uuid") or item.get("_id") or item.get("id", ""))

            self._table.setItem(row_idx, 0, QTableWidgetItem(dt_str))
            self._table.setItem(row_idx, 1, QTableWidgetItem(type_str))
            self._table.setItem(row_idx, 2, QTableWidgetItem(glucose_str))
            self._table.setItem(row_idx, 3, QTableWidgetItem(carbs_str))
            self._table.setItem(row_idx, 4, QTableWidgetItem(insulin_str))

            btn_del = QPushButton("Удалить")
            btn_del.setStyleSheet("background-color: #e74c3c; color: white; border-radius: 3px; font-weight: bold;")
            btn_del.clicked.connect(lambda _, uid=item_uuid, c=carbs, i=insulin: self._delete_item(uid, c, i))
            self._table.setCellWidget(row_idx, 5, btn_del)
            row_idx += 1

    def _delete_item(self, item_uuid: str, carbs: float, insulin: float):
        if not item_uuid:
            QMessageBox.warning(self, "Ошибка", "У данной записи отсутствует UUID/ID.")
            return

        msg = f"Вы действительно хотите навсегда удалить эту запись с сервера?\n\nУглеводы: {carbs}г  |  Инсулин: {insulin}ЕД\nUUID: {item_uuid}"
        reply = QMessageBox.question(
            self, "Подтверждение удаления", msg,
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
            QMessageBox.StandardButton.No,
        )
        if reply != QMessageBox.StandardButton.Yes:
            return

        url = self._base_url.rstrip("/") + f"/api/v1/treatments/{item_uuid}"
        if self._api_secret:
            url += f"?token={self._api_secret}"

        try:
            req = urllib.request.Request(url, method="DELETE")
            with urllib.request.urlopen(req, timeout=8) as resp:
                pass
            logger.info(f"Treatment deleted via dialog: uuid={item_uuid}")
            QMessageBox.information(
                self, "Удалено",
                "Запись терапии успешно удалена с сервера!\nВ xDrip+ она исчезнет при следующем опросе."
            )
            self._load_treatments()
        except Exception as e:
            logger.error(f"Failed to delete treatment via dialog: {e}")
            QMessageBox.critical(self, "Ошибка", f"Не удалось удалить запись с сервера:\n{e}")


# ---------------------------------------------------------------------------
# Settings dialog
# ---------------------------------------------------------------------------
class SettingsDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Настройки виджета")
        self.setModal(True)
        self.resize(360, 185)

        s = get_settings()
        self._url_edit    = QLineEdit(str(s.value("server_url", DEFAULT_URL)))
        self._secret_edit = QLineEdit(str(s.value("api_secret", "")))
        self._secret_edit.setEchoMode(QLineEdit.EchoMode.Password)

        opacity_val = int(s.value("opacity", 90))
        self._opacity_slider = QSlider(Qt.Orientation.Horizontal)
        self._opacity_slider.setRange(30, 100)
        self._opacity_slider.setValue(opacity_val)
        self._opacity_slider.setTickInterval(10)
        self._opacity_slider.setTickPosition(QSlider.TickPosition.TicksBelow)
        self._opacity_label = QLabel(f"{opacity_val}%")
        self._opacity_label.setMinimumWidth(36)
        self._opacity_slider.valueChanged.connect(self._on_opacity_change)

        opacity_row = QHBoxLayout()
        opacity_row.addWidget(self._opacity_slider)
        opacity_row.addWidget(self._opacity_label)

        form = QFormLayout()
        form.addRow("URL сервера:", self._url_edit)
        form.addRow("API Secret:",  self._secret_edit)
        form.addRow("Прозрачность:", opacity_row)

        buttons = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel
        )
        buttons.accepted.connect(self._save)
        buttons.rejected.connect(self.reject)

        layout = QVBoxLayout(self)
        layout.addLayout(form)
        layout.addWidget(buttons)

    def _on_opacity_change(self, value: int):
        self._opacity_label.setText(f"{value}%")
        if self.parent():
            self.parent().setWindowOpacity(value / 100.0)

    def _save(self):
        s = get_settings()
        s.setValue("server_url", self._url_edit.text().strip())
        s.setValue("api_secret",  self._secret_edit.text().strip())
        s.setValue("opacity",     self._opacity_slider.value())
        s.sync()
        logger.info("Settings saved in SettingsDialog")
        self.accept()


# ---------------------------------------------------------------------------
# About dialog
# ---------------------------------------------------------------------------
class AboutDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("О программе")
        self.setModal(True)
        self.resize(370, 270)

        text = QTextBrowser()
        text.setOpenExternalLinks(True)
        text.setHtml(f"""
            <h2 style="margin:0 0 4px 0">{APP_NAME} &nbsp; v{APP_VERSION}</h2>
            <p style="margin:0 0 8px 0; color:#888">
                Ультра-лёгкий десктопный виджет мониторинга глюкозы крови.
            </p>
            <p><b>Совместим с:</b> xDrip+, AAPS (AndroidAPS)<br>
               <b>Протокол:</b> Nightscout REST API<br>
               <b>Файл настроек:</b> config.ini (hot-reload)</p>
            <p><b>Пороги оповещений:</b><br>
               🔴 Гипо:&nbsp;&nbsp;&nbsp;&nbsp; &lt; {ALERT_HYPO} ммоль/л<br>
               🟡 Гипер:&nbsp;&nbsp;&nbsp; &gt; {ALERT_HYPER} ммоль/л<br>
               ⛔ Критично: &gt; {ALERT_CRITICAL} ммоль/л</p>
            <p><a href="https://github.com/EvgeniyKrasnyanskiy/xDripWidget">
               ⬡ GitHub: EvgeniyKrasnyanskiy/xDripWidget</a></p>
        """)
        text.setReadOnly(True)

        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok)
        btn_update = buttons.addButton("Проверить обновления…", QDialogButtonBox.ButtonRole.ActionRole)
        btn_update.clicked.connect(self._check_updates)
        buttons.accepted.connect(self.accept)

        layout = QVBoxLayout(self)
        layout.addWidget(text)
        layout.addWidget(buttons)

    def _check_updates(self):
        parent = self.parent()
        if parent and hasattr(parent, "_check_updates"):
            parent._check_updates(interactive=True)


# ---------------------------------------------------------------------------
# Main widget window
# ---------------------------------------------------------------------------
class GlucoseWidget(QWidget):
    def __init__(self):
        super().__init__()
        self._drag_pos: Optional[QPoint] = None
        self._data: Optional[dict] = None
        self._history: list[dict] = []
        self._error: Optional[str] = None
        self._worker: Optional[FetchWorker] = None
        self._update_worker: Optional[UpdateCheckerWorker] = None
        self._last_alerts: dict[str, float] = {}
        self._is_quitting: bool = False
        self._config_mtime: float = 0.0

        self._setup_ui()
        self._setup_tray()
        self._start_polling()

    # ------------------------------------------------------------------
    # UI setup
    # ------------------------------------------------------------------
    def _setup_ui(self):
        self.setWindowTitle(APP_NAME)
        self.setWindowFlags(
            Qt.WindowType.FramelessWindowHint
            | Qt.WindowType.WindowStaysOnTopHint
            | Qt.WindowType.Tool
        )
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        self.setFixedSize(220, 145)

        s = get_settings()
        self.move(s.value("position", QPoint(100, 100)))
        self.setWindowOpacity(int(s.value("opacity", 90)) / 100.0)

        self._font_big = QFont("Segoe UI", 28, QFont.Weight.Bold)
        self._font_med = QFont("Segoe UI", 12, QFont.Weight.Normal)
        self._font_sml = QFont("Segoe UI",  9, QFont.Weight.Normal)

        if os.path.exists(CONFIG_FILE):
            self._config_mtime = os.path.getmtime(CONFIG_FILE)

    # ------------------------------------------------------------------
    # Tray icon
    # ------------------------------------------------------------------
    def _setup_tray(self):
        self._tray = QSystemTrayIcon(self)
        self._update_tray_icon(COLOR_GRAY)
        self._tray.setToolTip(APP_NAME)
        self._tray.setContextMenu(self._build_tray_menu())
        self._tray.activated.connect(self._tray_activated)
        self._tray.show()

    def _update_tray_icon(self, color: QColor):
        self._tray.setIcon(create_blood_drop_icon(color))

    def _build_tray_menu(self) -> QMenu:
        menu = QMenu()
        for label, slot in [
            ("Показать / скрыть",          self._toggle_visibility),
            ("Обновить сейчас",            self._fetch),
            ("Ввести данные терапии",      self._open_treatments),
            ("История / Удаление терапий", self._open_treatment_history),
        ]:
            a = QAction(label, self)
            a.triggered.connect(slot)
            menu.addAction(a)
        menu.addSeparator()
        for label, slot in [
            ("Настройки…",   self._open_settings),
            ("О программе",  self._open_about),
        ]:
            a = QAction(label, self)
            a.triggered.connect(slot)
            menu.addAction(a)
        menu.addSeparator()
        a_quit = QAction("Выход", self)
        a_quit.triggered.connect(self._confirm_and_quit)
        menu.addAction(a_quit)
        return menu

    # ------------------------------------------------------------------
    # Polling & Hot-Reload
    # ------------------------------------------------------------------
    def _start_polling(self):
        logger.info("Starting polling timer")
        self._fetch()
        self._timer = QTimer(self)
        self._timer.timeout.connect(self._on_poll_timer)
        self._timer.start(POLL_INTERVAL_MS)

    def _on_poll_timer(self):
        self._check_config_hot_reload()
        self._fetch()

    def _check_config_hot_reload(self):
        if not os.path.exists(CONFIG_FILE):
            return
        try:
            mtime = os.path.getmtime(CONFIG_FILE)
            if mtime > self._config_mtime:
                self._config_mtime = mtime
                logger.info("Detected config.ini modification, reloading settings...")
                s = get_settings()
                self.setWindowOpacity(int(s.value("opacity", 90)) / 100.0)
        except Exception as e:
            logger.error(f"Hot reload check error: {e}")

    def _fetch(self):
        logger.debug("_fetch() invoked")

        if self._worker is not None:
            if self._worker.isRunning():
                logger.debug("_fetch(): previous worker is still running, cancelling it")
                self._worker.cancel()
                try:
                    self._worker.data_ready.disconnect()
                    self._worker.fetch_error.disconnect()
                except Exception:
                    pass
                return
            else:
                logger.debug("_fetch(): cleaning up finished worker reference")
                self._worker = None

        s      = get_settings()
        url    = str(s.value("server_url", DEFAULT_URL))
        secret = str(s.value("api_secret", ""))

        worker = FetchWorker(url, secret)
        worker.data_ready.connect(self._on_data)
        worker.fetch_error.connect(self._on_error)
        worker.finished.connect(self._on_fetch_worker_finished)
        self._worker = worker
        worker.start()

    def _on_fetch_worker_finished(self):
        logger.debug("_on_fetch_worker_finished")
        if self._worker is not None:
            w = self._worker
            self._worker = None
            w.deleteLater()

    def _on_data(self, data: dict, history: list):
        logger.debug(f"_on_data received: mmol={data.get('mmol')}")
        self._data    = data
        self._history = history
        self._error   = None
        self._check_alerts(data)
        self.update()

        mmol = data.get("mmol", 0.0)
        minutes_ago = data.get("minutes_ago", 999)
        stale = minutes_ago > STALE_MINUTES
        color = glucose_color(mmol, stale)
        self._update_tray_icon(color)
        self._update_tray_tooltip()

    def _on_error(self, msg: str):
        logger.warning(f"_on_error: {msg}")
        self._error = msg
        self._update_tray_icon(COLOR_GRAY)
        self.update()

    # ------------------------------------------------------------------
    # Glucose alerts
    # ------------------------------------------------------------------
    def _check_alerts(self, data: dict):
        mmol: float      = data.get("mmol", 0.0)
        minutes_ago: int = data.get("minutes_ago", 999)

        if minutes_ago > STALE_MINUTES:
            return

        now = time.time()

        def can_alert(key: str) -> bool:
            return (now - self._last_alerts.get(key, 0.0)) >= ALERT_COOLDOWN_S

        if mmol > ALERT_CRITICAL and can_alert("critical"):
            self._last_alerts["critical"] = now
            logger.info(f"Triggering critical alert: {mmol:.1f}")
            self._tray.showMessage(
                "⛔ Критически высокий сахар!",
                f"{mmol:.1f} ммоль/л — немедленно примите меры!",
                QSystemTrayIcon.MessageIcon.Critical, 12_000,
            )
        elif mmol > ALERT_HYPER and can_alert("hyper"):
            self._last_alerts["hyper"] = now
            logger.info(f"Triggering hyper alert: {mmol:.1f}")
            self._tray.showMessage(
                "🟡 Высокий сахар",
                f"{mmol:.1f} ммоль/л — выше нормы.",
                QSystemTrayIcon.MessageIcon.Warning, 8_000,
            )
        elif mmol < ALERT_HYPO and can_alert("hypo"):
            self._last_alerts["hypo"] = now
            logger.info(f"Triggering hypo alert: {mmol:.1f}")
            self._tray.showMessage(
                "🔴 Низкий сахар!",
                f"{mmol:.1f} ммоль/л — опасная гипогликемия!",
                QSystemTrayIcon.MessageIcon.Critical, 12_000,
            )

    # ------------------------------------------------------------------
    # GitHub Updates
    # ------------------------------------------------------------------
    def _check_updates(self, interactive: bool = False):
        logger.debug(f"_check_updates(interactive={interactive})")

        if self._update_worker is not None:
            if self._update_worker.isRunning():
                logger.debug("_check_updates: update worker is already running")
                return
            else:
                self._update_worker = None

        worker = UpdateCheckerWorker(APP_VERSION)
        worker.update_result.connect(
            lambda has_upd, tag, body, url: self._on_update_result(has_upd, tag, body, url, interactive)
        )
        if interactive:
            worker.error_signal.connect(
                lambda err: QMessageBox.warning(
                    self, "Проверка обновлений",
                    f"Не удалось проверить обновления:\n{err}"
                )
            )
        worker.finished.connect(self._on_update_worker_finished)
        self._update_worker = worker
        worker.start()

    def _on_update_worker_finished(self):
        logger.debug("_on_update_worker_finished")
        if self._update_worker is not None:
            w = self._update_worker
            self._update_worker = None
            w.deleteLater()

    def _on_update_result(self, has_update: bool, tag: str, body: str, url: str, interactive: bool):
        logger.info(f"Update check result: has_update={has_update}, tag={tag}")
        if has_update:
            msg = f"Доступна новая версия: v{tag}\n\n{body[:300]}"
            reply = QMessageBox.information(
                self, "Доступно обновление!", msg,
                QMessageBox.StandardButton.Open | QMessageBox.StandardButton.Close,
                QMessageBox.StandardButton.Open
            )
            if reply == QMessageBox.StandardButton.Open:
                import webbrowser
                webbrowser.open(url)
        elif interactive:
            QMessageBox.information(
                self, "Обновлений не найдено",
                f"У вас установлена последняя версия {APP_VERSION}."
            )

    # ------------------------------------------------------------------
    # Painting
    # ------------------------------------------------------------------
    def paintEvent(self, _event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)

        path = QPainterPath()
        path.addRoundedRect(0, 0, self.width(), self.height(), 14, 14)
        painter.fillPath(path, COLOR_BG)

        if self._error or not self._data:
            painter.setPen(COLOR_GRAY)
            painter.setFont(self._font_med)
            painter.drawText(
                0, 0, self.width(), 80,
                Qt.AlignmentFlag.AlignCenter,
                self._error or "Загрузка…",
            )
            return

        d = self._data
        mmol: float       = d.get("mmol", 0.0)
        direction: str    = d.get("direction", "Unknown")
        delta: str        = d.get("delta", "?")
        battery: int      = d.get("battery", -1)
        minutes_ago: int  = d.get("minutes_ago", 0)

        stale = minutes_ago > STALE_MINUTES
        color = glucose_color(mmol, stale)
        arrow = TREND_ARROWS.get(direction, "?")

        # ── Glucose + arrow ───────────────────────────────────────────
        painter.setPen(color)
        painter.setFont(self._font_big)
        painter.drawText(
            0, 4, self.width() - 8, 48,
            Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter,
            f"{mmol:.1f} {arrow}",
        )

        # ── Delta ─────────────────────────────────────────────────────
        painter.setPen(COLOR_SUB)
        painter.setFont(self._font_med)
        painter.drawText(
            8, 4, 80, 48,
            Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter,
            f"Δ {delta}",
        )

        # ── Battery & Time ───────────────────────────────────────────
        self._draw_battery_bar(painter, battery, stale)

        time_color = COLOR_GRAY if stale else COLOR_SUB
        painter.setPen(time_color)
        painter.setFont(self._font_sml)
        painter.drawText(
            130, 50, 84, 25,
            Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter,
            f"{minutes_ago}м назад" if minutes_ago < 60 else ">1ч назад",
        )

        # ── Top accent line ───────────────────────────────────────────
        painter.setPen(QPen(color, 2))
        painter.drawLine(14, 2, self.width() - 14, 2)

        # ── 4-hour trend sparkline ────────────────────────────────────
        self._draw_sparkline(painter)

    def _draw_battery_bar(self, painter: QPainter, pct: int, stale: bool):
        BAR_X, BAR_Y = 6,  54
        BAR_W, BAR_H = 72, 13
        CAP_W, CAP_H = 4,   6
        RADIUS = 2

        b_color = COLOR_GRAY if (pct < 0 or stale) else battery_color(pct)

        painter.setPen(QPen(COLOR_SUB, 1))
        painter.setBrush(QBrush(Qt.BrushStyle.NoBrush))
        painter.drawRoundedRect(BAR_X, BAR_Y, BAR_W, BAR_H, RADIUS, RADIUS)

        if pct > 0:
            fill_w = max(2, int((BAR_W - 4) * min(pct, 100) / 100))
            painter.setPen(Qt.PenStyle.NoPen)
            painter.setBrush(QBrush(b_color))
            painter.drawRoundedRect(
                BAR_X + 2, BAR_Y + 2,
                fill_w, BAR_H - 4,
                1, 1,
            )

        cap_x = BAR_X + BAR_W + 2
        cap_y = BAR_Y + (BAR_H - CAP_H) // 2
        painter.setPen(Qt.PenStyle.NoPen)
        painter.setBrush(QBrush(COLOR_SUB))
        painter.drawRoundedRect(cap_x, cap_y, CAP_W, CAP_H, 1, 1)

        label = f"{pct}%" if pct >= 0 else "—"
        painter.setPen(b_color)
        painter.setFont(self._font_sml)
        painter.drawText(
            BAR_X + BAR_W + CAP_W + 5, 50,
            38, 25,
            Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter,
            label,
        )

    def _draw_sparkline(self, painter: QPainter):
        if not self._history or len(self._history) < 2:
            return

        GX, GY, GW, GH = 10, 85, 200, 48

        min_val = 2.5
        max_val = 14.0
        for r in self._history:
            v = float(r.get("mmol", 5.5))
            if v < min_val:
                min_val = max(1.5, v - 0.5)
            if v > max_val:
                max_val = min(22.0, v + 1.0)

        val_range = max(0.1, max_val - min_val)

        def val_to_y(v: float) -> float:
            ratio = (v - min_val) / val_range
            return GY + GH - (ratio * GH)

        y_lo = val_to_y(3.9)
        y_hi = val_to_y(9.0)
        painter.setPen(QPen(QColor(255, 255, 255, 35), 1, Qt.PenStyle.DashLine))
        if GY <= y_lo <= GY + GH:
            painter.drawLine(GX, int(y_lo), GX + GW, int(y_lo))
        if GY <= y_hi <= GY + GH:
            painter.drawLine(GX, int(y_hi), GX + GW, int(y_hi))

        t_start = self._history[0].get("timestamp", 0)
        t_end   = self._history[-1].get("timestamp", t_start)
        t_span  = max(1, t_end - t_start)

        points = []
        for r in self._history:
            ts = r.get("timestamp", t_start)
            v  = float(r.get("mmol", 5.5))
            px = GX + int((ts - t_start) / t_span * GW)
            py = int(val_to_y(v))
            dot_color = glucose_color(v, stale=False)
            points.append((px, py, dot_color))

        painter.setPen(QPen(QColor(200, 200, 200, 70), 1.2))
        for i in range(len(points) - 1):
            p1, p2 = points[i], points[i + 1]
            painter.drawLine(p1[0], p1[1], p2[0], p2[1])

        for px, py, dot_color in points:
            painter.setPen(QPen(dot_color.darker(120), 1))
            painter.setBrush(QBrush(dot_color))
            painter.drawEllipse(QPoint(px, py), 3, 3)

    # ------------------------------------------------------------------
    # Drag & Mouse
    # ------------------------------------------------------------------
    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            self._drag_pos = event.globalPosition().toPoint() - self.pos()
        elif event.button() == Qt.MouseButton.RightButton:
            self._show_context_menu(event.globalPosition().toPoint())

    def mouseMoveEvent(self, event):
        if self._drag_pos and event.buttons() == Qt.MouseButton.LeftButton:
            self.move(event.globalPosition().toPoint() - self._drag_pos)

    def mouseReleaseEvent(self, _event):
        self._drag_pos = None
        try:
            s = get_settings()
            s.setValue("position", self.pos())
            s.sync()
        except Exception as e:
            logger.error(f"Error saving widget position: {e}")

    # ------------------------------------------------------------------
    # Context menu
    # ------------------------------------------------------------------
    def _show_context_menu(self, global_pos: QPoint):
        menu = QMenu(self)
        menu.setWindowFlags(menu.windowFlags() | Qt.WindowType.FramelessWindowHint)
        menu.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)

        entries = [
            ("Обновить сейчас",            self._fetch),
            ("Ввести данные терапии",      self._open_treatments),
            ("История / Удаление терапий", self._open_treatment_history),
            ("Свернуть в трей",            self._hide_to_tray),
            None,
            ("Настройки…",                 self._open_settings),
            ("О программе",                self._open_about),
            None,
            ("Выход",                      self._confirm_and_quit),
        ]
        for item in entries:
            if item is None:
                menu.addSeparator()
            else:
                label, slot = item
                a = QAction(label, self)
                a.triggered.connect(slot)
                menu.addAction(a)

        menu.exec(global_pos)

    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------
    def _toggle_visibility(self):
        if self.isVisible():
            self.hide()
        else:
            self.show()
            self.raise_()

    def _hide_to_tray(self):
        self.hide()
        self._tray.showMessage(
            APP_NAME, "Виджет свёрнут в трей",
            QSystemTrayIcon.MessageIcon.Information, 2000,
        )

    def _tray_activated(self, reason):
        if reason == QSystemTrayIcon.ActivationReason.DoubleClick:
            self._toggle_visibility()

    def _open_settings(self):
        dlg = SettingsDialog(self)
        dlg.exec()
        s = get_settings()
        self.setWindowOpacity(int(s.value("opacity", 90)) / 100.0)
        if dlg.result() == QDialog.DialogCode.Accepted:
            self._fetch()

    def _open_treatments(self):
        s = get_settings()
        url = str(s.value("server_url", DEFAULT_URL))
        secret = str(s.value("api_secret", ""))
        dlg = TreatmentDialog(url, secret, self)
        dlg.exec()

    def _open_treatment_history(self):
        s = get_settings()
        url = str(s.value("server_url", DEFAULT_URL))
        secret = str(s.value("api_secret", ""))
        dlg = TreatmentHistoryDialog(url, secret, self)
        dlg.exec()

    def _open_about(self):
        AboutDialog(self).exec()

    def _confirm_and_quit(self):
        reply = QMessageBox.question(
            self,
            "Выход из программы",
            "Вы действительно хотите выйти из xDrip Widget?",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
            QMessageBox.StandardButton.No,
        )
        if reply == QMessageBox.StandardButton.Yes:
            logger.info("User confirmed application exit")
            self._is_quitting = True
            QApplication.quit()

    def _update_tray_tooltip(self):
        if not self._data:
            return
        d = self._data
        tip = (f"{d.get('mmol', '?')} ммоль/л  "
               f"{TREND_ARROWS.get(d.get('direction', ''), '?')}  "
               f"{d.get('minutes_ago', '?')}м назад")
        self._tray.setToolTip(f"{APP_NAME}\n{tip}")

    def closeEvent(self, event):
        if self._is_quitting:
            event.accept()
        else:
            event.ignore()
            self._hide_to_tray()


# ---------------------------------------------------------------------------
# Single-instance protection
# ---------------------------------------------------------------------------
def _ensure_single_instance() -> Optional[QLocalServer]:
    sock = QLocalSocket()
    sock.connectToServer(INSTANCE_KEY)
    if sock.waitForConnected(400):
        sock.disconnectFromServer()
        logger.info(f"{APP_NAME} is already running.")
        print(f"{APP_NAME} is already running.")
        sys.exit(0)

    server = QLocalServer()
    QLocalServer.removeServer(INSTANCE_KEY)
    server.listen(INSTANCE_KEY)
    return server


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------
def main():
    app = QApplication(sys.argv)
    app.setApplicationName(APP_NAME)
    app.setOrganizationName(ORG_NAME)
    app.setQuitOnLastWindowClosed(False)

    _server = _ensure_single_instance()

    widget = GlucoseWidget()
    widget.show()

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
