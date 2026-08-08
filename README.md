# xDripWidget — Glycemia Micro-Backend + Desktop Widget

Ультра-лёгкий Nightscout-совместимый бэкенд (< 20 MB RAM) + десктопный виджет глюкозы.

> **Версия виджета:** 1.7.0 · **Версия бэкенда:** 1.0.0

---

## 🚀 Новое в версии v1.7.0

- **Точный ввод инсулина**: шаг счётчика ввода инсулина изменён на `0.1 ЕД` (углеводы по-прежнему `1 г`).
- **Оптимизация батарейки**: графические индикаторы батареи масштабированы для четкого отображения на Windows и Android.
- **Звуковые тревоги на Android**:
  - Настраиваемые пороги гипогликемии (по умолчанию `< 4.0 ммоль/л`) и гипергликемии (по умолчанию `> 10.0 ммоль/л`).
  - **3-цикловая тревога (5 минут)**: при выходе за пределы нормы звук играет 3 раза по 1 минуте с 1-минутным перерывом.
  - **Глушение по тапу на виджет**: тап по виджету моментально выключает звук и откладывает повтор на **30 минут** (для низкого сахара) или на **60 минут** (для высокого сахара).
  - **Алгоритмический генератор звука**: 3 тональных паттерна без использования mp3/wav файлов (`Siren` по умолчанию, `Beeps`, `Triple Tone`).
  - Регулировка громкости (0–100%) и кнопка «▶ Прослушать» в окне настроек.

---

## 🎨 Цветовая схема порогов глюкозы

| Диапазон (ммоль/л) | Статус | Цвет | Hex |
|---|---|---|---|
| `≤ 3.3` | Тяжелая гипогликемия | 🔴 Ярко-красный | `#e74c3c` |
| `3.4 – 3.8` | Легкая гипогликемия | 🟡 Жёлтый | `#f39c12` |
| `3.9 – 7.8` | Целевая норма | 🟢 Зелёный | `#27ae60` |
| `7.9 – 9.9` | Легкая гипергликемия | 🟡 Жёлтый | `#f39c12` |
| `≥ 10.0` | Гипергликемия | 🔴 Мягкий красный | `#e57373` |

---

## Структура проекта

```
xDripWidget/
├── main.py                     # FastAPI-бэкенд (Nightscout REST API)
├── widget.py                   # PyQt6 десктопный виджет (v1.6.0)
├── config.ini                  # Конфигурация виджета (URL, Secret, прозрачность, логи)
├── requirements.txt            # Зависимости сервера
├── widget_requirements.txt     # Зависимости виджета (PyQt6)
├── glycemia_backend.service    # Systemd unit-файл для Ubuntu VPS
└── data/                       # Создаётся автоматически (glycemia.db)
```

---

## ЧАСТЬ 1 — Деплой бэкенда на VPS (Ubuntu)

### 1. Загрузить файлы

```bash
sudo mkdir -p /opt/glycemia-backend/data
sudo chown -R www-data:www-data /opt/glycemia-backend
cd /opt/glycemia-backend
git clone https://github.com/EvgeniyKrasnyanskiy/xDripWidget.git .
```

### 2. Создать virtualenv и установить зависимости

```bash
python3 -m venv .venv
.venv/bin/pip install --upgrade pip
.venv/bin/pip install -r requirements.txt
```

### 3. Настроить секрет (через systemd override)

```bash
sudo mkdir -p /etc/systemd/system/glycemia_backend.service.d
sudo nano /etc/systemd/system/glycemia_backend.service.d/override.conf
```

Содержимое `override.conf`:
```ini
[Service]
Environment="API_SECRET=ВАШ_СЕКРЕТ_ЗДЕСЬ"
```

> ⚠️ Никогда не вписывайте реальный секрет прямо в `glycemia_backend.service` — используйте override.

### 4. Установить и запустить сервис

```bash
sudo cp glycemia_backend.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now glycemia_backend
sudo systemctl status glycemia_backend
```

### 5. Проверить

```bash
curl http://localhost:8080/health
# {"status":"ok","ts":...}

curl http://localhost:8080/api/v1/status.json
# {"status":"ok","name":"Micro-Nightscout",...}
```

---

## Поддерживаемые эндпоинты бэкенда

