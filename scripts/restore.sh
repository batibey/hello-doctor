#!/usr/bin/env bash
# HelloDoctor veritabanını bir yedekten geri yükler.
#
#   ./scripts/restore.sh backups/hellodoctor-20260827T120000Z.dump
#   ./scripts/restore.sh --latest
#   ./scripts/restore.sh --latest --force      # onay sorma (otomasyon için)
#
# DİKKAT: Mevcut veritabanının üzerine yazar. Geri yüklemeden önce mevcut
# durumun otomatik yedeği alınır; yanlış dosyayla çalıştırıldığında geri
# dönülebilsin diye.
set -euo pipefail

CONTAINER="${DB_CONTAINER:-hellodoctor-db}"
DB_NAME="${DB_NAME:-hellodoctor}"
DB_USER="${DB_USER:-hellodoctor}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BACKUP_DIR="${BACKUP_DIR:-$ROOT/backups}"

FILE=""
FORCE=0
for arg in "$@"; do
  case "$arg" in
    --latest) FILE=$(ls -1t "$BACKUP_DIR"/hellodoctor-*.dump 2>/dev/null | head -1) ;;
    --force)  FORCE=1 ;;
    *)        FILE="$arg" ;;
  esac
done

if [ -z "$FILE" ] || [ ! -f "$FILE" ]; then
  echo "Kullanım: $0 <yedek.dump> | --latest [--force]" >&2
  [ -d "$BACKUP_DIR" ] && { echo "Mevcut yedekler:"; ls -1t "$BACKUP_DIR"/hellodoctor-*.dump 2>/dev/null | head -5; }
  exit 2
fi

if ! docker ps --format '{{.Names}}' | grep -qx "$CONTAINER"; then
  echo "HATA: '$CONTAINER' konteyneri çalışmıyor." >&2
  exit 1
fi

echo "Geri yüklenecek : $FILE"
echo "Hedef           : $DB_NAME @ $CONTAINER"

CURRENT=$(docker exec "$CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -t -A -c \
  "SELECT 'kullanıcı ' || (SELECT count(*) FROM \"Users\") ||
          ', mesaj ' || (SELECT count(*) FROM \"Messages\");" 2>/dev/null || echo "(okunamadı)")
echo "Şu anki durum   : $CURRENT"

if [ "$FORCE" -ne 1 ]; then
  printf "\nMevcut veritabanının ÜZERİNE yazılacak. Devam? [evet/hayır] "
  read -r answer
  [ "$answer" = "evet" ] || { echo "İptal edildi."; exit 3; }
fi

STAMP=$(date -u +%Y%m%dT%H%M%SZ)
REMOTE="/tmp/restore-$STAMP.dump"
SAFETY_REMOTE="/tmp/pre-restore-$STAMP.dump"
cleanup() { docker exec "$CONTAINER" rm -f "$REMOTE" "$SAFETY_REMOTE" 2>/dev/null || true; }
trap cleanup EXIT

# Geri yüklemeden önceki hali sakla: yanlış dosya seçildiyse dönüş yolu kalsın.
SAFETY="$BACKUP_DIR/pre-restore-$STAMP.dump"
mkdir -p "$BACKUP_DIR"
docker exec "$CONTAINER" pg_dump -U "$DB_USER" -d "$DB_NAME" -Fc --no-owner --no-acl \
  -f "$SAFETY_REMOTE"
docker cp "$CONTAINER:$SAFETY_REMOTE" "$SAFETY" > /dev/null
echo "Geri yükleme öncesi yedek: $SAFETY"

# Dosya konteynere kopyalanıyor: özel biçimli dump seek edilebilir olmalı.
docker cp "$FILE" "$CONTAINER:$REMOTE" > /dev/null

# Açık bağlantılar şemayı kilitler; backend çalışıyorsa DROP takılır.
docker exec "$CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -q -c \
  "SELECT pg_terminate_backend(pid) FROM pg_stat_activity
   WHERE datname = '$DB_NAME' AND pid <> pg_backend_pid();" > /dev/null

# --clean --if-exists: dump içindeki nesneler önce düşürülür. public şemayı
# komple silmek yerine bunu kullanıyoruz ki uzantılar ve yetkiler korunsun.
docker exec "$CONTAINER" pg_restore -U "$DB_USER" -d "$DB_NAME" \
  --clean --if-exists --no-owner --no-acl "$REMOTE"

RESTORED=$(docker exec "$CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -t -A -c \
  "SELECT 'kullanıcı ' || (SELECT count(*) FROM \"Users\") ||
          ', mesaj ' || (SELECT count(*) FROM \"Messages\") ||
          ', randevu ' || (SELECT count(*) FROM \"Appointments\");")

echo "✓ Geri yüklendi : $RESTORED"
echo
echo "Backend'i yeniden başlatın: bağlantı havuzunda kalan bağlantılar koptu."
