import math
import logging
import time
from pathlib import Path

from PySide6.QtCore import Qt, QThread, QTimer, Signal, QUrl
from PySide6.QtGui import QAction, QColor, QIcon, QPainter, QPen, QPixmap, QDesktopServices
from PySide6.QtWidgets import (
    QApplication, QCheckBox, QComboBox, QFileDialog, QFrame, QHBoxLayout,
    QLabel, QListWidget, QListWidgetItem, QMainWindow, QMenu, QMessageBox,
    QProgressBar, QPushButton, QStackedWidget, QSystemTrayIcon, QTextEdit,
    QVBoxLayout, QWidget, QScrollArea,
)

from .audio import Recorder
from .audio_service import AudioService
from .diagnostics import configure_logging, close_logging
from .help_text import MICROPHONE_HELP
from . import __version__

from .domain import HOTKEYS, LANGUAGES, MAX_RECORDING_SECONDS, MODELS, Settings, can_insert
from .transcriber import Transcriber
from .windows import DesktopBridge, IS_WINDOWS

logger = logging.getLogger("avanegar.ui")

STYLE = """
QWidget { font-family: 'Segoe UI', 'Tahoma'; font-size: 14px; color: #20343d; }
QMainWindow, QWidget#Page { background: #f3f6f5; }
QFrame#Sidebar { background: #142e35; border-radius: 18px; }
QFrame#Sidebar QLabel { color: #ebf6f2; background: transparent; }
QLabel#Brand { font-size: 29px; font-weight: 700; }
QLabel#Title { font-size: 27px; font-weight: 700; }
QLabel#Subtitle { color: #72838a; font-size: 13px; }
QLabel#Section { font-size: 17px; font-weight: 600; }
QLabel#Badge { color: #197b64; background: #e1f2e9; border-radius: 12px; padding: 7px 12px; font-size: 12px; }
QLabel#Status { font-size: 21px; font-weight: 600; }
QLabel#Timer { color: #62777c; font-size: 18px; }
QFrame#Card { background: white; border: 1px solid #e1e8e5; border-radius: 16px; }
QPushButton { background: #e8eeeb; border: none; border-radius: 9px; padding: 11px 18px; font-weight: 600; }
QPushButton:hover { background: #dbe6e0; }
QPushButton:disabled { color: #94a29b; background: #edf1ef; }
QPushButton#Primary { background: #197b64; color: white; padding: 14px 28px; font-size: 16px; }
QPushButton#Primary:hover { background: #12614e; }
QPushButton#Primary[recording="true"] { background: #bd4d50; }
QPushButton#Nav { text-align: right; background: transparent; color: #b3c9c8; padding: 14px 18px; }
QPushButton#Nav:checked { background: #29484d; color: #80ddba; }
QPushButton#Nav:hover { background: #203e45; }
QTextEdit, QListWidget { background: white; border: 1px solid #e2e9e5; border-radius: 10px; padding: 10px; selection-background-color: #b3e1d0; }
QTextEdit { font-size: 16px; }
QListWidget::item { padding: 9px; border-bottom: 1px solid #eef2f0; }
QListWidget::item:selected { background: #e1f2e9; color: #185e50; }
QComboBox { background: white; border: 1px solid #d8e3dd; border-radius: 8px; padding: 10px; min-width: 200px; }
QComboBox QAbstractItemView { background: white; selection-background-color: #c5e8dc; }
QCheckBox { spacing: 10px; }
QProgressBar { border: none; background: #e8eeeb; border-radius: 3px; max-height: 5px; }
QProgressBar::chunk { background: #40a98a; border-radius: 3px; }
QToolTip { background: #142e35; color: white; border: none; padding: 8px; }
"""


def label(text, name=None, wrap=False):
    widget = QLabel(text)
    widget.setTextFormat(Qt.TextFormat.PlainText)
    if name:
        widget.setObjectName(name)
    widget.setWordWrap(wrap)
    return widget


