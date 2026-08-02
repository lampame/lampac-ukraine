#!/usr/bin/env bash
#
# prune-modules.sh — прибирає модулі, яких більше немає в repository.yaml.
#
# Чому потрібно:
#   ModuleRepository.UpdateModules() (Shared/Services/Module/ModuleRepository.cs)
#   тільки завантажує/оновлює модулі, вказані в конфізі. Видалених з
#   repository.yaml модулів він НЕ прибирає — вони лишаються на диску
#   (module/<repo>/<module>/), і при старті Lampac продовжують завантажуватись.
#
# Що робить скрипт:
#   1. Знаходить активний конфіг: mods/repository.yaml, інакше module/repository.yaml.
#   2. Парсить його та будує "очікуваний" набір module/<repo>/<module>/.
#   3. Видаляє:
#        - цілі теки <repo>, яких немає в конфізі взагалі (репозиторій прибрано);
#        - теки <module> всередині repo, які не вказані в modules: списку.
#   4. НЕ чіпає repo без явного modules: (режим "поставити все з репо") —
#      статично невідомо, які там модулі.
#
# Безпека:
#   За замовчуванням — dry-run (тільки показує, що буде видалено).
#   Реальне видалення лише з прапором --apply.
#
# Використання:
#   ./prune-modules.sh                 # dry-run
#   ./prune-modules.sh --apply         # реально видалити
#   LAMPAC_DIR=/opt/lampac ./prune-modules.sh --apply
#
# Важливо: скрипт треба запускати КОЛИ Lampac зупинено (або одразу після
# зупинки), інакше модулі, що вже завантажені в памʼять, усе одно працюватимуть
# до наступного перезапуску. Найкраще: зупинити службу -> prune --apply -> старт.
#
# Обмеження:
#   - Парсить список-форму (modules: список рядків). Flow-listу
#     "modules: [A, B]" теж розуміє. Dict-форму modules: {A: B} — ні.
#   - Репо без modules: скрипт пропускає (режим авто-вибору всіх тек).

set -euo pipefail

# --- пошук робочої директорії Lampac ---------------------------------------
if [[ -n "${LAMPAC_DIR:-}" ]]; then
    BASE="$LAMPAC_DIR"
else
    # спочатку директорія, де лежить сам скрипт (якщо це схоже на інсталяцію);
    # при запуску через pipe (bash -s) BASH_SOURCE порожній — беремо поточну теку
    SCRIPT_DIR=""
    if [[ -n "${BASH_SOURCE[0]:-}" ]]; then
        SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    fi

    if [[ -n "$SCRIPT_DIR" && ( -d "$SCRIPT_DIR/module" || -d "$SCRIPT_DIR/mods" ) ]]; then
        BASE="$SCRIPT_DIR"
    else
        BASE="$PWD"
    fi
fi

MODULE_DIR="$BASE/module"
MODS_DIR="$BASE/mods"

# --- активний конфіг --------------------------------------------------------
if [[ -f "$MODS_DIR/repository.yaml" ]]; then
    CONFIG="$MODS_DIR/repository.yaml"
elif [[ -f "$MODULE_DIR/repository.yaml" ]]; then
    CONFIG="$MODULE_DIR/repository.yaml"
else
    echo "prune-modules: конфіг не знайдено ($MODS_DIR/repository.yaml або $MODULE_DIR/repository.yaml)" >&2
    exit 1
fi

APPLY=false
for arg in "$@"; do
    case "$arg" in
        --apply) APPLY=true ;;
        -h|--help) grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "prune-modules: невідомий аргумент '$arg' (дозволено: --apply, --help)" >&2; exit 2 ;;
    esac
done

if [[ ! -d "$MODULE_DIR" ]]; then
    echo "prune-modules: теки $MODULE_DIR немає — нічого чистити."
    exit 0
fi

echo "prune-modules: конфіг = $CONFIG"
echo "prune-modules: module dir = $MODULE_DIR"

