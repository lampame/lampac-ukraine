# Ukraine online source for Lampac

## Table of contents

- [Ukraine online source for Lampac](#ukraine-online-source-for-lampac)
  - [Table of contents](#table-of-contents)
  - [Installation](#installation)
  - [Auto installation](#auto-installation)
  - [Init support](#init-support)
  - [Donate](#donate)


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
    - Anihub
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
    "displayindex": 1
  }
```
## Donate

Support the author: https://lampame.donatik.me
