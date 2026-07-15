#!/bin/bash
# =============================================================================
# OpenStreetMap Tile Server Installer
# Ubuntu Server 24.04/26.04 LTS 
# =============================================================================
set -e

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'
log_info() { echo -e "${GREEN}[INFO]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; exit 1; }

if [ "$EUID" -ne 0 ]; then log_error "Запустите с sudo: sudo bash $0"; fi

APP_USER="${SUDO_USER:-$USER}"
OPT_DIR="/opt/osm-update"
CARTO_DIR="${OPT_DIR}/openstreetmap-carto"
DATA_DIR="${OPT_DIR}/data"
DB_NAME="gis"
DB_RENDER="_renderd"
DB_APP="osm_app"
DB_APP_PASS="SecureOsmAppPass2026!"
TILE_DIR="/var/lib/mod_tile"
CARTO_VERSION="v5.9.0"

ARCH=$(dpkg --print-architecture)
if [ "$ARCH" = "arm64" ]; then MAPNIK_DIR="/usr/lib/aarch64-linux-gnu/mapnik/4.2/input"
elif [ "$ARCH" = "amd64" ]; then MAPNIK_DIR="/usr/lib/x86_64-linux-gnu/mapnik/4.2/input"
else log_error "Неподдерживаемая архитектура: $ARCH"; fi

SERVER_IP=$(hostname -I | awk '{print $1}')

log_info "==============================================================================="
log_info "Установка OSM тайл-сервера"
log_info "Пользователь: ${APP_USER}"
log_info "IP-адрес: ${SERVER_IP}"
log_info "Архитектура: ${ARCH}"
log_info "Версия стилей: ${CARTO_VERSION}"
log_info "==============================================================================="

log_info "Установка системных зависимостей..."
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq postgresql postgresql-contrib postgis osm2pgsql gdal-bin \
    libapache2-mod-tile renderd apache2 mapnik-utils libmapnik-dev \
    fonts-noto-cjk fonts-noto-hinted fonts-unifont git curl wget unzip \
    python3-psycopg2 python3-yaml npm node-carto lua5.1

log_info "Настройка PostgreSQL..."
sudo -u postgres psql -tc "SELECT 1 FROM pg_roles WHERE rolname = '${DB_RENDER}'" | grep -q 1 || sudo -u postgres createuser "${DB_RENDER}"
sudo -u postgres psql -tc "SELECT 1 FROM pg_roles WHERE rolname = '${DB_APP}'" | grep -q 1 || sudo -u postgres createuser "${DB_APP}"
sudo -u postgres psql -c "ALTER USER ${DB_APP} WITH PASSWORD '${DB_APP_PASS}';"

if ! sudo -u postgres psql -lqt | cut -d \| -f 1 | grep -qw "${DB_NAME}"; then
    sudo -u postgres createdb -E UTF8 -O "${DB_RENDER}" "${DB_NAME}"
    sudo -u postgres psql -d "${DB_NAME}" -c "CREATE EXTENSION IF NOT EXISTS postgis;"
    sudo -u postgres psql -d "${DB_NAME}" -c "CREATE EXTENSION IF NOT EXISTS hstore;"
    sudo -u postgres psql -d "${DB_NAME}" -c "ALTER TABLE geometry_columns OWNER TO ${DB_RENDER};"
    sudo -u postgres psql -d "${DB_NAME}" -c "ALTER TABLE spatial_ref_sys OWNER TO ${DB_RENDER};"
    sudo -u postgres psql -d "${DB_NAME}" -c "ALTER TABLE geography_columns OWNER TO ${DB_RENDER};"
    log_info "База данных '${DB_NAME}' создана."
else
    log_warn "База данных '${DB_NAME}' уже существует, пропускаем создание."
fi

sudo -u postgres psql -d "${DB_NAME}" -c "GRANT CREATE ON SCHEMA public TO ${DB_APP};"
sudo -u postgres psql -d "${DB_NAME}" -c "GRANT USAGE ON SCHEMA public TO ${DB_APP};"
sudo -u postgres psql -d "${DB_NAME}" -c "GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO ${DB_APP};"
sudo -u postgres psql -d "${DB_NAME}" -c "GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO ${DB_APP};"

log_info "Подготовка директорий и стилей..."
mkdir -p "${OPT_DIR}" "${DATA_DIR}" "${TILE_DIR}"
chown -R "${APP_USER}:${APP_USER}" "${OPT_DIR}"
chown -R "${DB_RENDER}:${DB_RENDER}" "${TILE_DIR}"

if [ ! -d "${CARTO_DIR}/.git" ]; then
    git clone --quiet https://github.com/gravitystorm/openstreetmap-carto.git "${CARTO_DIR}"
    cd "${CARTO_DIR}" && git switch --quiet --detach "${CARTO_VERSION}"
    carto project.mml > style.xml 2>/dev/null || true
    log_info "Стили загружены."
else
    log_warn "Стили уже загружены, пропускаем."
fi

chown -R "${DB_RENDER}:${DB_RENDER}" "${CARTO_DIR}"
chmod -R 775 "${CARTO_DIR}"

log_info "Настройка renderd..."
cat > /etc/renderd.conf << EOF
[renderd]
pid_file=/run/renderd/renderd.pid
socketname=/run/renderd/renderd.sock
num_threads=4
tile_dir=${TILE_DIR}

[mapnik]
plugins_dir=${MAPNIK_DIR}
font_dir=/usr/share/fonts/truetype
font_dir_recurse=true

[default]
URI=/osm_tiles/
TILEDIR=${TILE_DIR}
XML=${CARTO_DIR}/style.xml
HOST=localhost
TILESIZE=256
MAXZOOM=20
EOF


log_info "Настройка Apache..."
a2enmod tile headers > /dev/null 2>&1 || true

cat > /etc/apache2/sites-available/osm-tiles.conf << EOF
<VirtualHost *:80>
    ServerName localhost
    ModTileTileDir ${TILE_DIR}
    AddTileConfig /osm_tiles/ default
    Header always set Access-Control-Allow-Origin "*"
    DocumentRoot /var/www/html
    <Directory /var/www/html>
        Require all granted
    </Directory>
</VirtualHost>
EOF

a2ensite osm-tiles > /dev/null 2>&1 || true
a2dissite 000-default > /dev/null 2>&1 || true

cat > /var/www/html/index.html << HTMLEOF
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>OpenStreetMap Tile Server</title>
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css"/>
    <style>body { margin: 0; padding: 0; } #map { width: 100%; height: 100vh; }</style>
</head>
<body>
    <div id="map"></div>
    <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
    <script>
        var map = L.map('map').setView([50, 130], 5);
        L.tileLayer('/osm_tiles/{z}/{x}/{y}.png', {
            attribution: '&copy; OpenStreetMap contributors',
            maxZoom: 20
        }).addTo(map);
    </script>
</body>
</html>
HTMLEOF
chmod 644 /var/www/html/index.html

log_info "Настройка passwordless sudo..."
echo "${APP_USER} ALL=(${DB_RENDER}) NOPASSWD: /usr/bin/osm2pgsql" > /etc/sudoers.d/osm-update
chmod 440 /etc/sudoers.d/osm-update

log_info "Скачивание и импорт данных OSM (это займет 15-30 минут)..."

OSM_URL="https://download.geofabrik.de/russia/far-eastern-fed-district-latest.osm.pbf"
OSM_FILE="${DATA_DIR}/far-eastern-fed-district-latest.osm.pbf"

if [ ! -f "${OSM_FILE}" ]; then
    log_info "Скачивание ${OSM_URL}..."
    wget -q --show-progress -O "${OSM_FILE}" "${OSM_URL}"
else
    log_warn "Файл данных уже существует, пропускаем скачивание."
fi

TABLE_EXISTS=$(sudo -u postgres psql -d "${DB_NAME}" -tAc \
    "SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_name='planet_osm_point');")

if [ "${TABLE_EXISTS}" != "t" ]; then
    log_info "Импорт данных в базу данных..."
    sudo -u "${DB_RENDER}" osm2pgsql \
        -d "${DB_NAME}" \
        --create --slim -G --hstore \
        -S "${CARTO_DIR}/openstreetmap-carto.style" \
        -C 2500 --number-processes "$(nproc)" \
        "${OSM_FILE}"

    log_info "Создание индексов и функций..."
    [ -f "${CARTO_DIR}/indexes.sql" ] && sudo -u "${DB_RENDER}" psql -d "${DB_NAME}" -f "${CARTO_DIR}/indexes.sql" > /dev/null 2>&1 || log_warn "indexes.sql не найден, пропускаем."
    [ -f "${CARTO_DIR}/functions.sql" ] && sudo -u "${DB_RENDER}" psql -d "${DB_NAME}" -f "${CARTO_DIR}/functions.sql" > /dev/null 2>&1 || log_warn "functions.sql не найден, пропускаем."

    log_info "Загрузка внешних данных (береговые линии, ледники)..."
    cd "${CARTO_DIR}"
    sudo -u "${DB_RENDER}" python3 scripts/get-external-data.py

    log_info "Импорт данных завершен."
else
    log_warn "Таблицы OSM уже существуют, пропускаем импорт (данные в безопасности)."
fi

log_info "Создание таблиц утилиты..."

sudo -u postgres psql -d "${DB_NAME}" << 'EOSQL'
-- Таблица пользователей
CREATE TABLE IF NOT EXISTS "Users" (
    "Id" SERIAL PRIMARY KEY,
    "Username" TEXT NOT NULL UNIQUE,
    "PasswordHash" TEXT NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Таблица регионов
CREATE TABLE IF NOT EXISTS "MapRegions" (
    "Id" SERIAL PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Code" TEXT NOT NULL UNIQUE,
    "GeofabrikUrl" TEXT NOT NULL,
    "StateUrl" TEXT,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "LastUpdateTimestamp" TIMESTAMP,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP DEFAULT NOW(),
    "AutoUpdate" BOOLEAN NOT NULL DEFAULT FALSE
);

-- Журнал обновлений
CREATE TABLE IF NOT EXISTS "UpdateLogs" (
    "Id" SERIAL PRIMARY KEY,
    "RegionId" INTEGER NOT NULL REFERENCES "MapRegions"("Id") ON DELETE CASCADE,
    "UpdateType" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "StartedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "FinishedAt" TIMESTAMP,
    "DurationSeconds" INTEGER,
    "LogOutput" TEXT,
    "ErrorMessage" TEXT,
    "PbfFilePath" TEXT,
    "OscFilePath" TEXT,
    "RecordsProcessed" INTEGER,
    "FromTimestamp" TIMESTAMP,
    "ToTimestamp" TIMESTAMP
);

-- Настройки системы
CREATE TABLE IF NOT EXISTS "update_settings" (
    "key" TEXT PRIMARY KEY,
    "value" TEXT,
    "description" TEXT,
    "updated_at" TIMESTAMP DEFAULT NOW()
);

-- Дефолтные настройки
INSERT INTO "update_settings" ("key", "value", "description") VALUES
    ('UpdateMode', 'Confirm', 'Режим обновления: Confirm или Auto'),
    ('ScheduleType', 'Daily', 'Периодичность: Daily, Every3Days, Weekly'),
    ('ScheduleHour', '3', 'Час запуска (0-23)'),
    ('CartoDir', '/opt/osm-update/openstreetmap-carto', 'Директория стилей'),
    ('TileDir', '/var/lib/mod_tile', 'Директория тайлов')
ON CONFLICT ("key") DO NOTHING;

-- Регион Дальнего Востока по умолчанию
INSERT INTO "MapRegions" ("Name", "Code", "GeofabrikUrl", "StateUrl", "IsActive", "AutoUpdate")
VALUES (
    'Дальневосточный ФО',
    'far-eastern-fed-district',
    'https://download.geofabrik.de/russia/far-eastern-fed-district-latest.osm.pbf',
    'https://download.geofabrik.de/russia/far-eastern-fed-district-updates/state.txt',
    TRUE,
    FALSE
)
ON CONFLICT ("Code") DO NOTHING;

-- Индексы
CREATE INDEX IF NOT EXISTS "IX_UpdateLogs_RegionId" ON "UpdateLogs" ("RegionId");
CREATE INDEX IF NOT EXISTS "IX_UpdateLogs_Status" ON "UpdateLogs" ("Status");
CREATE INDEX IF NOT EXISTS "IX_UpdateLogs_StartedAt" ON "UpdateLogs" ("StartedAt" DESC);
CREATE INDEX IF NOT EXISTS "IX_MapRegions_Code" ON "MapRegions" ("Code");
CREATE INDEX IF NOT EXISTS "IX_Users_Username" ON "Users" ("Username");

-- Выдаем права osm_app на новые таблицы
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO osm_app;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO osm_app;
EOSQL

sudo -u postgres psql -d "${DB_NAME}" -c "
    ALTER TABLE \"Users\" OWNER TO ${DB_APP};
    ALTER TABLE \"MapRegions\" OWNER TO ${DB_APP};
    ALTER TABLE \"UpdateLogs\" OWNER TO ${DB_APP};
    ALTER TABLE \"update_settings\" OWNER TO ${DB_APP};
    ALTER SEQUENCE \"Users_Id_seq\" OWNER TO ${DB_APP};
    ALTER SEQUENCE \"MapRegions_Id_seq\" OWNER TO ${DB_APP};
    ALTER SEQUENCE \"UpdateLogs_Id_seq\" OWNER TO ${DB_APP};
"

log_info "Таблицы утилиты созданы, регион Дальнего Востока добавлен."

log_info "Перезапуск сервисов..."
systemctl enable renderd apache2 > /dev/null 2>&1
systemctl restart renderd apache2

touch /opt/osm-update/.server_initialized

log_info "Установка завершена успешно"

echo ""
echo "Интерактивная карта: http://${SERVER_IP}/"
echo "Прямой доступ к тайлу: http://${SERVER_IP}/osm_tiles/0/0/0.png"
echo ""
echo "Данные для веб-утилиты:"
echo "  Хост: localhost"
echo "  Порт: 5432"
echo "  База: ${DB_NAME}"
echo "  Пользователь: ${DB_APP}"
echo "  Пароль: ${DB_APP_PASS}"
echo "  Carto Dir: ${CARTO_DIR}"
echo "  Tile Dir: ${TILE_DIR}"
echo ""
echo "Полезные команды:"
echo "  sudo journalctl -u renderd -f          # Логи renderd"
echo "  sudo systemctl restart renderd          # Перезапуск renderd"
echo "  sudo systemctl status renderd apache2   # Статус сервисов"
echo ""
