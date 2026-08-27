#!/usr/bin/env bash
# HelloDoctor veritabanı yedeği.
#
#   ./scripts/backup.sh              # backups/ altına yazar
#   BACKUP_DIR=/mnt/yedek ./scripts/backup.sh
#   RETENTION_DAYS=30 ./scripts/backup.sh
#
# Redis yedeklenmez: yalnızca varlık ve görüşme eşleşmesi tutuyor, ikisi de
# geçici. Kaybolursa açık görüşmeler düşer, veri kaybı olmaz.
set -euo pipefail

CONTAINER="${DB_CONTAINER:-hellodoctor-db}"
DB_NAME="${DB_NAME:-hellodoctor}"
DB_USER="${DB_USER:-hellodoctor}"
BACKUP_DIR="${BACKUP_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/backups}"
RETENTION_DAYS="${RETENTION_DAYS:-14}"

if ! docker ps --format '{{.Names}}' | grep -qx "$CONTAINER"; then
  echo "HATA: '$CONTAINER' konteyneri çalışmıyor." >&2
  exit 1
fi

mkdir -p "$BACKUP_DIR"
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
FILE="$BACKUP_DIR/hellodoctor-$STAMP.dump"

echo "Yedekleniyor → $FILE"

# Dump konteyner içinde üretiliyor. Ana makinede pg_restore olmayabilir ve
# özel biçimli dump'ı doğrulamak seek edilebilir bir dosya istiyor — boru
# hattından okunamıyor.
REMOTE="/tmp/hellodoctor-$STAMP.dump"
cleanup() { docker exec "$CONTAINER" rm -f "$REMOTE" 2>/dev/null || true; }
trap cleanup EXIT

# -Fc: sıkıştırılmış özel biçim. Düz SQL'e göre küçük ve pg_restore ile
# seçmeli geri yüklemeye izin veriyor.
docker exec "$CONTAINER" pg_dump -U "$DB_USER" -d "$DB_NAME" -Fc --no-owner --no-acl \
  -f "$REMOTE"

# Yarım kalmış bir dump'ın geçerli yedek sanılması, yedeğin hiç olmamasından
# kötü: dosya ancak okunabildiği doğrulandıktan sonra dışarı alınıyor.
if ! docker exec "$CONTAINER" pg_restore --list "$REMOTE" > /dev/null 2>&1; then
  echo "HATA: Üretilen dump okunamadı, yedek alınmadı." >&2
  exit 1
fi

docker cp "$CONTAINER:$REMOTE" "$FILE" > /dev/null

SIZE=$(du -h "$FILE" | cut -f1)
ROWS=$(docker exec "$CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -t -A -c \
  "SELECT 'kullanıcı ' || (SELECT count(*) FROM \"Users\") ||
          ', mesaj ' || (SELECT count(*) FROM \"Messages\") ||
          ', randevu ' || (SELECT count(*) FROM \"Appointments\");")

echo "✓ $SIZE — $ROWS"

# Eski yedekleri buda. -mtime kullanılmıyor: dosya adındaki damga daha güvenilir
# (kopyalama mtime'ı değiştirebilir).
CUTOFF=$(date -u -v-"${RETENTION_DAYS}"d +%Y%m%dT%H%M%SZ 2>/dev/null \
      || date -u -d "${RETENTION_DAYS} days ago" +%Y%m%dT%H%M%SZ)

PRUNED=0
for f in "$BACKUP_DIR"/hellodoctor-*.dump; do
  [ -e "$f" ] || continue
  name=$(basename "$f" .dump)
  stamp=${name#hellodoctor-}
  if [[ "$stamp" < "$CUTOFF" ]]; then
    rm -f "$f"
    PRUNED=$((PRUNED + 1))
  fi
done
[ "$PRUNED" -gt 0 ] && echo "  $PRUNED eski yedek silindi (>${RETENTION_DAYS} gün)"

COUNT=$(ls -1 "$BACKUP_DIR"/hellodoctor-*.dump 2>/dev/null | wc -l | tr -d ' ')
echo "  $BACKUP_DIR içinde $COUNT yedek"
