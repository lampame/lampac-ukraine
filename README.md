# Ukraine online source for Lampac

## Sources
### TVShows and Movies

- [x] UAFlix
- [x] Makhno 
- [x] StarLight
- [x] KlonFUN

### Anime and Dorama
- [x] AnimeON
- [x] BambooUA
- [x] Unimay
- [x] Mikai 
- [x] NMoonAnime

## Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/lampac-ukraine/lampac-ukraine.git .
   ```

2. Move the modules to the correct directory:
   - If Lampac is installed system-wide, move the modules to the `module` directory.
   - If Lampac is running in Docker, mount the volume:
     ```bash
     -v /path/to/your/cloned/repo/Uaflix:/home/module/Uaflix
     ```

## Auto installation

If Lampac version 148.1 and newer

Create or update the module/repository.yaml file

```YAML
- repository: https://github.com/lampame/lampac-ukraine
  branch: main
  modules:
    - AnimeON
    - Unimay
    - Mikai
    - NMoonAnime
    - Uaflix
    - Bamboo
    - Makhno
    - StarLight
    - KlonFUN
```

branch - optional, default main

modules - optional, if not specified, all modules from the repository will be installed

## Init support

```json
"Uaflix": {
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
    "apn": true,
    "apn_host": "domaine.com/{encodeurl}"
  }
```

Parameter compatibility:
- `webcorshost` + `useproxy`: work together (parsing via CORS host, and network output can go through a proxy with `useproxy`).
- `webcorshost` does not conflict with `streamproxy`: CORS is used for parsing, `streamproxy` is used for streaming.
- `webcorshost` does not conflict with `apn`: APN is used at the streaming stage, not for regular parsing.

## JackTor config example (`init.conf`)

```json
"JackTor": {
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

Key parameters at a glance:
- `jackett` + `apikey`: your Jackett host and API key.
- `min_sid` / `min_peers` / `max_size` / `max_serial_size`: base torrent filters.
- `quality_allow`, `hdr_mode`, `codec_allow`, `audio_pref`: quality/codec/language prioritization.
- `torrs`, `auth_torrs`, `base_auth`: TorrServer nodes used for playback.
- `filter` / `filter_ignore`: regex filters for release title and voice labels.

## APN support

Sources with APN support:
- AnimeON
- Uaflix
- Mikai
- Makhno
- KlonFUN
- NMoonAnime

## Source/player availability check script

```bash
wget -O check.sh https://raw.githubusercontent.com/lampame/lampac-ukraine/main/check.sh && sh check.sh
```

## Donate

Support the author: https://lampame.donatik.me
