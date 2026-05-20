---
applyTo: "**"
---

Estás trabajando en **Windows 11** con **PowerShell** como único shell disponible.

- NUNCA uses comandos bash/Linux (`ls`, `cat`, `rm -rf`, `grep`, `find`, `chmod`, `export`, `&&`).
- Usa siempre sus equivalentes PowerShell (`Get-ChildItem`, `Get-Content`, `Remove-Item -Recurse -Force`, `Select-String`, `Get-ChildItem -Recurse`, `$env:VAR = "val"`, `;`).
- Las rutas usan `\` como separador. Nunca uses `/home/`, `/usr/`, `/etc/` ni rutas Unix absolutas.
- Variables de entorno en scripts: `$env:VAR`. En tasks.json/launch.json: `${env:VAR}`.
