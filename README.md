# xDripWidget — Glycemia Micro-Backend + Desktop Widget

Ультра-лёгкий Nightscout-совместимый бэкенд (< 20 MB RAM) + десктопный виджет глюкозы.

> **Версия виджета:** 1.2.0 · **Версия бэкенда:** 1.0.0

---

## Структура проекта

```
xDripWidget/
├── main.py                     # FastAPI-бэкенд (Nightscout REST API)
├── widget.py                   # PyQt6 десктопный виджет (v1.2.0)
├── requirements.txt            # зависимости сервера
├── widget_requirements.txt     # зависимости виджета (PyQt6)
├── glycemia_backend.service    # systemd unit-файл для Ubuntu VPS
└── data/                       # создаётся автоматически (glycemia.db)
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
# Отредактируйте ExecStart в .service, указав путь к .venv/bin/uvicorn
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

### 6. Опциональный Nginx-прокси (HTTPS)

```nginx
server {
    listen 443 ssl;
    server_name your.domain.com;
    # ssl_certificate / ssl_certificate_key — настройте сами

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }
}
```

---

## Поддерживаемые эндпоинты бэкенда

| Метод | Путь | Описание |
|---|---|---|
| `POST` | `/api/v1/entries` · `/api/v1/entries.json` | Приём глюкозы от xDrip+ |
| `POST` | `/api/v1/devicestatus` · `/api/v1/devicestatus.json` | Приём IoB/CoB от AAPS |
| `GET` | `/api/v1/current` | Текущие данные для виджета |
| `GET` | `/api/v1/status` · `/api/v1/status.json` | Nightscout status probe |
| `GET` | `/api/v1/treatments` · `/api/v1/treatments.json` | Заглушка (пустой массив) |
| `GET` | `/health` | Liveness probe |

Ответ `/api/v1/current`:
```json
{
  "mmol": 6.8,
  "mgdl": 122.4,
  "direction": "Flat",
  "delta": "+0.2",
  "iob": 0.0,
  "cob": 0.0,
  "battery": 24,
  "minutes_ago": 2,
  "timestamp": 1785801600
}
```

---

## ЧАСТЬ 2 — Настройка xDrip+ на Android

1. Откройте **Настройки** (гамбургер-меню) → **Inter-app settings** (Межпрограммное взаимодействие).
2. Включите **REST API Upload** (переключатель).
3. В поле **Base URL** введите:
   ```
   http://<IP_ВАШЕГО_VPS>:8080
   ```
   или через HTTPS/Nginx:
   ```
   https://your.domain.com
   ```
4. В поле **API Secret** введите **тот же секрет**, что записан в `API_SECRET` на сервере (plain text — xDrip+ сам вычислит SHA-1).
5. Убедитесь, что опция **Upload entries** активна.
6. Нажмите **Test** — должно появиться уведомление об успешной отправке.

> **Заряд батареи телефона** автоматически подхватывается из поля `uploader.battery` devicestatus и отображается в виджете.

### Настройка AAPS (опционально, для IoB)

В AAPS: **Конфигуратор** → **NSClient** → Base URL = `http://<IP>:8080`, API Secret = тот же.  
AAPS будет отправлять `devicestatus` с реальными значениями IoB/CoB.

---

## ЧАСТЬ 3 — Десктопный виджет (Windows / macOS / Linux)

### Вариант A: готовый EXE (Windows)

Скачайте `xDripWidget.exe` из раздела [Releases](https://github.com/EvgeniyKrasnyanskiy/xDripWidget/releases)  
или соберите сами (см. ниже). Запускается без установки Python.

### Вариант B: запуск из исходников

```bash
pip install -r widget_requirements.txt
python widget.py
```

### Первый запуск — настройки

Правая кнопка мыши → **Настройки…**  
Введите URL сервера, API Secret и уровень прозрачности.

### Управление

| Действие | Результат |
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
| `API_SECRET` | `changeme` | Секрет авторизации (plain text) |
| `DB_PATH` | `./data/glycemia.db` | Путь к SQLite базе |
| `PRUNE_HOURS` | `48` | Сколько часов хранить историю |
| `LOG_LEVEL` | `INFO` | Уровень логирования (`DEBUG` для диагностики) |
