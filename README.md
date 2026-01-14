# Ukraine online source for Lampac

- [x] AnimeON
- [x] BambooUA
- [x] CikavaIdeya
- [x] StarLight
- [x] UAKino
- [x] UAFlix
- [x] UATuTFun
- [x] Unimay


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
    - CikavaIdeya
    - Uaflix
    - UaTUT
    - Bamboo
    - UAKino
    - StarLight
```

branch - optional, default main

modules - optional, if not specified, all modules from the repository will be installed

## Init support

```json
"Uaflix": {
    "enable": true,
    "domain": "https://uaflix.net",
    "displayname": "Uaflix",
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

## APN support

Sources with APN support:
- AnimeON
- Uaflix
- UaTUT
- CikavaIdeya
- UAKino

## Donate

Support the author: https://lampame.donatik.me
