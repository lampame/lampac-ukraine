#!/usr/bin/env sh
set -u

TIMEOUT="${TIMEOUT:-12}"
USER_AGENT="Mozilla/5.0 (simple-source-player-checker)"

if ! command -v curl >/dev/null 2>&1; then
  echo "Потрібен curl, але він не встановлений."
  exit 1
fi

# IMPORTANT:
# Вкажіть тут КОНКРЕТНІ сторінки та (за потреби) окремий endpoint для перевірки плеєрів.
# Формат: SourceName|PageURL|ProbeURL
SOURCES=$(cat <<'SRC'
Uaflix|https://uafix.net/films/nozhi-nagolo-3/|
AnimeON|https://animeon.club/anime/924-provodzhalnicya-friren|https://animeon.club/api/player/47960/episode
Bamboo|https://bambooua.com/dorama/938-18_again.html|
Mikai|https://mikai.me/anime/1272-friren-shcho-provodzhaie-v-ostanniu-put|https://api.mikai.me/v1/anime/1272
KlonFUN|https://klon.fun/filmy/3887-marsianyn-marsiianyn-rozshyrena-versiia.html|
SRC
)

# Підтримувані провайдери плеєрів
PROVIDERS="Ashdi Zetvideo Moonanime Tortuga BambooPlayer"

source_tmp=$(mktemp)
provider_tmp=$(mktemp)
stream_tmp=$(mktemp)
trap 'rm -f "$source_tmp" "$provider_tmp" "$stream_tmp"' 0

http_code() {
  url="$1"
  code=$(curl -A "$USER_AGENT" -L -k -sS -o /dev/null -w '%{http_code}' --max-time "$TIMEOUT" "$url" 2>/dev/null || true)
  [ -z "$code" ] && code="000"
  printf '%s' "$code"
}

http_ok() {
  code="$1"
  [ "$code" -ge 200 ] 2>/dev/null && [ "$code" -lt 400 ] 2>/dev/null
}

extract_provider_urls() {
  html="$1"

  normalized=$(printf '%s' "$html" | sed 's#\\/#/#g')

  {
    # Повні та protocol-relative URL
    printf '%s' "$normalized" \
      | tr "\"'()<>," '\n' \
      | grep -Eio '(https?:)?//[^ )>]*(ashdi\.vip|zetvideo\.net|moonanime\.art|tortuga)[^ )>]*' \
      | sed 's#^//#https://#'

    # Випадок, коли домен вбудований без схеми (ashdi.vip/...)
    printf '%s' "$normalized" \
      | tr "\"'()<>," '\n' \
      | grep -Eio '(ashdi\.vip|zetvideo\.net|moonanime\.art|tortuga)[^ )>]*' \
      | sed 's#^#https://#'
  } | awk '!seen[$0]++'
}

provider_from_url() {
  url="$1"
  lurl=$(printf '%s' "$url" | tr '[:upper:]' '[:lower:]')
  case "$lurl" in
    *ashdi.vip*) echo "Ashdi" ;;
    *zetvideo.net*) echo "Zetvideo" ;;
    *moonanime.art*) echo "Moonanime" ;;
    *tortuga*) echo "Tortuga" ;;
    *friends.bambooua.com*) echo "BambooPlayer" ;;
    *) echo "Unknown" ;;
  esac
}

join_unique_providers() {
  awk '
    !seen[$0]++ {
      out = out (out ? ", " : "") $0
    }
    END {
      print out
    }
  '
}

extract_stream_urls() {
  html="$1"
  printf '%s' "$html" \
    | sed 's#\\/#/#g' \
    | tr "\"'()<>," '\n' \
    | grep -Eio '(https?:)?//[^ )>]+\.(m3u8|mp4)(\?[^ )>]*)?' \
    | sed 's#^//#https://#' \
    | awk '!seen[$0]++'
}

add_query_param() {
  url="$1"
  param="$2"
  case "$url" in
    *\?*) printf '%s&%s' "$url" "$param" ;;
    *) printf '%s?%s' "$url" "$param" ;;
  esac
}

provider_referer() {
  provider="$1"
  case "$provider" in
    Ashdi) printf '%s' "https://ashdi.vip/" ;;
    Zetvideo) printf '%s' "https://zetvideo.net/" ;;
    Moonanime) printf '%s' "https://animeon.club/" ;;
    Tortuga) printf '%s' "https://tortuga.tw/" ;;
    *) printf '%s' "" ;;
  esac
}

