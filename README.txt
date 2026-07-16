OSM UPDATE UTILITY


АВТОМАТИЧЕСКАЯ УСТАНОВКА (Рекомендуется)
================================================================================
Скрипт выполняет полную настройку сервера: устанавливает зависимости,
настраивает БД, скачивает и импортирует данные OSM (Дальневосточный ФО), создаёт 
таблицы утилиты, настраивает веб-сервер и готовит файл systemd-сервиса.


ШАГ 1: ЗАПУСК СКРИПТА УСТАНОВКИ
--------------------------------
Подключитесь к чистому серверу (тестирование проводилось на Ubuntu 26.04) и выполните:

    wget -qO- https://raw.githubusercontent.com/ggdr7/osm-updater/main/install-server.sh | sudo bash

После завершения скрипт выведет данные для подключения веб-утилиты.

ШАГ 2: УСТАНОВКА ВЕБ-УТИЛИТЫ
-----------------------------
Скрипт уже создал и настроил файл сервиса osm-update.service. Вам нужно 
только скачать и запустить саму утилиту:

    wget https://github.com/ggdr7/osm-updater/releases/latest/download/osm-updater-linux-x64.tar.gz
    sudo tar -xzf osm-updater-linux-x64.tar.gz -C /opt/osm-update/
    sudo chmod +x /opt/osm-update/OsmUpdateUtility
    sudo systemctl start osm-update

ШАГ 3: ПЕРВОНАЧАЛЬНАЯ НАСТРОЙКА
--------------------------------
1. Откройте браузер и перейдите по адресу: http://ВАШ_IP:5000/Setup
2. Форма будет предзаполнена данными из скрипта. Нажмите "Сохранить".
3. Создайте первого пользователя (администратора) на странице входа.


РУЧНАЯ УСТАНОВКА
================================================================================
Если вы предпочитаете контролировать каждый шаг или используете нестандартную
конфигурацию.

ШАГ 1: УСТАНОВКА СИСТЕМНЫХ ЗАВИСИМОСТЕЙ
----------------------------------------
    sudo apt-get update
    sudo apt-get install -y postgresql postgresql-contrib postgis osm2pgsql gdal-bin \
        libapache2-mod-tile renderd apache2 mapnik-utils libmapnik-dev \
        fonts-noto-cjk fonts-noto-hinted fonts-unifont git curl wget unzip \
        python3-psycopg2 python3-yaml npm node-carto lua5.1

ШАГ 2: НАСТРОЙКА POSTGRESQL
----------------------------
Создаем пользователей:

    sudo -u postgres createuser _renderd
    sudo -u postgres createuser osm_app
    sudo -u postgres psql -c "ALTER USER osm_app WITH PASSWORD 'SecureOsmAppPass2026!';"

Создаем базу данных (владелец _renderd для osm2pgsql):

    sudo -u postgres createdb -E UTF8 -O _renderd gis
    sudo -u postgres psql -d gis -c "CREATE EXTENSION IF NOT EXISTS postgis;"
    sudo -u postgres psql -d gis -c "CREATE EXTENSION IF NOT EXISTS hstore;"
    sudo -u postgres psql -d gis -c "ALTER TABLE geometry_columns OWNER TO _renderd;"
    sudo -u postgres psql -d gis -c "ALTER TABLE spatial_ref_sys OWNER TO _renderd;"
    sudo -u postgres psql -d gis -c "ALTER TABLE geography_columns OWNER TO _renderd;"

КРИТИЧНО для PostgreSQL 15+: Права для osm_app:

    sudo -u postgres psql -d gis -c "GRANT CREATE ON SCHEMA public TO osm_app;"
    sudo -u postgres psql -d gis -c "GRANT USAGE ON SCHEMA public TO osm_app;"
    sudo -u postgres psql -d gis -c "GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO osm_app;"
    sudo -u postgres psql -d gis -c "GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO osm_app;"

