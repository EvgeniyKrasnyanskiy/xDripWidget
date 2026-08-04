# xDripWidget — Glycemia Micro-Backend + Desktop Widget

Ультра-лёгкий Nightscout-совместимый бэкенд (< 20 MB RAM) + десктопный виджет глюкозы.

---

## Структура проекта

```
xDripWidget/
├── main.py                     # FastAPI-бэкенд
├── widget.py                   # PyQt6 десктопный виджет
├── requirements.txt            # зависимости сервера
├── widget_requirements.txt     # зависимости виджета
├── glycemia_backend.service    # systemd unit-файл
└── data/                       # создаётся автоматически, здесь glycemia.db
```

---

## ЧАСТЬ 1 — Деплой бэкенда на VPS (Ubuntu)

### 1. Загрузить файлы

```bash
sudo mkdir -p /opt/glycemia-backend/data
sudo chown -R www-data:www-data /opt/glycemia-backend
cd /opt/glycemia-backend
# скопируйте main.py, requirements.txt сюда (scp / git clone)
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
# Если используете venv, раскомментируйте нужную строку ExecStart в .service
sudo systemctl daemon-reload
sudo systemctl enable --now glycemia_backend
sudo systemctl status glycemia_backend
```

### 5. Проверить

```bash
curl http://localhost:8080/health
# Ожидаемый ответ: {"status":"ok","ts":...}
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

### Настройка AAPS (опционально, для IoB)

В AAPS: **Конфигуратор** → **NSClient** → Base URL = `http://<IP>:8080`, API Secret = тот же.  
AAPS будет отправлять `devicestatus` с IoB автоматически.

---

## ЧАСТЬ 3 — Запуск десктопного виджета (Windows / macOS / Linux)

### 1. Установить Python ≥ 3.11 и зависимости

```bash
pip install -r widget_requirements.txt
```

### 2. Запустить

```bash
python widget.py
```

### 3. Первый запуск — настройки

Правая кнопка мыши → **Настройки…**  
Введите URL сервера и API Secret (необязательно для GET /current).

### 4. Управление

| Действие | Результат |
|---|---|
| Перетащить (ЛКМ) | Переместить виджет |
| ПКМ → Свернуть в трей | Скрыть виджет |
| ПКМ → Обновить сейчас | Немедленный запрос |
| ПКМ → Настройки… | Изменить URL / secret |
| ПКМ → Выход | Завершить виджет |
| Двойной клик на трее | Показать / скрыть |

### Цветовая индикация

| Цвет | Значение |
|---|---|
| 🟢 Зелёный | Норма 4.0 – 9.0 ммоль/л |
| 🟡 Жёлтый | Лёгкая гипо/гипер 3.3–3.9 / 9.1–11.0 |
| 🔴 Красный | Выраженная гипо < 3.3 или гипер > 11.0 |
| ⚫ Серый | Данные устарели (> 15 минут) |

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
| `LOG_LEVEL` | `INFO` | Уровень логирования |
