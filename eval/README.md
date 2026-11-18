# eval/ · harness de evaluación de clasificación

## Harness

`src/DocumentIA.Batch.Evaluation` es la consola de evaluación (comando `run`, y `compare` para
contrastar dos ejecuciones). `run` clasifica un conjunto (`--set golden`) contra un entorno
(`--env DEV`) con la configuración de `--config`, deja los resultados en `--resume <dir>`
(`results.csv`, `run-info.json`) y admite `--only-files <ruta>`: fichero de texto con un nombre
de fichero por línea; solo se procesan esos. No ignora líneas con `#`, así que la lista no
lleva comentarios.

## Scripts

- `run_golden_luna_dev.ps1`: lanza contra DEV la pasada de control (`config-dev.json`) y/o la
  candidata (`config-dev-luna.json`), ambas en `eval/runs/` (gitignored). Parámetros: `-Phase`
  (`both`, `control`, `luna`), `-ControlDir`, `-LunaDir` (para reanudar), `-Passes` (intentos con
  `--resume`, 3), `-Parallel` (2), `-MaxPages` (10), `-Corpus`, `-OnlyFiles` (lista de ficheros
  para `--only-files`), `-Detached` (segundo plano) y `-WhatIf` (solo imprime). Nunca contra
  PRE ni PRO.
- `build_muestra_cache.py` (`python -I eval/build_muestra_cache.py`): genera la muestra fija a
  partir de `golden.csv` (solo `validated == True`): las 10 familias `expected_tdn1` con más
  filas (desempate alfabético) y, de cada una, los 5 primeros `file_name` ordenados.

## Muestra de caché (AB#101413)

- `golden-muestra-cache.txt`: los 50 nombres de la muestra, uno por línea; es la entrada de
  `-OnlyFiles`.
- `golden-muestra-cache.sha256`: `<sha256>  golden.csv  generado <fecha>`; identifica la versión
  de `golden.csv` de la que salió la muestra. Si cambia `golden.csv`, se regenera con el script.

## Ejemplo

    pwsh eval/run_golden_luna_dev.ps1 -Phase luna -OnlyFiles eval/golden-muestra-cache.txt -WhatIf
