# Дорожная карта развития xDrip Widget (Windows Desktop)

В данном документе собраны актуальные запланированные улучшения и функциональные расширения для десктопной версии виджета. Выполненные этапы перенесены в [.agent/docs/ARCHIVE.md](file:///h:/Diabetes/xDripWidget/.agent/docs/ARCHIVE.md).

---

## 1. Информативность и мониторинг данных

- [ ] **Отображение активного инсулина (IoB) и углеводов (CoB)**:
  - Серверная поддержка эндпоинта `/api/v1/current` уже верифицирована.
  - При наличии ненулевых значений (`iob > 0` или `cob > 0`) вывод аккуратного компактного блока: `IoB: 1.2 ед | CoB: 24 г`.
  - При нулевых значениях блок автоматически скрывается, сохраняя компактность интерфейса.
- [ ] **Интерактивный просмотр точек графика (Graph Tooltips)**:
  - Отображение всплывающей подсказки при наведении курсора на конкретную точку 4-часового графика: точное время замера, значение сахара и дельта.
- [ ] **Расчёт времени в целевом диапазоне (% TIR) за 4 часа**:
  - Анализ массива точек в истории и вывод компактного бейджа (например, `TIR: 88%`) в углу графика.

---

## 2. Инфраструктура и серверная миграция

- [ ] **Миграция бэкенда на существующий сервер в РФ с сайтом (HTTPS + поддомен)**:
  - **Контекст**: На целевом сервере уже работает веб-сайт и задействованы порты 80/443. Миграция строится по принципу **100% изоляции**, чтобы гарантированно не затронуть работу существующего сайта и его сервисов.
  - **Цель**: Стабильная выгрузка сахара через любого мобильного оператора РФ без VPN, по защищенному протоколу HTTPS.

  ### Пошаговый алгоритм миграции (на работающий сервер):

  1. **Создание поддомена в DNS**:
     - Чтобы не пересекаться с основным сайтом, выделить отдельный поддомен (например, `cgm.mydomain.ru` или `glycemia.mydomain.ru`).
     - В DNS-панели домена добавить **A-запись** поддомена, указывающую на IP-адрес этого сервера.

  2. **Выбор свободного внутреннего порта**:
     - Проверить, какие порты уже заняты существующими приложениями:
       ```bash
       sudo ss -tulpn | grep LISTEN
       ```
     - Выбрать свободный внутренний порт (например, `8085` или `8090`). Бэкенд будет слушать **только локальный интерфейс `127.0.0.1`** и не будет торчать наружу в интернет напрямую.

  3. **Изолированное развёртывание бэкенда**:
     - Убедиться в наличии Python 3 и venv:
       ```bash
       sudo apt update && sudo apt install -y python3-venv python3-pip git sqlite3
       ```
     - Создать изолированную папку (пакеты не смешаются с основным сайтом):
       ```bash
       sudo mkdir -p /opt/glycemia-backend/data
       sudo chown -R $USER:$USER /opt/glycemia-backend
       git clone https://github.com/EvgeniyKrasnyanskiy/xDripWidget.git /opt/glycemia-backend
       ```
     - Создать виртуальное окружение:
       ```bash
       cd /opt/glycemia-backend
       python3 -m venv .venv
       .venv/bin/pip install -r requirements.txt
       ```
     - **Перенос существующей базы данных** со старого VPS (сохранение всей истории сахаров и терапий):
       ```bash
       scp /opt/glycemia-backend/data/glycemia.db user@НОВЫЙ_СЕРВЕР:/opt/glycemia-backend/data/glycemia.db
       ```
     - Настроить systemd сервис с выбранным свободным портом (например, 8085) и секретом (`/etc/systemd/system/glycemia_backend.service.d/override.conf`):
       ```ini
       [Service]
       Environment="API_SECRET=MyGlucoseSecret2026"
       # При необходимости сменить порт (по умолчанию в unit-файле 8080):
       ExecStart=
       ExecStart=/opt/glycemia-backend/.venv/bin/uvicorn main:app --host 127.0.0.1 --port 8085
       ```
     - Запустить и включить службу:
       ```bash
       sudo systemctl daemon-reload
       sudo systemctl enable --now glycemia_backend
       ```
     - Проверить локальный ответ бэкенда:
       ```bash
       curl http://127.0.0.1:8085/health
       # Ответ: {"status":"ok","ts":...}
       ```

  4. **Настройка проксирования и SSL (два варианта в зависимости от сервера)**:

     * **Вариант А: Если на сервере установлен чистый Nginx**:
       - Конфиги основного сайта **не трогаются**. Создаётся отдельный файл виртуального хоста `/etc/nginx/sites-available/cgm.mydomain.ru`:
         ```nginx
         server {
             listen 80;
             server_name cgm.mydomain.ru;

             location / {
                 proxy_pass http://127.0.0.1:8085;
                 proxy_set_header Host $host;
                 proxy_set_header X-Real-IP $remote_addr;
                 proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
                 proxy_set_header X-Forwarded-Proto $scheme;
             }
         }
         ```
       - Активировать поддомен и проверить конфигурацию:
         ```bash
         sudo ln -s /etc/nginx/sites-available/cgm.mydomain.ru /etc/nginx/sites-enabled/
         sudo nginx -t && sudo systemctl reload nginx
         ```
       - Выпустить сертификат Certbot строго для нового поддомена (сертификаты существующего сайта не затрагиваются):
         ```bash
         sudo certbot --nginx -d cgm.mydomain.ru
         ```

     * **Вариант Б: Если сервер управляется панелью (FastPanel, ISPmanager, aaPanel, CyberPanel)**:
       1. В веб-панели нажать **«Добавить сайт / поддомен»** → указать `cgm.mydomain.ru`.
       2. В настройках поддомена включить **Reverse Proxy (Проксирование)** на адрес `http://127.0.0.1:8085`.
       3. Во вкладке **SSL** нажать **«Выпустить Let's Encrypt»** с галочкой принудительного HTTPS.

     - Проверить доступность по HTTPS из внешнего интернета:
       ```bash
       curl https://cgm.mydomain.ru/health
       # Ответ: {"status":"ok","ts":...}
       ```

  ### Инструкция по обновлению настроек клиентов:

  * **xDrip+ (Android-телефон со считывателем сенсора)**:
    - Открыть: **Настройки** → **Загрузка в облако (Cloud Upload)** → **Синхронизация Nightscout (REST-API)**:
      - **Включено (Enabled)**: Вкл (галочка).
      - **Базовый URL (Base URL)**: заменить старый IP на новый HTTPS-адрес:
        ```text
        https://glycemia.mydomain.ru/api/v1/
        ```
        *(если в вашей сборке xDrip+ секрет передается в строке адреса: `https://MyGlucoseSecret2026@glycemia.mydomain.ru/api/v1/`)*
      - **API Secret**: `MyGlucoseSecret2026`.
      - Нажать **Тест (Test)** — должно прийти уведомление `200 OK`.
      - *Результат:* выгрузка сахара с сенсора стабильно работает в фоновом режиме через любого мобильного оператора РФ без необходимости включать VPN.

  * **TIRUp (Android-приложение)**:
    - Открыть раздел настроек синхронизации с Nightscout / сервером:
      - **Nightscout URL**: `https://glycemia.mydomain.ru` *(или с суффиксом `/api/v1/` согласно формату поля в приложении)*.
      - **API Secret / Token**: `MyGlucoseSecret2026`.
      - Проверить соединение — приложение будет получать данные напрямую без задержек и без VPN.

  * **xDripWidget (Desktop C# Windows)**:
    - Нажать ПКМ по виджету или иконке в трее → **«Настройки…»**:
      - **Адрес сервера**: `https://glycemia.mydomain.ru`
      - **API Secret**: `MyGlucoseSecret2026`
      - Нажать **«Сохранить»**.

  * **xDripWidget (Android-виджет)**:
    - В настройках виджета обновить адрес сервера на `https://glycemia.mydomain.ru`.