provider_candidate_urls() {
  provider="$1"
  base_url="$2"
  printf '%s\n' "$base_url"

  if [ "$provider" = "Moonanime" ]; then
    case "$base_url" in
      *player=*) ;;
      *)
        add_query_param "$base_url" "player=animeon.club"
        printf '\n'
        add_query_param "$base_url" "player=mikai.me"
        printf '\n'
        ;;
    esac
  fi
}

decode_base64_auto() {
  value="$1"
  decoded=$(printf '%s' "$value" | base64 -d 2>/dev/null || true)
  if [ -z "$decoded" ]; then
    decoded=$(printf '%s' "$value" | base64 -D 2>/dev/null || true)
  fi
  printf '%s' "$decoded"
}

extract_file_property_urls() {
  html="$1"
  oneline=$(printf '%s' "$html" | sed 's#\\/#/#g' | tr '\n' ' ')

  printf '%s' "$oneline" \
    | grep -Eoi "file[[:space:]]*:[[:space:]]*['\"][^'\"]+['\"]" \
    | sed -E "s/.*file[[:space:]]*:[[:space:]]*['\"]([^'\"]+)['\"].*/\1/" \
    | sed 's#^//#https://#' \
    | grep -Eiv '^\[|^\{' \
    | grep -E '^(https?:)?//' \
    | awk '!seen[$0]++'
}

extract_obfuscated_file_urls() {
  html="$1"
  oneline=$(printf '%s' "$html" | sed 's#\\/#/#g' | tr '\n' ' ')

  printf '%s' "$oneline" \
    | grep -Eoi "file[[:space:]]*:[[:space:]]*['\"][^'\"]+['\"]" \
    | sed -E "s/.*file[[:space:]]*:[[:space:]]*['\"]([^'\"]+)['\"].*/\1/" \
    | while IFS= read -r raw; do
        [ -z "$raw" ] && continue
        # Не URL: пробуємо декодувати (Tortuga часто повертає base64-обфускацію)
        if printf '%s' "$raw" | grep -Eq '^(https?:)?//'; then
          continue
        fi

        if ! printf '%s' "$raw" | grep -Eq '^[A-Za-z0-9+/=]+$'; then
          continue
        fi

        decoded=$(decode_base64_auto "$raw")
        [ -z "$decoded" ] && continue

        # Частий формат Tortuga: base64 від перевернутого URL
        reversed=$(printf '%s' "$decoded" | rev)
        if printf '%s' "$reversed" | grep -Eq '^(https?:)?//'; then
          printf '%s\n' "$reversed"
          continue
        fi

        if printf '%s' "$decoded" | grep -Eq '^(https?:)?//'; then
          printf '%s\n' "$decoded"
        fi
      done \
    | sed 's#^//#https://#' \
    | awk '!seen[$0]++'
}

extract_stream_from_player_html() {
  html="$1"
  {
    extract_stream_urls "$html"
    extract_file_property_urls "$html"
    extract_obfuscated_file_urls "$html"
  } | awk '!seen[$0]++'
}

best_sample_player() {
  provider="$1"
  case "$provider" in
    Ashdi)
      awk -F'|' -v p="$provider" '$2==p && index($3, "/vod/") > 0 {print $3; exit}' "$provider_tmp"
      ;;
    Zetvideo)
      awk -F'|' -v p="$provider" '$2==p && index($3, "/vod/") > 0 {print $3; exit}' "$provider_tmp"
      ;;
    Tortuga)
      awk -F'|' -v p="$provider" '$2==p && index($3, "/vod/") > 0 {print $3; exit}' "$provider_tmp"
      ;;
    *)
      ;;
  esac
}

# Uaflix geo-block detection (textual)
is_uaflix_geoblocked() {
  html="$1"
  printf '%s' "$html" | grep -Eiq 'доступн(ий|а|і).*(лише|тільки).*(україн|україні)|недоступн(ий|а|і).*(ваш(ій|ей) країні|регіоні)|geo.?block|бачите тільки трейлер|пройдіть авторизацію'
}

echo "Перевірка джерел, плеєрів і стрімів..."
echo