ШАГ 3: ЗАГРУЗКА И КОМПИЛЯЦИЯ СТИЛЕЙ
------------------------------------
    sudo mkdir -p /opt/osm-update
    sudo chown $USER:$USER /opt/osm-update

    git clone https://github.com/gravitystorm/openstreetmap-carto.git /opt/osm-update/openstreetmap-carto
    cd /opt/osm-update/openstreetmap-carto
    git switch --detach v5.9.0
    carto project.mml > style.xml 2>/dev/null || true

Права на запись для _renderd:

    sudo chown -R _renderd:_renderd /opt/osm-update/openstreetmap-carto/
    sudo chmod -R 775 /opt/osm-update/openstreetmap-carto/

ШАГ 4: НАСТРОЙКА RENDERD
-------------------------
    sudo mkdir -p /var/lib/mod_tile
    sudo chown _renderd:_renderd /var/lib/mod_tile
    sudo nano /etc/renderd.conf

Вставьте:

    [renderd]
    pid_file=/run/renderd/renderd.pid
    socketname=/run/renderd/renderd.sock
    num_threads=4
    tile_dir=/var/lib/mod_tile

    [mapnik]
    plugins_dir=/usr/lib/x86_64-linux-gnu/mapnik/4.2/input
    font_dir=/usr/share/fonts/truetype
    font_dir_recurse=true

    [default]
    URI=/osm_tiles/
    TILEDIR=/var/lib/mod_tile
    XML=/opt/osm-update/openstreetmap-carto/style.xml
    HOST=localhost
    TILESIZE=256
    MAXZOOM=20

ШАГ 5: НАСТРОЙКА APACHE
------------------------
    sudo a2enmod tile headers
    sudo a2dissite 000-default.conf
    sudo nano /etc/apache2/sites-available/osm-tiles.conf

Вставьте:

    <VirtualHost *:80>
        ServerName localhost
        ModTileTileDir /var/lib/mod_tile
        AddTileConfig /osm_tiles/ default
        Header always set Access-Control-Allow-Origin "*"
        DocumentRoot /var/www/html
        <Directory /var/www/html>
            Require all granted
        </Directory>
    </VirtualHost>

Активация и создание тестовой страницы:

    sudo a2ensite osm-tiles.conf
    sudo systemctl restart apache2 renderd

    sudo tee /var/www/html/index.html > /dev/null << 'EOF'
    <!DOCTYPE html><html><head><meta charset="UTF-8"><title>OSM Tile Server</title>
    <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css"/>
    <style>body{margin:0;padding:0} #map{width:100%;height:100vh}</style></head>
    <body><div id="map"></div><script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
    <script>var map=L.map('map').setView([50,130],5);
    L.tileLayer('/osm_tiles/{z}/{x}/{y}.png',{attribution:'&copy; OSM',maxZoom:20}).addTo(map);</script></body></html>
    EOF

ШАГ 6: ИМПОРТ ДАННЫХ OSM
-------------------------
1. Настройка passwordless sudo для утилиты (замените ВАШ_ПОЛЬЗОВАТЕЛЬ):

    echo 'ВАШ_ПОЛЬЗОВАТЕЛЬ ALL=(_renderd) NOPASSWD: /usr/bin/osm2pgsql' | sudo tee /etc/sudoers.d/osm-update
    sudo chmod 440 /etc/sudoers.d/osm-update

2. Скачивание данных:

    mkdir -p /opt/osm-update/data
    wget -O /opt/osm-update/data/region.osm.pbf "https://download.geofabrik.de/russia/far-eastern-fed-district-latest.osm.pbf"

3. Импорт (Используется .style файл, НЕ используйте --tag-transform-script!):

    sudo -u _renderd osm2pgsql \
      -d gis --create --slim -G --hstore \
      -S /opt/osm-update/openstreetmap-carto/openstreetmap-carto.style \
      -C 2500 --number-processes $(nproc) \
      /opt/osm-update/data/region.osm.pbf

4. Индексы, функции и внешние данные:

    sudo -u _renderd psql -d gis -f /opt/osm-update/openstreetmap-carto/indexes.sql
    sudo -u _renderd psql -d gis -f /opt/osm-update/openstreetmap-carto/functions.sql
    cd /opt/osm-update/openstreetmap-carto && sudo -u _renderd python3 scripts/get-external-data.py

