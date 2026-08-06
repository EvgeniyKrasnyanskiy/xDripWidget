# xDripWidget — Glycemia Micro-Backend + Desktop Widget

Ультра-лёгкий Nightscout-совместимый бэкенд (< 20 MB RAM) + десктопный виджет глюкозы.

> **Версия виджета:** 1.5.1 · **Версия бэкенда:** 1.0.0

---

## Структура проекта

```
xDripWidget/
├── main.py                     # FastAPI-бэкенд (Nightscout REST API)
├── widget.py                   # PyQt6 десктопный виджет (v1.5.1)
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
| **Ввод терапий** | Запись приёма пищи, коррекции, перекуса, замера сахара (ммоль/л) и заметок |
| **История терапий** | Просмотр и удаление терапий с сервера с колонками (Дата, Тип, Глюкоза, Углеводы, Инсулин, Удалить) |
| **Настройка логов** | Переключение уровня логов (`INFO`, `DEBUG`, `WARNING`, `ERROR`) в меню Настройки и горячая перезагрузка `config.ini` |
| **Авто-обновление** | Автоматическая проверка свежих версий на GitHub Releases |

---

## Переменные окружения бэкенда

| Переменная | По умолчанию | Описание |
|---|---|---|
| `API_SECRET` | `changeme` | Секрет авторизации |
| `DB_PATH` | `./data/glycemia.db` | Путь к SQLite базе |
| `PRUNE_HOURS` | `48` | Сколько часов хранить историю |
| `LOG_LEVEL` | `INFO` | Уровень логирования (`DEBUG` для диагностики) |
