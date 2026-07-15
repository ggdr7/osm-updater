OSM UPDATE UTILITY

АВТОМАТИЧЕСКАЯ УСТАНОВКА
==================================

Вы можете запустить автоматическую настройку OSM сервера, лучше всего это делать на чистом сервере под Ubuntu 26.04.

ШАГ 1: ПОДГОТОВКА СЕРВЕРА
--------------------------
Подключитесь к серверу по SSH и выполните одну команду:

    wget -qO- https://raw.githubusercontent.com/ggdr7/osm-updater/main/install-server.sh | sudo bash

Скрипт установит все необходимые компоненты (PostgreSQL, osm2pgsql, renderd, Apache) и создаст базу данных.

Скрипт безопасен для повторного запуска — он не удалит существующие данные.

После выполнения скрипт выведет данные для подключения

ШАГ 2: УСТАНОВКА УТИЛИТЫ
-------------------------
Скачайте релиз:

    wget https://github.com/ggdr7/osm-updater/releases/latest/download/osm-updater-linux-x64.tar.gz

Распакуйте:

    tar -xzf osm-updater-linux-x64.tar.gz -C /opt/osm-update/

Настройте права:

    chmod +x /opt/osm-update/OsmUpdateUtility

Создайте сервис systemd:

    sudo nano /etc/systemd/system/osm-update.service

Вставьте следующее содержимое (замените ВАШ_ПОЛЬЗОВАТЕЛЬ на имя вашего пользователя):

    [Unit]
    Description=OSM Update Utility
    After=network.target

    [Service]
    Type=simple
    User=ВАШ_ПОЛЬЗОВАТЕЛЬ
    WorkingDirectory=/opt/osm-update
    ExecStart=/opt/osm-update/OsmUpdateUtility
    Restart=always
    RestartSec=10

    [Install]
    WantedBy=multi-user.target

Сохраните файл и выполните:

    sudo systemctl daemon-reload
    sudo systemctl enable osm-update
    sudo systemctl start osm-update


ШАГ 3: ПЕРВОНАЧАЛЬНАЯ НАСТРОЙКА
--------------------------------
Откройте браузер и перейдите по адресу:

    http://ВАШ_IP:5000/Setup

Форма будет предзаполнена данными из скрипта, при необходимости их можно изменить.

Создайте администратора на странице входа.


РУЧНАЯ УСТАНОВКА
================


ШАГ 1: УСТАНОВКА СИСТЕМНЫХ ЗАВИСИМОСТЕЙ
----------------------------------------
    sudo apt-get update
    sudo apt-get install -y postgresql postgresql-contrib postgis osm2pgsql gdal-bin libapache2-mod-tile renderd apache2 mapnik-utils libmapnik-dev fonts-noto-cjk fonts-noto-hinted fonts-unifont git curl wget unzip python3-psycopg2 python3-yaml npm node-carto lua5.1


ШАГ 2: НАСТРОЙКА POSTGRESQL
----------------------------
Создаем пользователя для рендеринга:

    sudo -u postgres createuser _renderd

Создаем пользователя для веб-утилиты:

    sudo -u postgres createuser osm_app
    sudo -u postgres psql -c "ALTER USER osm_app WITH PASSWORD 'ВАШ_ПАРОЛЬ';"

Создаем базу данных:

    sudo -u postgres createdb -E UTF8 -O _renderd gis

Устанавливаем расширения:

    sudo -u postgres psql -d gis -c "CREATE EXTENSION IF NOT EXISTS postgis;"
    sudo -u postgres psql -d gis -c "CREATE EXTENSION IF NOT EXISTS hstore;"

Назначаем права:

    sudo -u postgres psql -d gis -c "ALTER TABLE geometry_columns OWNER TO _renderd;"
    sudo -u postgres psql -d gis -c "ALTER TABLE spatial_ref_sys OWNER TO _renderd;"
    sudo -u postgres psql -d gis -c "ALTER TABLE geography_columns OWNER TO _renderd;"
    sudo -u postgres psql -d gis -c "GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO osm_app;"
    sudo -u postgres psql -d gis -c "GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO osm_app;"