def app_icon():
    pixmap = QPixmap(64, 64)
    pixmap.fill(Qt.GlobalColor.transparent)
    painter = QPainter(pixmap)
    painter.setRenderHint(QPainter.RenderHint.Antialiasing)
    painter.setPen(Qt.PenStyle.NoPen)
    painter.setBrush(QColor("#197b64"))
    painter.drawRoundedRect(0, 0, 64, 64, 17, 17)
    painter.setPen(QPen(QColor("#e8fff4"), 5, Qt.PenStyle.SolidLine, Qt.PenCapStyle.RoundCap))
    for x, h in [(16, 12), (27, 28), (38, 38), (49, 18)]:
        painter.drawLine(x, 32 - h // 2, x, 32 + h // 2)
    painter.end()
    return QIcon(pixmap)


class Waveform(QWidget):
    def __init__(self):
        super().__init__()
        self.setFixedHeight(68)
        self.level = 0.0
        self.active = False
        self.phase = 0

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        count, gap = 39, 8
        x0 = (self.width() - (count - 1) * gap) / 2
        for i in range(count):
            envelope = math.sin(math.pi * (i + 1) / (count + 1))
            fluctuation = abs(math.sin(i * 1.8 + self.phase))
            height = 5 + envelope * (12 + 42 * self.level * fluctuation) if self.active else 5 + 12 * envelope * abs(math.sin(i * 1.8))
            painter.setPen(QPen(QColor("#34a989" if self.active else "#bbd7ca"), 4, Qt.PenStyle.SolidLine, Qt.PenCapStyle.RoundCap))
            painter.drawLine(int(x0 + i * gap), int(34 - height / 2), int(x0 + i * gap), int(34 + height / 2))


class MainWindow(QMainWindow):
    transcribe = Signal(object, str, str)

    def __init__(self, directory: Path):
        super().__init__()
        self.directory = directory
        self.log_buffer = configure_logging(directory)
        self.log_revision = -1
        self.phase_started = time.monotonic()
        self.last_heartbeat = 0.0
        self.audio_session = 0
        self.recording_from_hotkey = False
        self.settings_path = directory / "settings.json"
        self.settings = Settings.load(self.settings_path)
        self.state = "idle"
        self.target = 0
        self.discard_result = False
        self.pending_quit = False
        self.started_at = 0.0
        self.audio = AudioService()
        self.recorder = self.audio.recorder
        self.audio.started.connect(self._audio_started, Qt.ConnectionType.QueuedConnection)
        self.audio.stopped.connect(self._audio_stopped, Qt.ConnectionType.QueuedConnection)
        self.audio.failed.connect(self._audio_failed, Qt.ConnectionType.QueuedConnection)
        self.setWindowTitle("آوانگار | تبدیل گفتار به متن")
        self.setWindowIcon(app_icon())
        self.setMinimumSize(820, 480)
        screen = QApplication.primaryScreen().availableGeometry()
        self.resize(min(1080, screen.width()), min(820, screen.height() - 50))
        self.setLayoutDirection(Qt.LayoutDirection.RightToLeft)
        self.setStyleSheet(STYLE)
        self._build_ui()
        self.bridge = DesktopBridge(QApplication.instance(), self._hotkey_press, self._hotkey_release, self._hotkey_cancel)
        self.hotkey_registered = self.bridge.register(self.settings.hotkey)
        self._update_shortcut_status()
        self.worker_thread = QThread(self)
        self.worker = Transcriber(directory / "models")
        self.worker.moveToThread(self.worker_thread)
        self.transcribe.connect(self.worker.run)
        self.worker.progress.connect(self._progress)
        self.worker.completed.connect(self._completed)
        self.worker.failed.connect(self._failed)
        self.worker_thread.finished.connect(self.worker.deleteLater)
        self.worker_thread.start()
        self.timer = QTimer(self)
        self.timer.setInterval(80)
        self.timer.timeout.connect(self._tick)
        self.timer.start()
        self.log_timer = QTimer(self)
        self.log_timer.setInterval(500)
        self.log_timer.timeout.connect(self._refresh_log)
        self.log_timer.start()
        self._refresh_log()
        self.paste_timer = QTimer(self)
        self.paste_timer.setInterval(50)
        self.paste_timer.timeout.connect(self._try_paste)
        self.tray = None
        self._setup_tray()
        if not IS_WINDOWS:
            self.notice.setText("حالت پیش‌نمایش: میان‌بر سراسری و درج خودکار فقط در ویندوز فعال‌اند.")
        elif not self.hotkey_registered:
            self.notice.setText("میان‌بر در اختیار برنامهٔ دیگری است. از تنظیمات، میان‌بر دیگری انتخاب کنید.")

    def _build_ui(self):
        root = QWidget()
        layout = QHBoxLayout(root)
        layout.setContentsMargins(18, 18, 18, 18)
        layout.setSpacing(24)
        sidebar = QFrame()
        sidebar.setObjectName("Sidebar")
        sidebar.setFixedWidth(210)
        side = QVBoxLayout(sidebar)
        side.setContentsMargins(18, 30, 18, 22)
        side.setSpacing(10)
        side.addWidget(label("آوانگار", "Brand"))
        side.addWidget(label("دستیار نوشتن با صدا"))
        side.addSpacing(38)
        self.nav = []
        for index, title in enumerate(["تبدیل صدا", "تنظیمات", "راهنمای شروع", "گزارش خطا / Log"]):
            button = QPushButton(title)
            button.setObjectName("Nav")
            button.setCheckable(True)
            button.clicked.connect(lambda checked=False, i=index: self._navigate(i))
            side.addWidget(button)
            self.nav.append(button)
        side.addStretch()
        side.addWidget(label("خصوصی، روی دستگاه شما", wrap=True))
        privacy = label("بدون ارسال صدا به سرور\nبدون نیاز به کلید API", wrap=True)
        privacy.setStyleSheet("color: #9bb7b8; font-size: 12px; line-height: 1.5;")
        side.addWidget(privacy)
        side.addSpacing(22)
        side.addWidget(label(f"AVANEGAR  /  {__version__}", "Subtitle"))
        layout.addWidget(sidebar)
        self.pages = QStackedWidget()
        for page in (self._dictation_page(), self._settings_page(), self._help_page(), self._log_page()):
            scroll = QScrollArea()
            scroll.setWidgetResizable(True)
            scroll.setFrameShape(QFrame.Shape.NoFrame)
            scroll.setWidget(page)
            self.pages.addWidget(scroll)
        layout.addWidget(self.pages, 1)
        self.setCentralWidget(root)
        self._navigate(0)

    def _page(self, title, subtitle):
        page = QWidget()
        page.setObjectName("Page")
        layout = QVBoxLayout(page)
        layout.setContentsMargins(0, 8, 6, 6)
        layout.setSpacing(16)
        layout.addWidget(label(title, "Title"))
        layout.addWidget(label(subtitle, "Subtitle", True))
        return page, layout

    def _dictation_page(self):
        page, layout = self._page("شما بگویید، آوانگار می‌نویسد.", "از یک فکر کوتاه تا یک پیام بلند؛ کافی‌ست صحبت کنید.")
        card = QFrame()
        card.setObjectName("Card")
        box = QVBoxLayout(card)
        box.setContentsMargins(24, 18, 24, 18)
        badge_row = QHBoxLayout()
        badge_row.addWidget(label("پردازش محلی  •  بدون ارسال صدا", "Badge"))
        badge_row.addStretch()
        self.clock = label("00:00 / 05:00", "Timer")
        self.clock.setLayoutDirection(Qt.LayoutDirection.LeftToRight)
        badge_row.addWidget(self.clock)
        box.addLayout(badge_row)
        self.wave = Waveform()
        box.addWidget(self.wave)
        self.status = label("آمادهٔ شنیدن", "Status", True)
        self.status.setAlignment(Qt.AlignmentFlag.AlignCenter)
        box.addWidget(self.status)
        self.shortcut_label = label("", "Subtitle")
        self.shortcut_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.shortcut_label.setLayoutDirection(Qt.LayoutDirection.LeftToRight)
        box.addWidget(self.shortcut_label)
        buttons = QHBoxLayout()
        buttons.addStretch()
        self.record_button = QPushButton("شروع ضبط")
        self.record_button.setObjectName("Primary")
        self.record_button.clicked.connect(lambda: self.toggle_recording(False))
        buttons.addWidget(self.record_button)
        self.cancel_button = QPushButton("لغو ضبط")
        self.cancel_button.clicked.connect(self.cancel)
        self.cancel_button.setEnabled(False)
        buttons.addWidget(self.cancel_button)
        buttons.addStretch()
        box.addLayout(buttons)
        self.progress_bar = QProgressBar()
        self.progress_bar.setRange(0, 0)
        self.progress_bar.setTextVisible(False)
        self.progress_bar.hide()
        box.addWidget(self.progress_bar)
        layout.addWidget(card)
        self.notice = label("بار اول، در تنظیمات «آماده‌سازی مدل» را بزنید. برای درج در برنامه‌های دیگر از میان‌بر استفاده کنید.", "Subtitle", True)
        layout.addWidget(self.notice)
        heading = QHBoxLayout()
        heading.addWidget(label("متن شما", "Section"))
        heading.addStretch()
        self.copy_button = QPushButton("کپی متن")
        self.copy_button.clicked.connect(self.copy_text)
        self.export_button = QPushButton("ذخیرهٔ متن")
        self.export_button.clicked.connect(self.export_text)
        heading.addWidget(self.copy_button)
        heading.addWidget(self.export_button)
        layout.addLayout(heading)
        self.result = QTextEdit()
        self.result.setAcceptRichText(False)
        self.result.setPlaceholderText("متن تبدیل‌شده اینجا ظاهر می‌شود…\nمی‌توانید پیش از کپی، آن را ویرایش کنید.")
        self.result.setMinimumHeight(115)
        self.result.textChanged.connect(self._text_changed)
        layout.addWidget(self.result, 2)
        history_heading = QHBoxLayout()
        history_heading.addWidget(label("گفتارهای این نشست", "Section"))
        history_heading.addStretch()
        self.clear_button = QPushButton("پاک کردن متن‌ها")
        self.clear_button.clicked.connect(self.clear_texts)
        history_heading.addWidget(self.clear_button)
        layout.addLayout(history_heading)
        self.history = QListWidget()
        self.history.setMaximumHeight(102)
        self.history.setMinimumHeight(65)
        self.history.setToolTip("۱۰ گفتار آخر، فقط در حافظه. برای باز کردن، روی متن کلیک کنید.")
        self.history.itemClicked.connect(lambda item: self.result.setPlainText(item.data(Qt.ItemDataRole.UserRole)))
        layout.addWidget(self.history, 1)
        layout.addWidget(label("صدا ذخیره نمی‌شود. متن‌ها با خروج کامل از برنامه پاک می‌شوند؛ کلیپ‌بورد مستقل از برنامه است.", "Subtitle", True))
        self._text_changed()
        return page

    def _settings_page(self):
        page, layout = self._page("همان‌طور که راحت‌ترید.", "زبان، میکروفون و میان‌بر را متناسب با کار خود انتخاب کنید.")
        card = QFrame()
        card.setObjectName("Card")
        form = QVBoxLayout(card)
        form.setContentsMargins(24, 24, 24, 24)
        form.setSpacing(13)
        self.language_combo = self._combo(form, "زبان گفتار", LANGUAGES, self.settings.language)
        self.model_combo = self._combo(form, "مدل تشخیص گفتار", MODELS, self.settings.model)
        form.addWidget(label("Base: حدود ۱۵۰ مگابایت • Small: حدود ۵۰۰ مگابایت • Medium: حدود ۱٫۵ گیگابایت\nSmall برای شروع فارسی پیشنهاد می‌شود. مدل بزرگ‌تر به حافظه و زمان بیشتری نیاز دارد.", "Subtitle", True))
        self.device_combo = self._combo(form, "میکروفون", {"": "پیش‌فرض ویندوز"}, "")
        self.device_combo.setMinimumWidth(0)
        self.device_combo.setSizeAdjustPolicy(QComboBox.SizeAdjustPolicy.AdjustToMinimumContentsLengthWithIcon)
        self.device_combo.setMinimumContentsLength(24)
        self.refresh_button = QPushButton("بازخوانی میکروفون‌ها")
        self.refresh_button.clicked.connect(lambda: self._refresh_devices())
        form.addWidget(self.refresh_button)
        self._refresh_devices(initial=True)
        self.hotkey_combo = self._combo(form, "میان‌بر سراسری · نگه دارید و صحبت کنید", {k: v[0] for k, v in HOTKEYS.items()}, self.settings.hotkey)
        self.hotkey_combo.setLayoutDirection(Qt.LayoutDirection.LeftToRight)
        self.auto_paste = QCheckBox("درج خودکار در پنجرهٔ مقصد پس از استفاده از میان‌بر")
        self.auto_paste.setChecked(self.settings.auto_paste)
        form.addWidget(self.auto_paste)
        form.addWidget(label("درج خودکار، متن را در کلیپ‌بورد می‌گذارد و Ctrl+V می‌فرستد. اگر پنجره عوض شود، متن فقط در آوانگار نمایش داده می‌شود.", "Subtitle", True))
        actions = QHBoxLayout()
        self.save_button = QPushButton("ذخیرهٔ تنظیمات")
        self.save_button.setObjectName("Primary")
        self.save_button.clicked.connect(self.save_settings)
        self.prepare_button = QPushButton("آماده‌سازی مدل")
        self.prepare_button.clicked.connect(self.prepare_model)
        actions.addWidget(self.save_button)
        actions.addWidget(self.prepare_button)
        form.addLayout(actions)
        layout.addWidget(card)
        self.settings_notice = label("پس از دریافت اولیه، مدل بدون اینترنت کار می‌کند. مدل در پوشهٔ داده‌های محلی ویندوز نگهداری می‌شود.", "Subtitle", True)
        layout.addWidget(self.settings_notice)
        layout.addStretch()
        return page

    @staticmethod
    def _combo(form, title, options, current):
        form.addWidget(label(title, "Section"))
        combo = QComboBox()
        for key, text in options.items():
            combo.addItem(text, key)
        combo.setCurrentIndex(max(0, combo.findData(current)))
        form.addWidget(combo)
        return combo

    def _help_page(self):
        page, layout = self._page("سه قدم تا نوشتن با صدا", "برای Windows 10 و 11، نسخهٔ ۶۴ بیتی")
        for title, text in [
            ("۱  /  یک‌بار آماده شوید", "میکروفون را وصل کنید. در تنظیمات ویندوز، دسترسی برنامه‌های دسکتاپ به میکروفون را روشن کنید. سپس در تنظیمات آوانگار، مدل را آماده کنید؛ این مرحله برای اولین بار به اینترنت نیاز دارد."),
            ("۲  /  نشانگر را جای متن بگذارید", "در Word، مرورگر، پیام‌رسان یا هر کادر متنی استاندارد کلیک کنید. کلیدهای Ctrl + Win را با هم نگه دارید. پس از نمایش «دارم می‌شنوم» صحبت کنید؛ با رها کردن یکی از کلیدها ضبط تمام و تبدیل آغاز می‌شود. فشار دوباره برای پایان لازم نیست. دکمهٔ داخل برنامه همچنان با کلیک شروع و با کلیک دوم پایان می‌دهد. هر ضبط تا ۵ دقیقه است."),
            ("۳  /  کمی صبر کنید؛ متن درج می‌شود", "تا پایان تبدیل، پنجرهٔ مقصد را عوض نکنید. اگر درج خودکار خاموش باشد یا پنجره عوض شده باشد، متن در آوانگار آمادهٔ کپی است. ضبط با دکمهٔ داخل برنامه نیز فقط متن را در آوانگار نمایش می‌دهد."),
            ("چند نکتهٔ کاربردی", "بستن پنجره، برنامه را کنار ساعت ویندوز نگه می‌دارد (در صورت وجود سینی سیستم). برای خروج کامل، از منوی آیکن کنار ساعت «خروج» را بزنید. برای پنجره‌های دارای دسترسی Administrator، صفحهٔ ورود ویندوز و بعضی بازی‌ها، درج خودکار تضمین نمی‌شود. متن را پیش از ارسال بازبینی کنید."),
        ]:
            card = QFrame()
            card.setObjectName("Card")
            box = QVBoxLayout(card)
            box.setContentsMargins(20, 17, 20, 17)
            box.addWidget(label(title, "Section"))
            box.addWidget(label(text, None, True))
            layout.addWidget(card)
        layout.addWidget(label("حریم خصوصی: صدا فقط در حافظه پردازش می‌شود. تاریخچه روی دیسک ذخیره نمی‌شود. متن کپی‌شده ممکن است در تاریخچه یا همگام‌سازی کلیپ‌بورد ویندوز باقی بماند.", "Subtitle", True))
        permission_card = QFrame()
        permission_card.setObjectName("Card")
        permission_layout = QVBoxLayout(permission_card)
        permission_layout.setContentsMargins(20, 20, 20, 20)
        permission_layout.addWidget(label("مجوز میکروفون؛ مرحله‌به‌مرحله", "Section"))
        permission_layout.addWidget(label(MICROPHONE_HELP, None, True))
        permission_button = QPushButton("باز کردن تنظیمات مجوز میکروفون ویندوز")
        permission_button.clicked.connect(self.open_microphone_settings)
        permission_layout.addWidget(permission_button)
        layout.addWidget(permission_card)
        layout.addStretch()
        return page

    def open_microphone_settings(self):
        if not IS_WINDOWS or not QDesktopServices.openUrl(QUrl("ms-settings:privacy-microphone")):
            self.notice.setText("Win + R را بزنید و ms-settings:privacy-microphone را وارد کنید.")
            logger.warning("Could not open Windows microphone settings URI")

    def _log_page(self):
        page, layout = self._page("گزارش خطا / Log", "برای بررسی مشکل، پس از بروز خطا دکمهٔ «کپی لاگ» را بزنید و گزارش را بفرستید.")
        actions = QHBoxLayout()
        self.copy_log_button = QPushButton("کپی لاگ")
        self.copy_log_button.setObjectName("Primary")
        self.copy_log_button.clicked.connect(self.copy_log)
        actions.addWidget(self.copy_log_button)
        refresh = QPushButton("بازخوانی لاگ")
        refresh.clicked.connect(lambda: self._refresh_log(force=True))
        actions.addWidget(refresh)
        actions.addStretch()
        layout.addLayout(actions)
        self.log_view = QTextEdit()
        self.log_view.setReadOnly(True)
        self.log_view.setLayoutDirection(Qt.LayoutDirection.LeftToRight)
        self.log_view.setLineWrapMode(QTextEdit.LineWrapMode.NoWrap)
        self.log_view.setMinimumHeight(320)
        self.log_view.document().setMaximumBlockCount(1000)
        layout.addWidget(self.log_view, 1)
        self.log_notice = label("لاگ شامل زمان، نسخه، مرحلهٔ عملیات و جزئیات خطاست؛ صدا، متن گفتار و کلیپ‌بورد ثبت نمی‌شوند. پیش از ارسال، گزارش را بازبینی کنید.", "Subtitle", True)
        layout.addWidget(self.log_notice)
        path = label(r"فایل پایدار: %LOCALAPPDATA%\Avanegar\logs\avanegar.log", "Subtitle", True)
        path.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        layout.addWidget(path)
        layout.addWidget(label("اگر مجبور شدید برنامه را ببندید، پس از باز کردن دوباره، لاگ نشست قبلی هم نمایش داده می‌شود. حداکثر ۳ فایل تقریباً ۱ مگابایتی نگهداری می‌شود.", "Subtitle", True))
        return page

    def _refresh_log(self, force=False):
        revision, text = self.log_buffer.snapshot()
        if force or revision != self.log_revision:
            bar = self.log_view.verticalScrollBar()
            at_end, position = bar.value() >= bar.maximum() - 2, bar.value()
            self.log_view.setPlainText(text)
            bar.setValue(bar.maximum() if at_end else position)
            self.log_revision = revision

    def copy_log(self):
        self._refresh_log(force=True)
        QApplication.clipboard().setText(self.log_view.toPlainText())
        self.log_notice.setText("لاگ کپی شد. پیش از ارسال آن را مرور کنید؛ فایل صوتی و متن گفتار در این گزارش نیست.")

    def _navigate(self, index):
        self.pages.setCurrentIndex(index)
        for i, button in enumerate(self.nav):
            button.setChecked(i == index)

    def _refresh_devices(self, initial=False):
        selected = self.settings.device_name if initial else self.device_combo.currentData()
        self.device_combo.clear()
        self.device_combo.addItem("پیش‌فرض ویندوز", "")
        try:
            seen = set()
            for _, name in Recorder.devices():
                if name not in seen:
                    self.device_combo.addItem(name, name)
                    seen.add(name)
        except Exception:
            logger.exception("Microphone enumeration failed")
            self.device_combo.setToolTip("میکروفونی پیدا نشد؛ اتصال و مجوزهای ویندوز را بررسی کنید.")
        if selected and self.device_combo.findData(selected) == -1:
            self.device_combo.addItem(f"{selected} (متصل نیست)", selected)
        self.device_combo.setCurrentIndex(max(0, self.device_combo.findData(selected)))

    def _update_shortcut_status(self):
        suffix = "" if self.hotkey_registered else "  —  غیرفعال"
        self.shortcut_label.setText(HOTKEYS[self.settings.hotkey][0] + suffix)

    def save_settings(self):
        if self.state != "idle":
            return False
        new = Settings(
            model=self.model_combo.currentData(), language=self.language_combo.currentData(),
            device_name=self.device_combo.currentData(), hotkey=self.hotkey_combo.currentData(),
            auto_paste=self.auto_paste.isChecked(),
        )
        old_hotkey = self.settings.hotkey
        if IS_WINDOWS and not self.bridge.register(new.hotkey):
            self.hotkey_registered = self.bridge.registered
            self._update_shortcut_status()
            self.settings_notice.setText("این میان‌بر قابل ثبت نیست. میان‌بر دیگری انتخاب کنید؛ تنظیمات ذخیره نشد.")
            return False
        try:
            new.save(self.settings_path)
        except OSError as exc:
            if IS_WINDOWS:
                self.bridge.register(old_hotkey)
                self.hotkey_registered = self.bridge.registered
                self._update_shortcut_status()
            self.settings_notice.setText(f"ذخیرهٔ تنظیمات انجام نشد: {exc}")
            return False
        self.settings = new
        self.hotkey_registered = self.bridge.registered
        self._update_shortcut_status()
        self.settings_notice.setText("تنظیمات ذخیره شد. آماده‌سازی مدل را می‌توانید پیش از اولین ضبط انجام دهید.")
        return True

    def _setup_tray(self):
        if not QSystemTrayIcon.isSystemTrayAvailable():
            return
        self.tray = QSystemTrayIcon(self.windowIcon(), self)
        self.tray.setToolTip("آوانگار · آمادهٔ ضبط")
        menu = QMenu(self)
        show_action = QAction("نمایش آوانگار", self)
        show_action.triggered.connect(self.show_window)
        menu.addAction(show_action)
        menu.addSeparator()
        quit_action = QAction("خروج", self)
        quit_action.triggered.connect(self.request_quit)
        menu.addAction(quit_action)
        self.tray.setContextMenu(menu)
        self.tray.activated.connect(lambda reason: self.show_window() if reason in (QSystemTrayIcon.ActivationReason.Trigger, QSystemTrayIcon.ActivationReason.DoubleClick) else None)
        self.tray.show()

    def show_window(self):
        self.showNormal()
        self.raise_()
        self.activateWindow()

    def _notify(self, message):
        if self.tray:
            self.tray.showMessage("آوانگار", message, QSystemTrayIcon.MessageIcon.Information, 2500)

    def _hotkey_press(self):
        if self.state == "idle":
            self.toggle_recording(True)
        else:
            logger.info("Hold press ignored while state=%s", self.state)

    def _hotkey_release(self):
        if self.recording_from_hotkey and self.state in ("starting", "recording"):
            self._finish_recording()

    def _hotkey_cancel(self):
        if self.recording_from_hotkey:
            self.cancel()

    def _set_state(self, state):
        logger.info("State %s -> %s (session=%s)", self.state, state, self.audio_session)
        self.state = state
        self.phase_started = time.monotonic()
        self.last_heartbeat = self.phase_started
        recording = state == "recording"
        busy = state in ("starting", "stopping", "processing", "pasting", "audio_stalled")
        self.record_button.setEnabled(state in ("idle", "recording"))
        self.record_button.setText("پایان ضبط و تبدیل" if recording else "شروع ضبط")
        self.record_button.setProperty("recording", recording)
        self.record_button.style().unpolish(self.record_button)
        self.record_button.style().polish(self.record_button)
        self.cancel_button.setEnabled(state in ("starting", "recording", "stopping"))
        for widget in (self.save_button, self.prepare_button, self.model_combo, self.language_combo, self.device_combo, self.hotkey_combo, self.auto_paste, self.refresh_button):
            widget.setEnabled(state == "idle")
        self.progress_bar.setVisible(busy)
        self.wave.active = recording
        self.wave.update()
        if state == "idle":
            self.recording_from_hotkey = False
        if self.tray:
            self.tray.setToolTip("آوانگار · " + {
                "idle": "آمادهٔ ضبط", "starting": "در حال باز کردن میکروفون",
                "recording": "در حال ضبط", "stopping": "در حال بستن میکروفون",
                "processing": "در حال تبدیل", "pasting": "در حال درج",
                "audio_stalled": "میکروفون پاسخ نمی‌دهد؛ لاگ را بررسی کنید",
            }[state])

    def toggle_recording(self, from_hotkey=False):
        if self.state == "recording":
            self._finish_recording()
        elif self.state == "idle":
            self.target = self.bridge.foreground() if from_hotkey else 0
            if self.bridge.is_own_window(self.target):
                self.target = 0
            self.audio_session += 1
            self.discard_result = False
            self.recording_from_hotkey = from_hotkey
            self._set_state("starting")
            self.status.setText("در حال باز کردن میکروفون…")
            self.notice.setText("کلیدها را نگه دارید؛ پس از آماده شدن میکروفون صحبت کنید." if from_hotkey else "در حال آماده شدن میکروفون…")
            self.audio.start(self.audio_session, self.settings.device_name)

    def _audio_started(self, session):
        if session != self.audio_session or self.state != "starting":
            return  # Release may have arrived while the driver was still opening.
        self.started_at = time.monotonic()
        self._set_state("recording")
        self.status.setText("دارم می‌شنوم…")
        self.notice.setText("کلیدهای میان‌بر را نگه دارید و صحبت کنید؛ با رها کردن، ضبط پایان می‌یابد." if self.recording_from_hotkey else "برای پایان، دکمهٔ «پایان ضبط و تبدیل» را بزنید.")
        self._notify("ضبط شروع شد؛ برای پایان کلیدها را رها کنید." if self.recording_from_hotkey else "ضبط شروع شد.")

    def _tick(self):
        now = time.monotonic()
        if self.state in ("starting", "stopping", "processing", "audio_stalled"):
            elapsed = int(now - self.phase_started)
            self.clock.setText(f"{elapsed // 60:02}:{elapsed % 60:02}")
            if now - self.last_heartbeat >= 5:
                logger.info("Waiting: stage=%s elapsed=%ss", self.state, elapsed)
                self.last_heartbeat = now
            if self.state in ("starting", "stopping") and elapsed >= 10:
                logger.error("Microphone driver timeout: stage=%s session=%s", self.state, self.audio_session)
                self.audio.log_stack()
                self.discard_result = True
                self.target = 0
                if self.state == "starting":
                    self.audio.stop(self.audio_session)
                self._set_state("audio_stalled")
                self.status.setText("میکروفون پاسخ نمی‌دهد")
                self.notice.setText("رابط برنامه فعال است. از صفحهٔ Log گزارش را کپی کنید. اگر درایور برنگشت، از منوی کنار ساعت خارج شوید و میکروفون را دوباره وصل کنید.")
            return
        if self.state != "recording":
            return
        elapsed = int(now - self.started_at)
        self.clock.setText(f"{elapsed // 60:02}:{elapsed % 60:02} / 05:00")
        self.wave.level = self.recorder.level
        self.wave.phase += 0.45
        self.wave.update()
        if elapsed >= MAX_RECORDING_SECONDS or self.recorder.limit_reached:
            self._finish_recording()

    def _finish_recording(self):
        if self.state not in ("starting", "recording"):
            return
        self._set_state("stopping")
        self.status.setText("در حال پایان ضبط…")
        self.notice.setText("ضبط صدا متوقف شد؛ در حال بستن میکروفون در پس‌زمینه…")
        self.audio.stop(self.audio_session)

    def _audio_stopped(self, session, clip, had_overflow):
        if session != self.audio_session or self.state not in ("stopping", "audio_stalled"):
            return
        if self.discard_result:
            self.target = 0
            self._set_state("idle")
            self.status.setText("ضبط لغو شد")
            self.notice.setText("صدای این ضبط حذف شد و تبدیل نمی‌شود.")
            return
        self._set_state("processing")
        self.status.setText("در حال تبدیل…")
        self.notice.setText("بخشی از صدا از دست رفته است؛ متن را بررسی کنید." if had_overflow else "ضبط پایان یافت. تبدیل روی همین دستگاه انجام می‌شود؛ برای درج خودکار در همان پنجره بمانید.")
        self.transcribe.emit(clip, self.settings.model, self.settings.language)

    def _audio_failed(self, session, message):
        if session == self.audio_session:
            self._failed("خطای میکروفون. مجوز را طبق صفحهٔ راهنما بررسی کنید؛ جزئیات در Log است.\n" + message)

    def cancel(self):
        if self.state in ("starting", "recording", "stopping"):
            self.discard_result = True
            self.target = 0
            self._finish_recording()
            self.notice.setText("در حال لغو ضبط و آزاد کردن میکروفون…")

    def prepare_model(self):
        if not self.save_settings():
            return
        self.target = 0
        self.discard_result = False
        self._set_state("processing")
        self.status.setText("آماده‌سازی مدل…")
        self._navigate(0)
        self.transcribe.emit(None, self.settings.model, self.settings.language)

    def _progress(self, message):
        logger.info("Transcription stage: %s", message)
        self.notice.setText(message)

    def _completed(self, text, prepare_only):
        logger.info("Transcription completed: prepare_only=%s has_text=%s", prepare_only, bool(text))
        self._set_state("idle")
        if self.pending_quit:
            self._quit_now()
            return
        if self.discard_result:
            return
        if prepare_only:
            self.status.setText("مدل آماده است")
            self.notice.setText("حالا می‌توانید بدون اینترنت ضبط و تبدیل کنید.")
            return
        if not text:
            self.status.setText("گفتاری تشخیص داده نشد")
            self.notice.setText("نزدیک‌تر به میکروفون صحبت کنید و انتخاب میکروفون را بررسی کنید. متن قبلی تغییر نکرده است.")
            self._notify("گفتاری تشخیص داده نشد.")
            return
        self.result.setPlainText(text)
        item = QListWidgetItem(time.strftime("%H:%M") + "  ·  " + text[:90].replace("\n", " "))
        item.setData(Qt.ItemDataRole.UserRole, text)
        self.history.insertItem(0, item)
        while self.history.count() > 10:
            self.history.takeItem(10)
        self.status.setText("متن شما آماده است")
        if can_insert(self.target, self.bridge.foreground(), self.bridge.is_own_window(self.target), self.settings.auto_paste):
            self.pending_text = text
            self.paste_deadline = time.monotonic() + 2.0
            self._set_state("pasting")
            self.paste_timer.start()
        else:
            self.notice.setText("متن آمادهٔ کپی است. درج خودکار فقط با میان‌بر و در همان پنجرهٔ مقصد انجام می‌شود.")
            self._notify("متن آماده است؛ از آوانگار کپی کنید.")

    def _try_paste(self):
        if not can_insert(self.target, self.bridge.foreground(), self.bridge.is_own_window(self.target), self.settings.auto_paste):
            self._end_paste("پنجرهٔ مقصد عوض شد؛ برای جلوگیری از درج اشتباه، متن فقط در آوانگار نگه داشته شد.")
            return
        if self.bridge.modifiers_down():
            if time.monotonic() > self.paste_deadline:
                self._end_paste("کلیدهای میان‌بر رها نشدند؛ متن آمادهٔ کپی است.")
            return
        QApplication.clipboard().setText(self.pending_text)
        # Check again immediately before injecting the paste keystroke.
        if self.bridge.foreground() != self.target:
            self._end_paste("پنجرهٔ مقصد عوض شد. متن در کلیپ‌بورد آمادهٔ چسباندن است.")
        elif self.bridge.paste():
            self._end_paste("فرمان درج به پنجرهٔ مقصد فرستاده شد. اگر متن درج نشد، با Ctrl+V بچسبانید.")
        else:
            self._end_paste("ویندوز اجازهٔ درج نداد؛ متن در کلیپ‌بورد است. در پنجرهٔ مقصد Ctrl+V بزنید.")

    def _end_paste(self, message):
        self.paste_timer.stop()
        self.pending_text = ""
        self.target = 0
        self._set_state("idle")
        self.notice.setText(message)
        self._notify(message)

    def _failed(self, message):
        logger.error("Operation failed: %s", message)
        self._set_state("idle")
        self.target = 0
        self.status.setText("این بار انجام نشد")
        self.notice.setText(message)
        self._notify("عملیات انجام نشد؛ جزئیات در پنجرهٔ آوانگار است.")
        if self.pending_quit:
            self._quit_now()

    def _text_changed(self):
        has_text = bool(self.result.toPlainText().strip())
        self.copy_button.setEnabled(has_text)
        self.export_button.setEnabled(has_text)

    def copy_text(self):
        QApplication.clipboard().setText(self.result.toPlainText())
        self.notice.setText("متن کپی شد. در محل دلخواه Ctrl+V بزنید.")

    def export_text(self):
        path, _ = QFileDialog.getSaveFileName(self, "ذخیرهٔ متن", "گفتار.txt", "Text files (*.txt)")
        if path:
            try:
                Path(path).write_text(self.result.toPlainText(), encoding="utf-8-sig")
                self.notice.setText("متن ذخیره شد.")
            except OSError as exc:
                self.notice.setText(f"ذخیره انجام نشد: {exc}")

    def clear_texts(self):
        self.history.clear()
        self.result.clear()
        if self.state == "pasting":
            self._end_paste("درج خودکار لغو شد.")
        self.notice.setText("متن‌های این نشست پاک شدند. کلیپ‌بورد ویندوز و فایل‌های ذخیره‌شده تغییر نکردند.")

    def closeEvent(self, event):
        event.ignore()
        if self.tray:
            self.hide()
            self._notify("آوانگار هنوز فعال است. برای خروج کامل از منوی کنار ساعت استفاده کنید.")
        else:
            self.request_quit()

    def request_quit(self):
        if self.state == "processing":
            answer = QMessageBox.question(self, "عملیات در حال اجراست", "پس از پایان عملیات فعلی، بدون درج متن از برنامه خارج شود؟")
            if answer == QMessageBox.StandardButton.Yes:
                self.pending_quit = True
                self.discard_result = True
                self.target = 0
                self.notice.setText("پس از پایان عملیات جاری، برنامه بسته می‌شود…")
            return
        if self.state in ("starting", "recording", "stopping"):
            answer = QMessageBox.question(self, "ضبط در حال اجراست", "ضبط فعلی حذف و برنامه بسته شود؟")
            if answer != QMessageBox.StandardButton.Yes:
                return
        self._quit_now()

    def _quit_now(self):
        logger.info("Application exit requested")
        self.timer.stop()
        self.log_timer.stop()
        self.paste_timer.stop()
        self.audio.shutdown()
        self.bridge.close()
        self.worker_thread.quit()
        self.worker_thread.wait()
        if self.tray:
            self.tray.hide()
        close_logging()
        QApplication.instance().quit()
