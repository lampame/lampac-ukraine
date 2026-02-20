# Ukraine online source for Lampac

## Sources
### TVShows and Movies

- [x] UAFlix
- [x] UATuTFun
- [x] Makhno 
- [x] StarLight
- [x] KlonFUN

### Anime and Dorama
- [x] AnimeON
- [x] BambooUA
- [x] Unimay
- [x] Mikai 

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
    - UATuT
    - Uaflix
    - UaTUT
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

## APN support

Sources with APN support:
- AnimeON
- Uaflix
- UaTUT
- Mikai
- Makhno
- KlonFUN

## Source/player availability check script

```bash
wget -O check.sh https://raw.githubusercontent.com/lampame/lampac-ukraine/main/check.sh && sh check.sh
```

## Donate

Support the author: https://lampame.donatik.me