| Метод | Путь | Описание |
|---|---|---|
| `GET` / `POST` | `/api/v1/entries` · `/api/v1/entries.json` | Приём и чтение записей глюкозы (SGV / MBG) |
| `POST` | `/api/v1/devicestatus` · `/api/v1/devicestatus.json` | Приём IoB/CoB/заряда батареи от AAPS и xDrip+ |
| `GET` / `POST` / `PUT` / `DELETE` | `/api/v1/treatments` · `/api/v1/treatments.json` | Управление терапиями (углеводы, инсулин, замеры СК, заметки) |
| `GET` | `/api/v1/current` | Текущие данные для виджета (SGV, дельта, IoB, CoB, батарея) |
| `GET` | `/api/v1/history` | История показаний сенсора за N часов для графика виджета |
| `GET` | `/api/v1/status` · `/api/v1/status.json` | Nightscout status probe |
| `GET` | `/health` | Liveness probe |

---

## ЧАСТЬ 2 — Настройка xDrip+ на Android

1. Откройте **Настройки** → **Inter-app settings** (Межпрограммное взаимодействие).
2. Включите **REST API Upload** (переключатель).
3. В поле **Base URL** введите:
   ```
   http://<IP_ВАШЕГО_VPS>:8085
   ```
4. В поле **API Secret** введите секрет, указанный в `API_SECRET` на сервере.
5. Нажмите **Test** — должно появиться уведомление об успешной отправке.

---

## ЧАСТЬ 3 — Десктопный виджет (Windows / macOS / Linux)

### Вариант A: готовый EXE (Windows)

Скачайте `xDripWidget.exe` из раздела [Releases](https://github.com/EvgeniyKrasnyanskiy/xDripWidget/releases).

### Управление и возможности

| Функция | Описание |
|---|---|
| Перетащить (ЛКМ) | Переместить виджет |
| ПКМ → Свернуть в трей | Скрыть виджет |
| ПКМ → Обновить сейчас | Немедленный запрос к серверу |
| ПКМ → Настройки… | URL, Secret, прозрачность (30–100%) |
| ПКМ → О программе | Версия, пороги, ссылка на GitHub |
| ПКМ → Выход | Завершить виджет |
| Двойной клик на трее | Показать / скрыть |

### Что отображается на виджете

```
┌─────────────────────────────┐
│ Δ +0.2          8.9 ↗       │
│ [███████░░░] 24%   2м назад │
└─────────────────────────────┘
```

- **Δ** — дельта (изменение за 5 минут)
- Крупное число — уровень глюкозы ммоль/л + стрелка тренда
- **Батарея** — горизонтальная полоска с заливкой, процент рядом
- Время последнего обновления

### Цветовая индикация глюкозы

| Цвет | Значение |
|---|---|
| 🟢 Зелёный | Норма 3.9 – 11.0 ммоль/л |
| 🟡 Жёлтый | Лёгкая гипо/гипер 3.3–3.9 / 9.0–11.0 |
| 🔴 Красный | Выраженная гипо < 3.3 или гипер > 11.0 |
| ⚫ Серый | Данные устарели (> 15 минут) |

### Цветовая индикация батареи

| Цвет | Уровень |
|---|---|
| 🟢 Зелёный | > 50% |
| 🟡 Жёлтый | 21–50% |
| 🔴 Красный | ≤ 20% |
| ⚫ Серый | Нет данных |

### Оповещения (Windows 10 / 11)

Виджет показывает системные уведомления Windows при выходе за пороги:

| Порог | Тип | Повтор |
|---|---|---|
| < 4.5 ммоль/л | 🔴 Низкий сахар! | не чаще 1 раза в час |
| > 9.0 ммоль/л | 🟡 Высокий сахар | не чаще 1 раза в час |
| > 14.0 ммоль/л | ⛔ Критически высокий! | не чаще 1 раза в час |

> Устаревшие данные (> 15 минут) не вызывают оповещений.

---

## Сборка EXE (PyInstaller)

```bash
python -m venv .venv
.venv\Scripts\pip install PyQt6 pyinstaller
.venv\Scripts\pyinstaller --onefile --windowed --name xDripWidget --hidden-import PyQt6.QtNetwork widget.py
# Результат: dist\xDripWidget.exe
```

---

## Мониторинг памяти сервера

```bash
systemctl status glycemia_backend
ps aux --sort=-%mem | grep uvicorn
# Ожидаемое потребление: 12–20 MB RSS
```

---

## Переменные окружения бэкенда

| Переменная | По умолчанию | Описание |
|---|---|---|
| `API_SECRET` | `changeme` | Секрет авторизации |
| `DB_PATH` | `./data/glycemia.db` | Путь к SQLite базе |
| `PRUNE_HOURS` | `48` | Сколько часов хранить историю |
| `LOG_LEVEL` | `INFO` | Уровень логирования (`DEBUG` для диагностики) |
