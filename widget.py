"""
Glycemia Desktop Widget — PyQt6
Always-on-top translucent overlay, polls /api/v1/current every 60 s.
Works on Windows, macOS and Linux.

Requirements: widget_requirements.txt
"""

import json
import sys
import time
import urllib.error
import urllib.request
from typing import Optional

from PyQt6.QtCore import (
    QPoint,
    QSettings,
    Qt,
    QThread,
    QTimer,
    pyqtSignal,
)
from PyQt6.QtGui import QAction, QColor, QFont, QFontDatabase, QPainter, QPainterPath
from PyQt6.QtWidgets import (
    QApplication,
    QDialog,
    QDialogButtonBox,
    QFormLayout,
    QLabel,
    QLineEdit,
    QMenu,
    QSystemTrayIcon,
    QVBoxLayout,
    QWidget,
)

try:
    from PyQt6.QtGui import QIcon
    import importlib.resources as _ir
    _HAS_ICON = False  # set True if you add an .ico file
except ImportError:
    _HAS_ICON = False

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
APP_NAME = "xDrip Widget"
ORG_NAME = "xdripwidget"
DEFAULT_URL = "http://localhost:8080"
POLL_INTERVAL_MS = 60_000  # 60 seconds

# Thresholds (mmol/L)
HYPO_SEVERE = 3.3
HYPO_MILD = 3.9
HYPER_MILD = 9.0
HYPER_SEVERE = 11.0
STALE_MINUTES = 15

TREND_ARROWS = {
    "DoubleUp": "⇈",
    "SingleUp": "↑",
    "FortyFiveUp": "↗",
    "Flat": "→",
    "FortyFiveDown": "↘",
    "SingleDown": "↓",
    "DoubleDown": "⇊",
    "NOT COMPUTABLE": "?",
    "RATE OUT OF RANGE": "⚡",
    "Unknown": "?",
}

# ---------------------------------------------------------------------------
# Colors
# ---------------------------------------------------------------------------
COLOR_GREEN  = QColor("#27ae60")
COLOR_YELLOW = QColor("#f39c12")
COLOR_RED    = QColor("#e74c3c")
COLOR_GRAY   = QColor("#7f8c8d")
COLOR_BG     = QColor(20, 20, 30, 210)     # dark translucent
COLOR_TEXT   = QColor("#ecf0f1")
COLOR_SUB    = QColor("#bdc3c7")


def glucose_color(mmol: float, stale: bool) -> QColor:
    if stale:
        return COLOR_GRAY
    if mmol < HYPO_SEVERE or mmol > HYPER_SEVERE:
        return COLOR_RED
    if mmol < HYPO_MILD or mmol > HYPER_MILD:
        return COLOR_YELLOW
    return COLOR_GREEN


# ---------------------------------------------------------------------------
# Worker thread — fetches data without blocking UI
# ---------------------------------------------------------------------------
class FetchWorker(QThread):
    data_ready = pyqtSignal(dict)
    fetch_error = pyqtSignal(str)

    def __init__(self, base_url: str, api_secret: str = ""):
        super().__init__()
        self._base_url = base_url
        self._api_secret = api_secret

    def run(self):
        url = self._base_url.rstrip("/") + "/api/v1/current"
        if self._api_secret:
            url += f"?token={self._api_secret}"
        try:
            req = urllib.request.Request(url, headers={"Accept": "application/json"})
            with urllib.request.urlopen(req, timeout=10) as resp:
                raw = resp.read().decode()
                data = json.loads(raw)
            self.data_ready.emit(data)
        except urllib.error.HTTPError as e:
            self.fetch_error.emit(f"HTTP {e.code}")
        except Exception as exc:
            self.fetch_error.emit(str(exc))


