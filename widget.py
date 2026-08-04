"""
Glycemia Desktop Widget — PyQt6
Always-on-top translucent overlay, polls /api/v1/current every 60 s.
Works on Windows, macOS and Linux.

Features:
  - Graphical battery bar indicator (horizontal, colored fill)
  - IoB kept in data but hidden from display
  - Glucose alerts: hypo < 4.5, hyper > 9.0, critical > 14.0 mmol/L
  - 1-hour cooldown per alert type
  - Single-instance protection via QLocalServer
  - Opacity control in settings (live preview)
  - "About" dialog

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
    QApplication,
    QDialog,
    QDialogButtonBox,
    QFormLayout,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QMenu,
    QSlider,
    QSystemTrayIcon,
    QTextBrowser,
    QVBoxLayout,
    QWidget,
)

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
APP_NAME    = "xDrip Widget"
APP_VERSION = "1.2.0"
ORG_NAME    = "xdripwidget"
INSTANCE_KEY = "xDripWidgetSingleInstance"
DEFAULT_URL  = "http://localhost:8080"
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


# ---------------------------------------------------------------------------
# Worker thread
# ---------------------------------------------------------------------------
class FetchWorker(QThread):
    data_ready  = pyqtSignal(dict)
    fetch_error = pyqtSignal(str)

    def __init__(self, base_url: str, api_secret: str = ""):
        super().__init__()
        self._base_url   = base_url
        self._api_secret = api_secret

    def run(self):
        url = self._base_url.rstrip("/") + "/api/v1/current"
        if self._api_secret:
            url += f"?token={self._api_secret}"
        try:
            req = urllib.request.Request(url, headers={"Accept": "application/json"})
            with urllib.request.urlopen(req, timeout=10) as resp:
                data = json.loads(resp.read().decode())
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
        self.resize(360, 185)

        s = QSettings(ORG_NAME, APP_NAME)
        self._url_edit    = QLineEdit(s.value("server_url", DEFAULT_URL))
        self._secret_edit = QLineEdit(s.value("api_secret", ""))
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
        s = QSettings(ORG_NAME, APP_NAME)
        s.setValue("server_url", self._url_edit.text().strip())
        s.setValue("api_secret",  self._secret_edit.text().strip())
        s.setValue("opacity",     self._opacity_slider.value())
        self.accept()


# ---------------------------------------------------------------------------
# About dialog
# ---------------------------------------------------------------------------
class AboutDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("О программе")
        self.setModal(True)
        self.resize(350, 240)

        text = QTextBrowser()
        text.setOpenExternalLinks(True)
        text.setHtml(f"""
            <h2 style="margin:0 0 4px 0">{APP_NAME} &nbsp; v{APP_VERSION}</h2>
            <p style="margin:0 0 8px 0; color:#888">
                Ультра-лёгкий десктопный виджет мониторинга глюкозы крови.
            </p>
            <p><b>Совместим с:</b> xDrip+, AAPS (AndroidAPS)<br>
               <b>Протокол:</b> Nightscout REST API<br>
               <b>Обновление:</b> каждые 60 секунд</p>
            <p><b>Пороги оповещений:</b><br>
               🔴 Гипо:&nbsp;&nbsp;&nbsp;&nbsp; &lt; {ALERT_HYPO} ммоль/л<br>
               🟡 Гипер:&nbsp;&nbsp;&nbsp; &gt; {ALERT_HYPER} ммоль/л<br>
               ⛔ Критично: &gt; {ALERT_CRITICAL} ммоль/л<br>
               <i>Повтор не чаще 1 раза в час.</i></p>
            <p><a href="https://github.com/EvgeniyKrasnyanskiy/xDripWidget">
               ⬡ GitHub: EvgeniyKrasnyanskiy/xDripWidget</a></p>
        """)
        text.setReadOnly(True)

        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok)
        buttons.accepted.connect(self.accept)

        layout = QVBoxLayout(self)
        layout.addWidget(text)
        layout.addWidget(buttons)


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
        self._last_alerts: dict[str, float] = {}  # alert_key → unix timestamp

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

        s = QSettings(ORG_NAME, APP_NAME)
        self.move(s.value("position", QPoint(100, 100)))
        self.setWindowOpacity(int(s.value("opacity", 90)) / 100.0)

        self._font_big = QFont("Segoe UI", 28, QFont.Weight.Bold)
        self._font_med = QFont("Segoe UI", 12, QFont.Weight.Normal)
        self._font_sml = QFont("Segoe UI",  9, QFont.Weight.Normal)

    # ------------------------------------------------------------------
    # Tray icon
    # ------------------------------------------------------------------
    def _setup_tray(self):
        self._tray = QSystemTrayIcon(self)
        px = QPixmap(16, 16)
        px.fill(COLOR_GREEN)
        self._tray.setIcon(QIcon(px))
        self._tray.setToolTip(APP_NAME)
        self._tray.setContextMenu(self._build_tray_menu())
        self._tray.activated.connect(self._tray_activated)
        self._tray.show()

    def _build_tray_menu(self) -> QMenu:
        menu = QMenu()
        for label, slot in [
            ("Показать / скрыть", self._toggle_visibility),
            ("Обновить сейчас",   self._fetch),
        ]:
            a = QAction(label, self)
            a.triggered.connect(slot)
            menu.addAction(a)
        menu.addSeparator()
        for label, slot in [
            ("Настройки…",  self._open_settings),
            ("О программе", self._open_about),
        ]:
            a = QAction(label, self)
            a.triggered.connect(slot)
            menu.addAction(a)
        menu.addSeparator()
        a_quit = QAction("Выход", self)
        a_quit.triggered.connect(QApplication.quit)
        menu.addAction(a_quit)
        return menu

    # ------------------------------------------------------------------
    # Polling
    # ------------------------------------------------------------------
    def _start_polling(self):
        self._fetch()
        self._timer = QTimer(self)
        self._timer.timeout.connect(self._fetch)
        self._timer.start(POLL_INTERVAL_MS)

    def _fetch(self):
        if self._worker and self._worker.isRunning():
            return
        s      = QSettings(ORG_NAME, APP_NAME)
        url    = s.value("server_url", DEFAULT_URL)
        secret = s.value("api_secret", "")
        self._worker = FetchWorker(url, secret)
        self._worker.data_ready.connect(self._on_data)
        self._worker.fetch_error.connect(self._on_error)
        self._worker.start()

    def _on_data(self, data: dict):
        self._data  = data
        self._error = None
        self._check_alerts(data)
        self.update()
        self._update_tray_tooltip()

    def _on_error(self, msg: str):
        self._error = msg
        self.update()

    # ------------------------------------------------------------------
    # Glucose alerts
    # ------------------------------------------------------------------
    def _check_alerts(self, data: dict):
        mmol: float      = data.get("mmol", 0.0)
        minutes_ago: int = data.get("minutes_ago", 999)

        if minutes_ago > STALE_MINUTES:
            return  # don't alert on stale data

        now = time.time()

        def can_alert(key: str) -> bool:
            return (now - self._last_alerts.get(key, 0.0)) >= ALERT_COOLDOWN_S

        if mmol > ALERT_CRITICAL and can_alert("critical"):
            self._last_alerts["critical"] = now
            self._tray.showMessage(
                "⛔ Критически высокий сахар!",
                f"{mmol:.1f} ммоль/л — немедленно примите меры!",
                QSystemTrayIcon.MessageIcon.Critical, 12_000,
            )
        elif mmol > ALERT_HYPER and can_alert("hyper"):
            self._last_alerts["hyper"] = now
            self._tray.showMessage(
                "🟡 Высокий сахар",
                f"{mmol:.1f} ммоль/л — выше нормы.",
                QSystemTrayIcon.MessageIcon.Warning, 8_000,
            )
        elif mmol < ALERT_HYPO and can_alert("hypo"):
            self._last_alerts["hypo"] = now
            self._tray.showMessage(
                "🔴 Низкий сахар!",
                f"{mmol:.1f} ммоль/л — опасная гипогликемия!",
                QSystemTrayIcon.MessageIcon.Critical, 12_000,
            )

    # ------------------------------------------------------------------
    # Painting
    # ------------------------------------------------------------------
    def paintEvent(self, _event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)

        # Rounded dark background
        path = QPainterPath()
        path.addRoundedRect(0, 0, self.width(), self.height(), 14, 14)
        painter.fillPath(path, COLOR_BG)

        if self._error or not self._data:
            painter.setPen(COLOR_GRAY)
            painter.setFont(self._font_med)
            painter.drawText(self.rect(), Qt.AlignmentFlag.AlignCenter,
                             self._error or "Загрузка…")
            return

        d = self._data
        mmol: float       = d.get("mmol", 0.0)
        direction: str    = d.get("direction", "Unknown")
        delta: str        = d.get("delta", "?")
        # iob is intentionally NOT displayed but remains in data for future use
        battery: int      = d.get("battery", -1)
        minutes_ago: int  = d.get("minutes_ago", 0)

        stale = minutes_ago > STALE_MINUTES
        color = glucose_color(mmol, stale)
        arrow = TREND_ARROWS.get(direction, "?")

        # ── Glucose + arrow (large, right-aligned) ────────────────────
        painter.setPen(color)
        painter.setFont(self._font_big)
        painter.drawText(
            0, 5, self.width() - 6, 55,
            Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter,
            f"{mmol:.1f} {arrow}",
        )

        # ── Delta (medium, left) ──────────────────────────────────────
        painter.setPen(COLOR_SUB)
        painter.setFont(self._font_med)
        painter.drawText(
            8, 5, 80, 55,
            Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter,
            f"Δ {delta}",
        )

        # ── Bottom row: [battery bar] | time ─────────────────────────
        self._draw_battery_bar(painter, battery, stale)

        time_color = COLOR_GRAY if stale else COLOR_SUB
        painter.setPen(time_color)
        painter.setFont(self._font_sml)
        painter.drawText(
            130, 62, 86, 30,
            Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter,
            f"{minutes_ago}м назад" if minutes_ago < 60 else ">1ч назад",
        )

        # ── Top accent line ───────────────────────────────────────────
        painter.setPen(QPen(color, 2))
        painter.drawLine(14, 2, self.width() - 14, 2)

    def _draw_battery_bar(self, painter: QPainter, pct: int, stale: bool):
        """
        Draw a horizontal battery icon with colored fill.
        Layout: [  body  ][cap]  XX%
                 x=6, w=72, h=14
        """
        BAR_X, BAR_Y = 6,  68
        BAR_W, BAR_H = 72, 14
        CAP_W, CAP_H = 4,   7
        RADIUS = 2

        b_color = COLOR_GRAY if (pct < 0 or stale) else battery_color(pct)

        # Body outline
        painter.setPen(QPen(COLOR_SUB, 1))
        painter.setBrush(QBrush(Qt.BrushStyle.NoBrush))
        painter.drawRoundedRect(BAR_X, BAR_Y, BAR_W, BAR_H, RADIUS, RADIUS)

        # Colored fill
        if pct > 0:
            fill_w = max(2, int((BAR_W - 4) * min(pct, 100) / 100))
            painter.setPen(Qt.PenStyle.NoPen)
            painter.setBrush(QBrush(b_color))
            painter.drawRoundedRect(
                BAR_X + 2, BAR_Y + 2,
                fill_w, BAR_H - 4,
                1, 1,
            )

        # Positive terminal cap (right side)
        cap_x = BAR_X + BAR_W + 2
        cap_y = BAR_Y + (BAR_H - CAP_H) // 2
        painter.setPen(Qt.PenStyle.NoPen)
        painter.setBrush(QBrush(COLOR_SUB))
        painter.drawRoundedRect(cap_x, cap_y, CAP_W, CAP_H, 1, 1)

        # Percentage label
        label = f"{pct}%" if pct >= 0 else "—"
        painter.setPen(b_color)
        painter.setFont(self._font_sml)
        painter.drawText(
            BAR_X + BAR_W + CAP_W + 5, 62,
            38, 30,
            Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter,
            label,
        )

    # ------------------------------------------------------------------
    # Drag
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
        QSettings(ORG_NAME, APP_NAME).setValue("position", self.pos())

    # ------------------------------------------------------------------
    # Context menu (right-click)
    # ------------------------------------------------------------------
    def _show_context_menu(self, global_pos: QPoint):
        menu = QMenu(self)
        menu.setWindowFlags(menu.windowFlags() | Qt.WindowType.FramelessWindowHint)
        menu.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)

        entries = [
            ("Обновить сейчас", self._fetch),
            ("Свернуть в трей", self._hide_to_tray),
            None,
            ("Настройки…",      self._open_settings),
            ("О программе",     self._open_about),
            None,
            ("Выход",           QApplication.quit),
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
        # Always restore the saved opacity (whether Ok or Cancel)
        s = QSettings(ORG_NAME, APP_NAME)
        self.setWindowOpacity(int(s.value("opacity", 90)) / 100.0)
        if dlg.result() == QDialog.DialogCode.Accepted:
            self._fetch()

    def _open_about(self):
        AboutDialog(self).exec()

    def _update_tray_tooltip(self):
        if not self._data:
            return
        d = self._data
        tip = (f"{d.get('mmol', '?')} ммоль/л  "
               f"{TREND_ARROWS.get(d.get('direction', ''), '?')}  "
               f"{d.get('minutes_ago', '?')}м назад")
        self._tray.setToolTip(f"{APP_NAME}\n{tip}")

    def closeEvent(self, event):
        event.ignore()
        self._hide_to_tray()


# ---------------------------------------------------------------------------
# Single-instance protection
# ---------------------------------------------------------------------------
def _ensure_single_instance() -> Optional[QLocalServer]:
    """
    Connect to existing instance → show notification and exit.
    Otherwise → become the named server for future checks.
    """
    sock = QLocalSocket()
    sock.connectToServer(INSTANCE_KEY)
    if sock.waitForConnected(400):
        sock.disconnectFromServer()
        # Could send a "show" command here via socket if desired
        print(f"{APP_NAME} is already running.")
        sys.exit(0)

    server = QLocalServer()
    QLocalServer.removeServer(INSTANCE_KEY)  # clean up after crash
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

    _server = _ensure_single_instance()  # keep reference alive for process lifetime

    widget = GlucoseWidget()
    widget.show()

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