ШАГ 7: УСТАНОВКА УТИЛИТЫ
-------------------------
    wget https://github.com/ggdr7/osm-updater/releases/latest/download/osm-updater-linux-x64.tar.gz
    sudo tar -xzf osm-updater-linux-x64.tar.gz -C /opt/osm-update/
    sudo chmod +x /opt/osm-update/OsmUpdateUtility

Создание сервиса (замените ВАШ_ПОЛЬЗОВАТЕЛЬ):

    sudo nano /etc/systemd/system/osm-update.service

Вставьте:

    [Unit]
    Description=OSM Update Utility
    After=network.target

    [Service]
    Type=simple
    User=ВАШ_ПОЛЬЗОВАТЕЛЬ
    WorkingDirectory=/opt/osm-update
    ExecStart=/opt/osm-update/OsmUpdateUtility
    Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
    Restart=always
    RestartSec=10

    [Install]
    WantedBy=multi-user.target

Запуск:

    sudo systemctl daemon-reload
    sudo systemctl enable osm-update
    sudo systemctl start osm-update


ИСПОЛЬЗОВАНИЕ
================================================================================
После установки откройте http://ВАШ_IP:5000/ и войдите под созданным пользователем.

ОСНОВНЫЕ ФУНКЦИИ:
  - Главная страница: Статус системы, журнал обновлений, управление регионами.
  - Настройки: Режим обновления, расписание, пути, смена пароля.
  - Hangfire Dashboard: http://ВАШ_IP:5000/hangfire - мониторинг фоновых задач.


РЕШЕНИЕ ВОЗМОЖНЫХ ПРОБЛЕМ 
================================================================================

Ошибка: permission denied for schema public
Возникает в PostgreSQL 15+ при создании таблиц утилитой.
Решение: 
    sudo -u postgres psql -d gis -c "GRANT CREATE ON SCHEMA public TO osm_app;"

Ошибка: sudo: A terminal is required to authenticate
Утилита не может запустить osm2pgsql через sudo без пароля.
Решение: 
    echo 'ВАШ_ПОЛЬЗОВАТЕЛЬ ALL=(_renderd) NOPASSWD: /usr/bin/osm2pgsql' | sudo tee /etc/sudoers.d/osm-update
    sudo chmod 440 /etc/sudoers.d/osm-update

Ошибка: File does not exist: openstreetmap-carto-flex.lua
Ваша версия openstreetmap-carto использует формат .style.
Решение: Убедитесь, что в утилите используется путь:
    /opt/osm-update/openstreetmap-carto/openstreetmap-carto.style
НЕ используйте флаг --tag-transform-script вместе с .style файлом.

Карта не отображается (404 на тайлы)
1. Проверьте наличие данных: 
   PGPASSWORD='SecureOsmAppPass2026!' psql -h localhost -U osm_app -d gis -c "SELECT COUNT(*) FROM planet_osm_point;"
2. Если счетчик равен 0 — выполните импорт вручную (см. Ручная установка, Шаг 6).
3. Перезапустите renderd: sudo systemctl restart renderd
4. Первый запрос к тайлу может вернуть 404 — подождите 10-15 секунд и обновите страницу.

PermissionError при get-external-data.py
У пользователя _renderd нет прав на запись в папку стилей.
Решение: 
    sudo chown -R _renderd:_renderd /opt/osm-update/openstreetmap-carto/
    sudo chmod -R 775 /opt/osm-update/openstreetmap-carto/

Ошибка: column "UpdatedAt" does not exist
Проблема рассинхронизации регистров PostgreSQL и EF Core в старых версиях.
Решение: Обновите утилиту до версии v1.0.2+. Для существующих баз выполните:
    sudo -u postgres psql -d gis -c "ALTER TABLE \"MapRegions\" RENAME COLUMN \"updatedat\" TO \"UpdatedAt\";"