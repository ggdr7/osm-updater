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

log_info "Начинаем безопасную установку/обновление OSM сервера..."

log_info "Установка зависимостей..."
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
    sudo -u "${DB_RENDER}" psql -d "${DB_NAME}" -f "${CARTO_DIR}/indexes.sql" > /dev/null 2>&1
    sudo -u "${DB_RENDER}" psql -d "${DB_NAME}" -f "${CARTO_DIR}/functions.sql" > /dev/null 2>&1

    log_info "Загрузка внешних данных (береговые линии, ледники)..."
    cd "${CARTO_DIR}"
    sudo -u "${DB_RENDER}" python3 scripts/get-external-data.py

    log_info "Импорт данных завершен."
else
    log_warn "Таблицы OSM уже существуют, пропускаем импорт (данные в безопасности)."
fi

log_info "Перезапуск сервисов..."
systemctl enable renderd apache2 > /dev/null 2>&1
systemctl restart renderd apache2

touch /opt/osm-update/.server_initialized


log_info "Установка завершена успешно!"

echo "Данные для веб-утилиты:"
echo "  Хост: localhost"
echo "  Порт: 5432"
echo "  База: ${DB_NAME}"
echo "  Пользователь: ${DB_APP}"
echo "  Пароль: ${DB_APP_PASS}"
echo "  Carto Dir: ${CARTO_DIR}"
echo "  Tile Dir: ${TILE_DIR}"