ШАГ 3: ЗАГРУЗКА СТИЛЕЙ
-----------------------
    sudo mkdir -p /opt/osm-update
    sudo chown $USER:$USER /opt/osm-update

    git clone https://github.com/gravitystorm/openstreetmap-carto.git /opt/osm-update/openstreetmap-carto
    cd /opt/osm-update/openstreetmap-carto
    git switch --detach v5.9.0

Компиляция стилей:

    carto project.mml > style.xml 2>/dev/null || true


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

Активируйте сайт:

    sudo a2ensite osm-tiles
    sudo systemctl restart apache2 renderd


ШАГ 6: ИМПОРТ ДАННЫХ OSM =
---------------------------------------
    mkdir -p /opt/osm-update/data
    wget -O /opt/osm-update/data/region.osm.pbf "https://download.geofabrik.de/russia/far-eastern-fed-district-latest.osm.pbf"

    sudo -u _renderd osm2pgsql -d gis --create --slim -G --hstore --tag-transform-script /opt/osm-update/openstreetmap-carto/openstreetmap-carto.lua -C 2500 --number-processes $(nproc) -S /opt/osm-update/openstreetmap-carto/openstreetmap-carto.style /opt/osm-update/data/region.osm.pbf

    sudo -u _renderd psql -d gis -f /opt/osm-update/openstreetmap-carto/indexes.sql
    sudo -u _renderd psql -d gis -f /opt/osm-update/openstreetmap-carto/functions.sql
    cd /opt/osm-update/openstreetmap-carto && sudo -u _renderd python3 scripts/get-external-data.py


ШАГ 7: УСТАНОВКА УТИЛИТЫ
-------------------------
Скачайте и распакуйте релиз:

    wget https://github.com/ggdr7/osm-updater/releases/latest/download/osm-updater-linux-x64.tar.gz
    tar -xzf osm-updater-linux-x64.tar.gz -C /opt/osm-update/
    chmod +x /opt/osm-update/OsmUpdateUtility

Создайте сервис systemd:

    sudo nano /etc/systemd/system/osm-update.service

Вставьте следующее содержимое (замените ВАШ_ПОЛЬЗОВАТЕЛЬ на имя вашего пользователя):

    [Unit]
    Description=OSM Update Utility
    After=network.target

    [Service]
    Type=simple
    User=ВАШ_ПОЛЬЗОВАТЕЛЬ
    WorkingDirectory=/opt/osm-update
    ExecStart=/opt/osm-update/OsmUpdateUtility
    Restart=always
    RestartSec=10

    [Install]
    WantedBy=multi-user.target

Сохраните файл и выполните:

    sudo systemctl daemon-reload
    sudo systemctl enable osm-update
    sudo systemctl start osm-update

ШАГ 8: ПЕРВОНАЧАЛЬНАЯ НАСТРОЙКА
--------------------------------
Откройте браузер и перейдите по адресу:

    http://ВАШ_IP:5000/Setup

Форма будет предзаполнена данными, при необходимости их можно изменить.

Создайте администратора на странице входа.


ИСПОЛЬЗОВАНИЕ
=============
После установки откройте http://ВАШ_IP:5000/ и войдите под созданным пользователем.

ОСНОВНЫЕ ФУНКЦИИ:
  - Главная страница: Статус системы, журнал обновлений, управление регионами
  - Настройки: Режим обновления, расписание, пути, смена пароля
  - Hangfire Dashboard: http://ВАШ_IP:5000/hangfire — мониторинг фоновых задач

ДОБАВЛЕНИЕ РЕГИОНА:
  1. Откройте главную страницу
  2. В разделе "Добавить регион" заполните:
     - Название (например, "Дальневосточный ФО")
     - Код (например, far-eastern-fed-district)
     - URL файла .osm.pbf (например, https://download.geofabrik.de/russia/far-eastern-fed-district-latest.osm.pbf)
  3. Нажмите "Добавить"

Утилита автоматически скачает файл, импортирует его в базу данных и настроит рендеринг тайлов.


ПОЛЕЗНЫЕ КОМАНДЫ
================
  - Просмотр логов утилиты:
      sudo journalctl -u osm-update -f

  - Просмотр логов renderd:
      sudo journalctl -u renderd -f

  - Перезапуск утилиты:
      sudo systemctl restart osm-update

  - Перезапуск renderd:
      sudo systemctl restart renderd

  - Статус сервисов:
      sudo systemctl status osm-update renderd apache2