# ---------------------------------------------------------------------------
# Settings dialog
# ---------------------------------------------------------------------------
class SettingsDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Настройки виджета")
        self.setModal(True)
        self.resize(340, 140)

        settings = QSettings(ORG_NAME, APP_NAME)
        self._url_edit = QLineEdit(settings.value("server_url", DEFAULT_URL))
        self._secret_edit = QLineEdit(settings.value("api_secret", ""))
        self._secret_edit.setEchoMode(QLineEdit.EchoMode.Password)

        form = QFormLayout()
        form.addRow("URL сервера:", self._url_edit)
        form.addRow("API Secret:", self._secret_edit)

        buttons = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel
        )
        buttons.accepted.connect(self._save)
        buttons.rejected.connect(self.reject)

        layout = QVBoxLayout(self)
        layout.addLayout(form)
        layout.addWidget(buttons)

    def _save(self):
        settings = QSettings(ORG_NAME, APP_NAME)
        settings.setValue("server_url", self._url_edit.text().strip())
        settings.setValue("api_secret", self._secret_edit.text().strip())
        self.accept()


# ---------------------------------------------------------------------------
# Main widget window
# ---------------------------------------------------------------------------
class GlucoseWidget(QWidget):
    def __init__(self):
        super().__init__()
        self._drag_pos: Optional[QPoint] = None
        self._data: Optional[dict] = None
        self._error: Optional[str] = None
        self._worker: Optional[FetchWorker] = None

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
        self.setFixedSize(220, 100)

        settings = QSettings(ORG_NAME, APP_NAME)
        pos = settings.value("position", QPoint(100, 100))
        self.move(pos)

        # Load system font
        font_id = QFontDatabase.addApplicationFont("")  # use system
        self._font_big = QFont("Segoe UI", 30, QFont.Weight.Bold)
        self._font_med = QFont("Segoe UI", 12, QFont.Weight.Normal)
        self._font_sml = QFont("Segoe UI", 9, QFont.Weight.Normal)

    # ------------------------------------------------------------------
    # Tray icon
    # ------------------------------------------------------------------
    def _setup_tray(self):
        self._tray = QSystemTrayIcon(self)
        # Minimal programmatic icon (a colored square)
        from PyQt6.QtGui import QPixmap
        px = QPixmap(16, 16)
        px.fill(COLOR_GREEN)
        self._tray.setIcon(QIcon(px))
        self._tray.setToolTip(APP_NAME)

        menu = QMenu()
        act_show = QAction("Показать / скрыть", self)
        act_show.triggered.connect(self._toggle_visibility)
        act_settings = QAction("Настройки…", self)
        act_settings.triggered.connect(self._open_settings)
        act_quit = QAction("Выход", self)
        act_quit.triggered.connect(QApplication.quit)

        menu.addAction(act_show)
        menu.addSeparator()
        menu.addAction(act_settings)
        menu.addSeparator()
        menu.addAction(act_quit)

        self._tray.setContextMenu(menu)
        self._tray.activated.connect(self._tray_activated)
        self._tray.show()

    # ------------------------------------------------------------------
    # Polling
    # ------------------------------------------------------------------
    def _start_polling(self):
        self._fetch()  # immediate first load
        self._timer = QTimer(self)
        self._timer.timeout.connect(self._fetch)
        self._timer.start(POLL_INTERVAL_MS)

    def _fetch(self):
        if self._worker and self._worker.isRunning():
            return
        settings = QSettings(ORG_NAME, APP_NAME)
        url = settings.value("server_url", DEFAULT_URL)
        secret = settings.value("api_secret", "")
        self._worker = FetchWorker(url, secret)
        self._worker.data_ready.connect(self._on_data)
        self._worker.fetch_error.connect(self._on_error)
        self._worker.start()

    def _on_data(self, data: dict):
        self._data = data
        self._error = None
        self.update()
        self._update_tray_tooltip()

    def _on_error(self, msg: str):
        self._error = msg
        self.update()

    # ------------------------------------------------------------------
    # Painting
    # ------------------------------------------------------------------
    def paintEvent(self, _event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)

        # Rounded background
        path = QPainterPath()
        path.addRoundedRect(0, 0, self.width(), self.height(), 14, 14)
        painter.fillPath(path, COLOR_BG)

        if self._error or not self._data:
            painter.setPen(COLOR_GRAY)
            painter.setFont(self._font_med)
            msg = self._error or "Загрузка…"
            painter.drawText(self.rect(), Qt.AlignmentFlag.AlignCenter, msg)
            return

        d = self._data
        mmol: float = d.get("mmol", 0.0)
        direction: str = d.get("direction", "Unknown")
        delta: str = d.get("delta", "?")
        iob: float = d.get("iob", 0.0)
        minutes_ago: int = d.get("minutes_ago", 0)

        stale = minutes_ago > STALE_MINUTES
        color = glucose_color(mmol, stale)
        arrow = TREND_ARROWS.get(direction, "?")

        # --- Glucose + arrow (large) ---
        painter.setPen(color)
        painter.setFont(self._font_big)
        glucose_text = f"{mmol:.1f} {arrow}"
        painter.drawText(
            0, 5, self.width() - 8, 55,
            Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter,
            glucose_text,
        )

        # --- Delta (medium) ---
        painter.setPen(COLOR_SUB)
        painter.setFont(self._font_med)
        painter.drawText(
            8, 5, 80, 55,
            Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter,
            f"Δ {delta}",
        )

        # --- IoB + time (small bottom row) ---
        painter.setFont(self._font_sml)
        painter.setPen(COLOR_SUB)
        painter.drawText(
            8, 62, self.width() // 2, 30,
            Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter,
            f"IoB {iob:.2f}U",
        )
        time_color = COLOR_GRAY if stale else COLOR_SUB
        painter.setPen(time_color)
        painter.drawText(
            self.width() // 2, 62, self.width() // 2 - 8, 30,
            Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter,
            f"{minutes_ago}м назад" if minutes_ago < 60 else ">1ч назад",
        )

        # Thin colored accent line on top
        painter.setPen(color)
        painter.drawLine(14, 2, self.width() - 14, 2)

    # ------------------------------------------------------------------
    # Drag support
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
        settings = QSettings(ORG_NAME, APP_NAME)
        settings.setValue("position", self.pos())

    # ------------------------------------------------------------------
    # Context menu (right-click)
    # ------------------------------------------------------------------
    def _show_context_menu(self, global_pos: QPoint):
        menu = QMenu(self)
        menu.setWindowFlags(menu.windowFlags() | Qt.WindowType.FramelessWindowHint)
        menu.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)

        act_hide = QAction("Свернуть в трей", self)
        act_hide.triggered.connect(self._hide_to_tray)
        act_settings = QAction("Настройки…", self)
        act_settings.triggered.connect(self._open_settings)
        act_refresh = QAction("Обновить сейчас", self)
        act_refresh.triggered.connect(self._fetch)
        act_quit = QAction("Выход", self)
        act_quit.triggered.connect(QApplication.quit)

        menu.addAction(act_refresh)
        menu.addAction(act_hide)
        menu.addSeparator()
        menu.addAction(act_settings)
        menu.addSeparator()
        menu.addAction(act_quit)
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
        self._tray.showMessage(APP_NAME, "Виджет свёрнут в трей", QSystemTrayIcon.MessageIcon.Information, 2000)

    def _tray_activated(self, reason):
        if reason == QSystemTrayIcon.ActivationReason.DoubleClick:
            self._toggle_visibility()

    def _open_settings(self):
        dlg = SettingsDialog(self)
        if dlg.exec() == QDialog.DialogCode.Accepted:
            self._fetch()  # reload with new settings

    def _update_tray_tooltip(self):
        if not self._data:
            return
        d = self._data
        tip = f"{d.get('mmol', '?')} ммоль/л  {d.get('direction', '')}  {d.get('minutes_ago', '?')}м"
        self._tray.setToolTip(f"{APP_NAME}\n{tip}")

    def closeEvent(self, event):
        event.ignore()
        self._hide_to_tray()


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------
def main():
    app = QApplication(sys.argv)
    app.setApplicationName(APP_NAME)
    app.setOrganizationName(ORG_NAME)
    app.setQuitOnLastWindowClosed(False)

    widget = GlucoseWidget()
    widget.show()

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