# --- парсинг repository.yaml ------------------------------------------------
# На виході: два "файли" через тимчасові теки — списки репозиторіїв і пар repo/module.
parse_yaml() {
    awk '
        function reponame(url,    n, parts, name) {
            sub(/^[A-Za-z0-9+.-]*:\/\//, "", url)   # https://, git@, ssh://...
            sub(/^git@[^:]*:/, "", url)
            gsub(/\/+$/, "", url)
            n = split(url, parts, "/")
            name = parts[n]
            sub(/\.git$/, "", name)
            return name
        }
        function reset() { repo=""; inmods=0; hasmods=0 }
        {
            line = $0
            if (line ~ /^[[:space:]]*$/ || line ~ /^[[:space:]]*#/) next
            match(line, /^[[:space:]]*/); indent = RLENGTH
            sub(/^[[:space:]]+/, "", line)

            # ключ URL репозиторію (list: "- repository: ...", map: "  repository: ...")
            if (line ~ /^[-][[:space:]]+(repository|repo|url|git|remote):/ ||
                line ~ /^(repository|repo|url|git|remote):/) {
                sub(/^[-][[:space:]]+(repository|repo|url|git|remote):[[:space:]]*/, "", line)
                sub(/^(repository|repo|url|git|remote):[[:space:]]*/, "", line)
                gsub(/^[\x27\x22]|[\x27\x22]$/, "", line)
                repo = reponame(line)
                inmods = 0; hasmods = 0
                print "REPO\t" repo
                next
            }

            # ключ-заголовок рівня 0 у map-формі (наприклад "ukraine:")
            if (indent == 0 && line ~ /:[[:space:]]*$/) { reset(); next }

            # ключ списку модулів
            if (line ~ /^(modules|folders|directories|paths|include):/) {
                rest = line
                sub(/^(modules|folders|directories|paths|include):[[:space:]]*/, "", rest)
                inmods = 1; hasmods = 1
                if (rest ~ /^\[/) {   # flow list: modules: [A, B]
                    sub(/^\[/, "", rest); sub(/\]$/, "", rest)
                    n = split(rest, arr, /[,[:space:]]+/)
                    for (i = 1; i <= n; i++)
                        if (arr[i] != "") { gsub(/[\x27\x22]/, "", arr[i]); print "MOD\t" repo "/" arr[i] }
                    inmods = 0
                }
                next
            }

            # елемент списку модулів
            if (inmods && line ~ /^[-][[:space:]]+/) {
                sub(/^[-][[:space:]]+/, "", line)
                gsub(/[[:space:]]+$/, "", line)
                gsub(/^[\x27\x22]|[\x27\x22]$/, "", line)
                print "MOD\t" repo "/" line
                next
            }

            # будь-який інший ключ завершує блок modules
            inmods = 0
        }
    ' "$1"
}

TMPDIR_SAFE="${TMPDIR:-/tmp}"
PARSE_FILE="$TMPDIR_SAFE/prune-modules.parse.$$"
MANAGED_FILE="$TMPDIR_SAFE/prune-modules.managed.$$"
HASMODS_FILE="$TMPDIR_SAFE/prune-modules.hasmods.$$"
EXPECTED_FILE="$TMPDIR_SAFE/prune-modules.expected.$$"
: > "$MANAGED_FILE"; : > "$HASMODS_FILE"; : > "$EXPECTED_FILE"

parse_yaml "$CONFIG" > "$PARSE_FILE"

while IFS=$'\t' read -r kind value; do
    [[ -z "$value" ]] && continue
    case "$kind" in
        REPO)
            echo "$value" >> "$MANAGED_FILE"
            ;;
        MOD)
            repo="${value%%/*}"
            mod="${value#*/}"
            echo "$repo" >> "$MANAGED_FILE"
            echo "$repo" >> "$HASMODS_FILE"
            echo "$repo/$mod" >> "$EXPECTED_FILE"
            ;;
    esac
done < "$PARSE_FILE"

rm -f "$PARSE_FILE"
trap 'rm -f "$MANAGED_FILE" "$HASMODS_FILE" "$EXPECTED_FILE"' EXIT

managed_count=$(sort -u "$MANAGED_FILE" | grep -c . || true)
expected_count=$(grep -c . "$EXPECTED_FILE" || true)
echo "prune-modules: репозиторіїв у конфізі: $managed_count"
echo "prune-modules: очікуваних module/<repo>/<mod> теків: $expected_count"

# --- збір списку на видалення ------------------------------------------------
REMOVE=()

# файли, які завжди треба зберігати в module/
KEEP_FILES=("repository.yaml" ".repository_state.json")

for entry in "$MODULE_DIR"/*; do
    [[ -e "$entry" ]] || continue
    name="$(basename "$entry")"

    keep=false
    for k in "${KEEP_FILES[@]}"; do [[ "$name" == "$k" ]] && keep=true; done
    if $keep; then continue; fi

    if [[ -f "$entry" ]]; then
        # невідомий файл у корені module/ — не чіпаємо (безпечніше лишити)
        echo "prune-modules: [пропуск файлу] $name"
        continue
    fi

    if [[ -d "$entry" ]]; then
        if ! grep -qx "$name" "$MANAGED_FILE"; then
            REMOVE+=("$entry")
        else
            if grep -qx "$name" "$HASMODS_FILE"; then
                for mod in "$entry"/*; do
                    [[ -d "$mod" ]] || continue
                    mname="$(basename "$mod")"
                    if ! grep -qx "$name/$mname" "$EXPECTED_FILE"; then
                        REMOVE+=("$mod")
                    fi
                done
            else
                echo "prune-modules: [пропуск, repo без modules:] $name"
            fi
        fi
    fi
done

# --- вивід / виконання --------------------------------------------------------
if [[ ${#REMOVE[@]} -eq 0 ]]; then
    echo "prune-modules: нічого видаляти."
    exit 0
fi

echo "prune-modules: буде видалено (${#REMOVE[@]}):"
for d in "${REMOVE[@]}"; do
    echo "  rm -rf $d"
done

if ! $APPLY; then
    echo
    echo "Це був dry-run. Щоб реально видалити — запустіть з --apply."
    echo "Попередньо зупиніть Lampac і запустіть скрипт, а тоді стартуйте знову."
    exit 0
fi

for d in "${REMOVE[@]}"; do
    rm -rf -- "$d"
    echo "prune-modules: видалено $d"
done

echo "prune-modules: готово."
