# Lampac Ukraine Modules (`LME.*`)

Набір українських онлайн-модулів для Lampac NextGen.
Усі модулі використовують префікс `LME.` (Lampac Modules Extended), щоб уникати конфліктів із вбудованими модулями Lampac.

## Навігація

- [Українська](#ua)
- [English](#en)

## <a id="ua"></a>Українська

### Доступні модулі

**Фільми та серіали**
- `LME.Uaflix`
- `LME.Makhno`
- `LME.StarLight`
- `LME.KlonFUN`
- `LME.UafilmME`
- `LME.JackTor`

**Аніме та дорами**
- `LME.AnimeON`
- `LME.Bamboo`
- `LME.Unimay`
- `LME.Mikai`
- `LME.NMoonAnime`

### Ручне встановлення

1. Клонуйте репозиторій:
```bash
git clone https://github.com/lampame/lampac-ukraine.git .
```

2. Скопіюйте потрібні теки модулів у директорію `module` вашого Lampac.

3. Для Docker приклад монтування:
```bash
-v /path/to/lampac-ukraine/LME.Uaflix:/lampac/module/LME.Uaflix
```

### Автовстановлення через `repository.yaml`

Працює у Lampac `148.1+`.

Створіть або оновіть `module/repository.yaml`:

```yaml
- repository: https://github.com/lampame/lampac-ukraine
  branch: main
  modules:
    - LME.Shared
    - LME.AnimeON
    - LME.Unimay
    - LME.Mikai
    - LME.NMoonAnime
    - LME.Uaflix
    - LME.Bamboo
    - LME.Makhno
    - LME.StarLight
    - LME.KlonFUN
    - LME.UafilmME
    - LME.JackTor
```

Важливо:
- `branch` — необов'язково, за замовчуванням `main`.
- `modules` — необов'язково; якщо не вказано, встановляться всі модулі з репозиторію.
- Якщо ви вказуєте конкретний список `modules`, додавайте `LME.Shared`, бо інші модулі підключають спільні файли через `syntaxPaths`.

### Налаштування в `init.conf`

Ключ має збігатися з назвою модуля (`LME.XXX`), а не з назвою провайдера.

Приклад для `LME.Uaflix`:

```json
"LME.Uaflix": {
  "enable": true,
  "domain": "https://uaflix.net",
  "displayname": "Uaflix",
  "login": null,
  "passwd": null,
  "cookie": null,
  "webcorshost": null,
  "streamproxy": false,
  "useproxy": false,
  "proxy": {
    "useAuth": true,
    "username": "FooBAR",
    "password": "Strong_password",
    "list": [
      "socks5://adress:port"
    ]
  },
  "displayindex": 1,
  "magic_apn": {
    "ashdi": "https://tut.im/proxy.php?url={encodeurl}"
  }
}
```

Сумісність параметрів:
- `webcorshost` + `useproxy`: працюють разом (парсинг через CORS-хост, мережевий вихід може йти через проксі).
- `webcorshost` + `streamproxy`: не конфліктують (CORS для парсингу, `streamproxy` для потоків).
- `magic_apn.ashdi` використовується лише для Ashdi-посилань і лише коли значення не порожнє.
- `webcorshost` + `magic_apn`: не конфліктують.

### Приклад конфігурації `LME.JackTor`

```json
"LME.JackTor": {
  "enable": true,
  "displayname": "JackTor",
  "displayindex": 0,

  "jackett": "jackett.app",
  "apikey": "YOUR_JACKETT_API_KEY",

  "min_sid": 5,
  "min_peers": 0,
  "max_size": 0,
  "max_serial_size": 0,
  "max_age_days": 0,

  "forceAll": false,
  "emptyVoice": true,
  "sort": "sid",
  "query_mode": "both",
  "year_tolerance": 1,

  "quality_allow": [2160, 1080, 720],
  "hdr_mode": "any",
  "codec_allow": "any",
  "audio_pref": ["ukr", "eng", "rus"],

  "trackers_allow": ["toloka", "rutracker", "noname-club"],
  "trackers_block": ["selezen"],

  "filter": "",
  "filter_ignore": "(camrip|ts|telesync)",

  "torrs": [
    "http://127.0.0.1:8090"
  ],
  "auth_torrs": [
    {
      "enable": true,
      "host": "http://ts.example.com:8090",
      "login": "{account_email}",
      "passwd": "StrongPassword",
      "country": "UA",
      "no_country": null,
      "headers": {
        "x-api-key": "your-ts-key"
      }
    }
  ],
  "base_auth": {
    "enable": false,
    "login": "{account_email}",
    "passwd": "StrongPassword",
    "headers": {}
  },

  "group": 0,
  "group_hide": true
}
```

Ключові параметри:
- `jackett` + `apikey`: хост Jackett та API-ключ.
- `min_sid` / `min_peers` / `max_size` / `max_serial_size`: базові фільтри торрентів.
- `quality_allow`, `hdr_mode`, `codec_allow`, `audio_pref`: пріоритезація якості, кодека та мов.
- `torrs`, `auth_torrs`, `base_auth`: вузли TorrServer для відтворення.
- `filter` / `filter_ignore`: regex-фільтри для релізів та озвучок.

### Скрипт перевірки доступності джерел

```bash
wget -O check.sh https://raw.githubusercontent.com/lampame/lampac-ukraine/main/check.sh && sh check.sh
```

### Підтримка

Підтримати автора: https://lampame.donatik.me

---

## <a id="en"></a>English

### Available modules

**TV shows and movies**
- `LME.Uaflix`
- `LME.Makhno`
- `LME.StarLight`
- `LME.KlonFUN`
- `LME.UafilmME`
- `LME.JackTor`

**Anime and dorama**
- `LME.AnimeON`
- `LME.Bamboo`
- `LME.Unimay`
- `LME.Mikai`
- `LME.NMoonAnime`

### Manual installation

1. Clone the repository:
```bash
git clone https://github.com/lampame/lampac-ukraine.git .
```

2. Copy required module folders into Lampac `module` directory.

3. Docker mount example:
```bash
-v /path/to/lampac-ukraine/LME.Uaflix:/lampac/module/LME.Uaflix
```

### Auto installation via `repository.yaml`

Requires Lampac `148.1+`.

Create or update `module/repository.yaml`:

```yaml
- repository: https://github.com/lampame/lampac-ukraine
  branch: main
  modules:
    - LME.Shared
    - LME.AnimeON
    - LME.Unimay
    - LME.Mikai
    - LME.NMoonAnime
    - LME.Uaflix
    - LME.Bamboo
    - LME.Makhno
    - LME.StarLight
    - LME.KlonFUN
    - LME.UafilmME
    - LME.JackTor
```

Notes:
- `branch` is optional, default is `main`.
- `modules` is optional; if omitted, all repository modules are installed.
- If you specify an explicit module list, include `LME.Shared` because other modules use shared files through `syntaxPaths`.

### `init.conf` key rule

Use module name (`LME.XXX`) as a key, not provider name.
Example: `LME.Uaflix` instead of `Uaflix`.

### Source/player availability check script

```bash
wget -O check.sh https://raw.githubusercontent.com/lampame/lampac-ukraine/main/check.sh && sh check.sh
```

### Support

Support the author: https://lampame.donatik.me
