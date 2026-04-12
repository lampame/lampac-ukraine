# Ukraine online source for Lampac NextGen

> **Important:** All modules use the prefix `LME.` (Lampac Modules Extended) to avoid conflicts with Lampac's built-in modules.
> Text names, namespaces, keys in `init.conf`, and routes all use the prefix `LME.`.

## Sources
### TVShows and Movies

- [x] LME.Uaflix
- [x] LME.Makhno
- [x] LME.StarLight
- [x] LME.KlonFUN
- [x] LME.UafilmME

### Anime and Dorama
- [x] LME.AnimeON
- [x] LME.Bamboo
- [x] LME.Unimay
- [x] LME.Mikai
- [x] LME.NMoonAnime

## Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/lampac-ukraine/lampac-ukraine.git .
   ```

2. Move the modules to the correct directory:
   - If Lampac is installed system-wide, move the modules to the `module` directory.
   - If Lampac is running in Docker, mount the volume:
     ```bash
     -v /path/to/your/cloned/repo/LME.Uaflix:/home/module/LME.Uaflix
     ```

## Auto installation

If Lampac version 148.1 and newer

Create or update the module/repository.yaml file

```YAML
- repository: https://github.com/lampac-ukraine/lampac-ukraine
  branch: main
  modules:
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

branch - optional, default main

modules - optional, if not specified, all modules from the repository will be installed

## Init support

> **Note:** The key in `init.conf` must match the module name (`LME.XXX`), **not** the provider name.
> For example, for Uaflix, use `“LME.Uaflix”`, not `“Uaflix”`.

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

Parameter compatibility:
- `webcorshost` + `useproxy`: work together (parsing via CORS host, and network output can go through a proxy with `useproxy`).
- `webcorshost` does not conflict with `streamproxy`: CORS is used for parsing, `streamproxy` is used for streaming.
- `magic_apn.ashdi` is used only for Ashdi links and only when the value is not empty.
- `webcorshost` does not conflict with `magic_apn`: CORS is used for parsing, while `magic_apn` is used for Ashdi streaming.

## JackTor config example (`init.conf`)

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

Key parameters at a glance:
- `jackett` + `apikey`: your Jackett host and API key.
- `min_sid` / `min_peers` / `max_size` / `max_serial_size`: base torrent filters.
- `quality_allow`, `hdr_mode`, `codec_allow`, `audio_pref`: quality/codec/language prioritization.
- `torrs`, `auth_torrs`, `base_auth`: TorrServer nodes used for playback.
- `filter` / `filter_ignore`: regex filters for release title and voice labels.

## Source/player availability check script

```bash
wget -O check.sh https://raw.githubusercontent.com/lampame/lampac-ukraine/main/check.sh && sh check.sh
```

## Donate

Support the author: https://lampame.donatik.me
