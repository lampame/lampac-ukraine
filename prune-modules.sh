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
#        - цілі теки <repo>, які колись були встановлені через repository.yaml
#          (видно в .repository_state.json), але тепер прибрані з конфігу;
#        - теки <module> всередині repo, які не вказані в modules: списку;
#        - теки <module> всередині repo без modules: (режим "поставити все"),
#          яких уже немає у віддаленому репозиторії (звірка з GitHub).
#   4. НІКОЛИ не чіпає теки, що не керуються repository.yaml:
#      вбудовані модулі Lampac (AdminPanel, Online, SISI, ...) і вручну
#      встановлені модулі в module/ або mods/ лишаються недоторканими.
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
#   - Для repo без modules: потрібен доступ до GitHub API (curl або wget),
#     інакше таке repo безпечно пропускається.
#   - Приватні репозиторії, що потребують токена, у режимі "поставити все"
#     пропускаються (скрипт токена не знає).

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

# state-файл лежить поруч із конфігом (як у ModuleRepository)
STATE_FILE="$(dirname "$CONFIG")/.repository_state.json"

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
        function ownerpath(url,    n, parts) {
            sub(/^[A-Za-z0-9+.-]*:\/\//, "", url)
            sub(/^git@[^:]*:/, "", url)
            gsub(/\/+$/, "", url)
            sub(/\.git$/, "", url)
            n = split(url, parts, "/")
            if (n >= 2) return parts[n-1] "/" parts[n]
            return ""
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
                print "REPO\t" repo "\t" ownerpath(line)
                next
            }

            # ключ-заголовок рівня 0 у map-формі (наприклад "ukraine:")
            if (indent == 0 && line ~ /:[[:space:]]*$/) { reset(); next }

            # ключ гілки
            if (line ~ /^branch:/ || line ~ /^[-][[:space:]]+branch:/) {
                sub(/^[-][[:space:]]+branch:[[:space:]]*/, "", line)
                sub(/^branch:[[:space:]]*/, "", line)
                gsub(/^[\x27\x22]|[\x27\x22]$/, "", line)
                if (repo != "") print "BRANCH\t" repo "\t" line
                next
            }

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
REPO_FILE="$TMPDIR_SAFE/prune-modules.repo.$$"
BRANCH_FILE="$TMPDIR_SAFE/prune-modules.branch.$$"
KNOWN_FILE="$TMPDIR_SAFE/prune-modules.known.$$"
: > "$MANAGED_FILE"; : > "$HASMODS_FILE"; : > "$EXPECTED_FILE"
: > "$REPO_FILE"; : > "$BRANCH_FILE"; : > "$KNOWN_FILE"

parse_yaml "$CONFIG" > "$PARSE_FILE"

while IFS=$'\t' read -r kind value extra; do
    [[ -z "$value" ]] && continue
    case "$kind" in
        REPO)
            echo "$value" >> "$MANAGED_FILE"
            [[ -n "$extra" ]] && printf '%s\t%s\n' "$value" "$extra" >> "$REPO_FILE"
            ;;
        BRANCH)
            printf '%s\t%s\n' "$value" "$extra" >> "$BRANCH_FILE"
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
trap 'rm -f "$MANAGED_FILE" "$HASMODS_FILE" "$EXPECTED_FILE" "$REPO_FILE" "$BRANCH_FILE" "$KNOWN_FILE"' EXIT

