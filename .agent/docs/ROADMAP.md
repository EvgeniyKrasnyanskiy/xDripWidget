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

- [ ] **Миграция бэкенда на сервер в РФ с поддержкой HTTPS и доменного имени**:
  - **Цель**: Полное устранение сбоев выгрузки данных через мобильных операторов РФ без необходимости постоянного использования VPN на смартфоне.

  ### Пошаговый алгоритм миграции:
  1. **Аренда VPS в РФ**:
     - Рекомендуемые провайдеры: Timeweb Cloud, Selectel, Beget, RuVDS, Yandex Cloud.
     - Конфигурация: 1 vCPU, 512 МБ – 1 ГБ RAM, ОС Ubuntu 22.04 / 24.04 LTS (микро-бэкенд потребляет < 20 МБ RAM).
  2. **Привязка домена (DNS)**:
     - Выбрать домен или поддомен (например, `glycemia.mydomain.ru`).
     - Создать **A-запись** в панели DNS, указав IPv4-адрес нового российского сервера.
  3. **Развёртывание бэкенда**:
     - Установить системные пакеты:
       ```bash
       sudo apt update && sudo apt install -y python3-venv python3-pip git sqlite3
       ```
     - Клонировать проект:
       ```bash
       sudo mkdir -p /opt/glycemia-backend/data
       sudo chown -R $USER:$USER /opt/glycemia-backend
       git clone https://github.com/EvgeniyKrasnyanskiy/xDripWidget.git /opt/glycemia-backend
       ```
     - Создать virtualenv и установить зависимости:
       ```bash
       cd /opt/glycemia-backend
       python3 -m venv .venv
       .venv/bin/pip install -r requirements.txt
       ```
     - **Перенос существующей базы данных** со старого сервера (сохранение всей истории сахаров и терапий):
       ```bash
       # Выполнить со старого сервера или скопировать через scp:
       scp /opt/glycemia-backend/data/glycemia.db user@НОВЫЙ_IP:/opt/glycemia-backend/data/glycemia.db
       ```
     - Настроить секрет в systemd override (`/etc/systemd/system/glycemia_backend.service.d/override.conf`):
       ```ini
       [Service]
       Environment="API_SECRET=MyGlucoseSecret2026"
       ```
     - Установить и активировать сервис:
       ```bash
       sudo cp /opt/glycemia-backend/glycemia_backend.service /etc/systemd/system/
       sudo systemctl daemon-reload
       sudo systemctl enable --now glycemia_backend
       ```
  4. **Настройка Reverse Proxy (Nginx) и бесплатного SSL (Let's Encrypt)**:
     - Установить Nginx и Certbot:
       ```bash
       sudo apt install -y nginx certbot python3-certbot-nginx
       ```
     - Создать конфиг виртуального хоста `/etc/nginx/sites-available/glycemia`:
       ```nginx
       server {
           listen 80;
           server_name glycemia.mydomain.ru;

           location / {
               proxy_pass http://127.0.0.1:8080;
               proxy_set_header Host $host;
               proxy_set_header X-Real-IP $remote_addr;
               proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
               proxy_set_header X-Forwarded-Proto $scheme;
           }
       }
       ```
     - Активировать сайт и перезагрузить Nginx:
       ```bash
       sudo ln -s /etc/nginx/sites-available/glycemia /etc/nginx/sites-enabled/
       sudo nginx -t && sudo systemctl reload nginx
       ```
     - Выпустить бесплатный SSL-сертификат:
       ```bash
       sudo certbot --nginx -d glycemia.mydomain.ru
       ```
       *(Certbot автоматически настроит HTTPS на порту 443 и автоматическое перенаправление с HTTP на HTTPS).*
     - Проверить работоспособность:
       ```bash
       curl https://glycemia.mydomain.ru/health
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