while IFS='|' read -r source_name page_url probe_url; do
  [ -z "$source_name" ] && continue

  code=$(http_code "$page_url")
  page_status="FAIL($code)"
  if http_ok "$code"; then
    page_status="OK($code)"
  fi

  fetch_url="$page_url"
  [ -n "${probe_url:-}" ] && fetch_url="$probe_url"
  page_body=$(curl -A "$USER_AGENT" -L -k -sS --max-time "$TIMEOUT" "$fetch_url" 2>/dev/null || true)

  geo_note=""
  if [ "$source_name" = "Uaflix" ] && is_uaflix_geoblocked "$page_body"; then
    geo_note="GEO_BLOCK"
  fi

  provider_list=""
  extract_provider_urls "$page_body" | while IFS= read -r purl; do
    [ -z "$purl" ] && continue
    provider=$(provider_from_url "$purl")
    [ "$provider" = "Unknown" ] && continue
    printf '%s|%s|%s\n' "$source_name" "$provider" "$purl" >> "$provider_tmp"
  done

  # Bamboo має власний відеоплеєр з прямим src на m3u8/mp4
  if [ "$source_name" = "Bamboo" ]; then
    bamboo_stream=$(extract_stream_urls "$page_body" | head -n 1)
    if [ -n "$bamboo_stream" ]; then
      printf '%s|%s|%s\n' "$source_name" "BambooPlayer" "$bamboo_stream" >> "$provider_tmp"
    fi
  fi

  provider_list=$(awk -F'|' -v s="$source_name" '$1==s {print $2}' "$provider_tmp" | join_unique_providers)
  [ -z "$provider_list" ] && provider_list="NOT_FOUND"

  printf '%s|%s|%s|%s|%s\n' "$source_name" "$page_url" "$page_status" "$provider_list" "$geo_note" >> "$source_tmp"
done <<EOF_SRC
$SOURCES
EOF_SRC

# Для кожного унікального провайдера пробуємо дістати стрім хоча б з 1 його плеєра
for provider in $PROVIDERS; do
  sample_player=$(best_sample_player "$provider")
  if [ -z "$sample_player" ]; then
    sample_player=$(awk -F'|' -v p="$provider" '$2==p {print $3; exit}' "$provider_tmp")
  fi

  if [ -z "$sample_player" ]; then
    printf '%s|%s|%s\n' "$provider" "NO_PLAYER_URL" "NOT_FOUND" >> "$stream_tmp"
    continue
  fi

  # Для прямих stream URL (наприклад, BambooPlayer) перевіряємо одразу
  if printf '%s' "$sample_player" | grep -Eiq '\.(m3u8|mp4)(\?[^ ]*)?$'; then
    scode=$(http_code "$sample_player")
    if http_ok "$scode"; then
      printf '%s|%s|%s\n' "$provider" "$sample_player" "OK($scode)" >> "$stream_tmp"
    else
      printf '%s|%s|%s\n' "$provider" "$sample_player" "FAIL($scode)" >> "$stream_tmp"
    fi
    continue
  fi

  stream_url=""
  referer=$(provider_referer "$provider")
  for candidate in $(provider_candidate_urls "$provider" "$sample_player" | awk '!seen[$0]++'); do
    if [ -n "$referer" ]; then
      pbody=$(curl -A "$USER_AGENT" -e "$referer" -L -k -sS --max-time "$TIMEOUT" "$candidate" 2>/dev/null || true)
    else
      pbody=$(curl -A "$USER_AGENT" -L -k -sS --max-time "$TIMEOUT" "$candidate" 2>/dev/null || true)
    fi

    stream_url=$(extract_stream_from_player_html "$pbody" | head -n 1)
    [ -n "$stream_url" ] && break
  done

  if [ -z "$stream_url" ]; then
    printf '%s|%s|%s\n' "$provider" "$sample_player" "GEO_BLOCK" >> "$stream_tmp"
    continue
  fi

  scode=$(http_code "$stream_url")
  if http_ok "$scode"; then
    printf '%s|%s|%s\n' "$provider" "$sample_player" "OK($scode)" >> "$stream_tmp"
  else
    printf '%s|%s|%s\n' "$provider" "$sample_player" "FAIL($scode)" >> "$stream_tmp"
  fi
done

echo "=== Звіт 1: Джерело - сторінка/плеєр ==="
while IFS='|' read -r source_name page_url page_status provider_list geo_note; do
  [ -z "$source_name" ] && continue
  if [ -n "$geo_note" ]; then
    echo "- ${source_name}: сторінка ${page_status%%(*}(${geo_note}), плеєр: ${provider_list}"
  else
    echo "- ${source_name}: сторінка ${page_status}, плеєр: ${provider_list}"
  fi
done < "$source_tmp"

echo
echo "=== Звіт 2: Плеєр - доступність стріма (унікально) ==="
while IFS='|' read -r provider sample status; do
  [ -z "$provider" ] && continue
  echo "- ${provider} - ${status}"
done < "$stream_tmp"

echo
echo "Готово."