# "відомі" репо = ті, що є в конфізі + ті, що колись встановлювались через
# repository.yaml (видно в .repository_state.json). Це дозволяє безпечно
# відрізнити repo-теки від вбудованих/ручних модулів Lampac і не чіпати останні.
cp "$MANAGED_FILE" "$KNOWN_FILE"
if [[ -f "$STATE_FILE" ]]; then
    awk '
        function extract(rest,    ident, parts) {
            if (match(rest, /[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+/)) {
                ident = substr(rest, RSTART, RLENGTH)
                split(ident, parts, "/")
                print parts[2]
            }
        }
        {
            line = $0
            idx = index(line, "cache:branch-sha:")
            if (idx) extract(substr(line, idx + 18))
            idx = index(line, "cache:repo-default-branch:")
            if (idx) extract(substr(line, idx + 27))
            idx = index(line, "etag:repo:")
            if (idx) extract(substr(line, idx + 10))
            idx = index(line, "etag:branch:")
            if (idx) extract(substr(line, idx + 12))
        }
    ' "$STATE_FILE" >> "$KNOWN_FILE"
fi
sort -u "$KNOWN_FILE" -o "$KNOWN_FILE"

managed_count=$(sort -u "$MANAGED_FILE" | grep -c . || true)
expected_count=$(grep -c . "$EXPECTED_FILE" || true)
echo "prune-modules: репозиторіїв у конфізі: $managed_count"
echo "prune-modules: очікуваних module/<repo>/<mod> теків: $expected_count"

# --- допоміжні функції -------------------------------------------------------
repo_owner_path() { awk -F'\t' -v n="$1" '$1==n {print $2; exit}' "$REPO_FILE"; }
repo_branch()    { awk -F'\t' -v n="$1" '$1==n {print $2; exit}' "$BRANCH_FILE"; }

fetch_body() {
    if command -v curl >/dev/null 2>&1; then
        curl -sSL "$1"
    elif command -v wget >/dev/null 2>&1; then
        wget -qO - "$1"
    else
        return 1
    fi
}

http_code() {
    if command -v curl >/dev/null 2>&1; then
        curl -sSL -o /dev/null -w '%{http_code}' "$1" 2>/dev/null
    elif command -v wget >/dev/null 2>&1; then
        wget -q -O /dev/null --server-response "$1" 2>&1 | awk 'NR==1{print $2}'
    fi
}

# поточні теки верхнього рівня репозиторію (як FetchRepositoryFolders у C#).
# Спершу GitHub API (JSON), потім fallback на github.com (HTML) — бо api.github.com
# часто обмежений лімітом (403) або заблокований там, де github.com ще працює.
github_dirs_branch() {
    local op="$1" br="$2" url out
    # 1) GitHub API
    url="https://api.github.com/repos/${op}/contents?ref=${br}"
    out="$(fetch_body "$url" 2>/dev/null \
        | tr '{' '\n' \
        | grep '"type":"dir"' \
        | grep -oE '"name":"[^"]+"' \
        | sed -E 's/"name":"([^"]+)"/\1/' || true)"
    if [[ -n "$out" ]]; then
        printf '%s\n' "$out"
        return 0
    fi
    # 2) github.com (HTML-дерево) — лише теки верхнього рівня
    url="https://github.com/${op}/tree/${br}"
    fetch_body "$url" 2>/dev/null \
        | grep -oE "href=\"/${op}/tree/${br}/[^/?#\"]+" \
        | sed -E "s#href=\"/${op}/tree/${br}/##" \
        | sort -u
}

GITHUB_STATUS=""
github_dirs() {
    local op="$1" br="$2" b out first=1
    out=""
    GITHUB_STATUS=""
    for b in "$br" main master; do
        if [[ -n "$first" ]]; then
            GITHUB_STATUS="$(http_code "https://api.github.com/repos/${op}/contents?ref=${b}" 2>/dev/null || true)"
            first=""
        fi
        out="$(github_dirs_branch "$op" "$b" || true)"
        if [[ -n "$out" ]]; then
            printf '%s\n' "$out"
            return 0
        fi
    done
}

# --- збір списку на видалення ------------------------------------------------
REMOVE=()

# файли, які завжди треба зберігати в module/
KEEP_FILES=("repository.yaml" "repository.example.yaml" ".repository_state.json")

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

    [[ -d "$entry" ]] || continue

    if grep -Fqx "$name" "$MANAGED_FILE"; then
        # репо є в конфізі
        if grep -Fqx "$name" "$HASMODS_FILE"; then
            # явний modules: — прибираємо підтеки, яких немає в списку
            for mod in "$entry"/*; do
                [[ -d "$mod" ]] || continue
                mname="$(basename "$mod")"
                if ! grep -Fqx "$name/$mname" "$EXPECTED_FILE"; then
                    REMOVE+=("$mod")
                fi
            done
        else
            # install-all режим: звіряємо підтеки з віддаленим репозиторієм
            op="$(repo_owner_path "$name")"
            br="$(repo_branch "$name")"
            [[ -z "$br" ]] && br="main"
            if [[ -z "$op" ]]; then
                echo "prune-modules: [пропуск, не вдалось визначити repo] $name"
                continue
            fi
            current="$(github_dirs "$op" "$br" || true)"
            if [[ -z "$current" ]]; then
                if [[ -n "$GITHUB_STATUS" && "$GITHUB_STATUS" != "200" ]]; then
                    echo "prune-modules: [пропуск, GitHub недоступний (HTTP ${GITHUB_STATUS}) для] $name"
                else
                    echo "prune-modules: [пропуск, немає тек у GitHub для] $name"
                fi
                continue
            fi
            for mod in "$entry"/*; do
                [[ -d "$mod" ]] || continue
                mname="$(basename "$mod")"
                if ! printf '%s\n' "$current" | grep -Fqx "$mname"; then
                    REMOVE+=("$mod")
                fi
            done
        fi
    elif grep -Fqx "$name" "$KNOWN_FILE"; then
        # репо було встановлене через repository.yaml, але прибране з конфігу
        REMOVE+=("$entry")
    else
        # вбудований або вручну встановлений модуль Lampac — не чіпаємо
        echo "prune-modules: [пропуск, вбудований/ручний модуль] $name"
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
