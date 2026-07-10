# Extracción Estafeta Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construir un script PowerShell 7 ("Extracción Estafeta") que fuerza la extracción de un lote de PDFs organizados por subcarpeta de tipología contra el backend PROD y genera un Excel por tipología (formato idéntico al batch), un Excel resumen y un log.

**Architecture:** Script standalone que descubre subcarpetas → código de tipología, envía cada PDF a `/api/ingest` con `forceReprocess`/`skipDuplicateCheck` y `expectedType` (sin bloque `assetResolver`, lo decide el orquestador), hace polling del durable status, guarda el output JSON y construye los Excel portando 1:1 la lógica del batch mediante las mismas APIs `System.Text.Json` y `System.IO.Compression` que usa el WPF. La lógica pura vive en ficheros `lib/*.ps1` dot-sourceados; el entry-point orquesta y paraleliza.

**Tech Stack:** PowerShell 7.6+, `System.Text.Json`, `System.Net.Http.HttpClient`, `System.IO.Compression`, `System.Xml.XmlWriter`, Pester v5.

## Global Constraints

- Runtime: **PowerShell 7+** (`pwsh`) — se usa `ForEach-Object -Parallel`. No compatible con Windows PowerShell 5.1.
- Ubicación (repo `DocumentIA.Batch`): script en `scripts/extraccion-estafeta/`, tests en `scripts/extraccion-estafeta/tests/`.
- Backend objetivo por defecto: **PROD**. La function key nunca se hardcodea (parámetro o env var `DOCUMENTIA_FUNCTION_KEY`).
- El request de ingest **no** incluye `assetResolver` (el orquestador decide por config de tipología).
- `skipGDCUpload` por defecto **true** (extracción de prueba).
- El formato Excel debe ser **idéntico** al del batch: cabeceras y `FormatCellValue` portadas de `src/DocumentIA.Batch/Services/BatchExportRows.cs` y `BatchOutputJsonReader.cs`, y writer portado de `BatchExcelExportService.cs` (celdas `inlineStr`, sin módulos externos).
- Sin referencias de autoría ni menciones a modelos en código, commits ni docs.
- Mapeo carpeta→código: reemplazar `_` por `.`.
- Comparaciones de propiedades JSON case-insensitive con fallback (casing mixto camel/Pascal en prod).

## File Structure

```
scripts/extraccion-estafeta/
  Invoke-ExtraccionEstafeta.ps1     # entry-point: params, config, descubrimiento, orquestación, salidas
  lib/
    OutputJson.ps1                  # port de BatchOutputJsonReader (System.Text.Json)
    ExportTable.ps1                 # port de BatchExportRows (headers, fields, filas)
    ExcelWriter.ps1                 # port de BatchExcelExportService (xlsx multi-hoja)
    IngestClient.ps1                # port de DocumentIaBackendClient (ingest + polling) + builders puros
    Outputs.ps1                     # KPIs, _log.csv, _run.json, _resumen.xlsx
  tests/
    OutputJson.Tests.ps1
    ExportTable.Tests.ps1
    ExcelWriter.Tests.ps1
    IngestClient.Tests.ps1
    Discovery.Tests.ps1
    fixtures/
      output-A.json                 # output real (2 campos DatosExtraidos)
      output-B.json                 # output real (campo extra, casing Pascal)
  README.md
```

Cada `lib/*.ps1` define funciones puras (prefijo `EE`) y es dot-sourceable por el entry-point y por los tests. El entry-point y el bloque `-Parallel` dot-sourcean estos ficheros.

---

### Task 1: Scaffolding + mapeo carpeta→código

**Files:**
- Create: `scripts/extraccion-estafeta/lib/ExportTable.ps1`
- Test: `scripts/extraccion-estafeta/tests/ExportTable.Tests.ps1`

**Interfaces:**
- Produces: `Get-EETipologiaCode([string]$FolderName) -> [string]`

- [ ] **Step 1: Asegurar Pester v5**

Run: `pwsh -NoProfile -Command "(Get-Module -ListAvailable Pester | Sort-Object Version -Descending | Select-Object -First 1).Version"`
Expected: `5.x.x`. Si no aparece: `pwsh -NoProfile -Command "Install-Module Pester -Scope CurrentUser -MinimumVersion 5.0 -Force"`.

- [ ] **Step 2: Write the failing test**

Create `scripts/extraccion-estafeta/tests/ExportTable.Tests.ps1`:

```powershell
BeforeAll {
    . "$PSScriptRoot/../lib/ExportTable.ps1"
}

Describe 'Get-EETipologiaCode' {
    It 'reemplaza guion bajo por punto' {
        Get-EETipologiaCode 'cera.44_vado' | Should -Be 'cera.44.vado'
    }
    It 'deja intactos los codigos sin guion bajo' {
        Get-EETipologiaCode 'comu.04' | Should -Be 'comu.04'
    }
    It 'maneja multiples guiones bajos' {
        Get-EETipologiaCode 'cera.44_basura' | Should -Be 'cera.44.basura'
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests/ExportTable.Tests.ps1"`
Expected: FAIL (`Get-EETipologiaCode` no definido / fichero lib no existe).

- [ ] **Step 4: Write minimal implementation**

Create `scripts/extraccion-estafeta/lib/ExportTable.ps1`:

```powershell
Set-StrictMode -Version Latest

function Get-EETipologiaCode {
    param([Parameter(Mandatory)][string]$FolderName)
    return $FolderName.Replace('_', '.')
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests/ExportTable.Tests.ps1"`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add scripts/extraccion-estafeta/lib/ExportTable.ps1 scripts/extraccion-estafeta/tests/ExportTable.Tests.ps1
git commit -m "feat(extraccion-estafeta): mapeo carpeta a codigo de tipologia"
```

---

### Task 2: Port del lector de output JSON

**Files:**
- Create: `scripts/extraccion-estafeta/lib/OutputJson.ps1`
- Create: `scripts/extraccion-estafeta/tests/fixtures/output-A.json`
- Create: `scripts/extraccion-estafeta/tests/fixtures/output-B.json`
- Test: `scripts/extraccion-estafeta/tests/OutputJson.Tests.ps1`

**Interfaces:**
- Produces:
  - `Open-EEJsonDocument([string]$Path) -> [System.Text.Json.JsonDocument] | $null`
  - `ConvertTo-EECellValue([System.Text.Json.JsonElement]$Value) -> [string]`
  - `Get-EETryPath([System.Text.Json.JsonElement]$Root,[string]$Path) -> @{ Found=[bool]; Value=[JsonElement] }`
  - `Get-EEPathValue([System.Text.Json.JsonElement]$Root,[string]$Path) -> [string]`
  - `Get-EEObjectPropertyNames([System.Text.Json.JsonElement]$Root,[string]$Path) -> [string[]]`

- [ ] **Step 1: Crear fixtures**

Create `scripts/extraccion-estafeta/tests/fixtures/output-A.json`:

```json
{
  "Identificacion": { "Documento": "docA.pdf", "Guid": "11111111-1111-1111-1111-111111111111", "Tipologia": "cera.44.vado" },
  "Integridad": { "CRC32": "AAAA", "SHA256": "hashA" },
  "DatosExtraidos": { "Titular": "Juan", "Importe": 1250.5 },
  "DetalleEjecucion": { "Extraccion": { "Modelo": "gpt", "ConfianzaPorCampo": { "Titular": 0.98, "Importe": 0.90 } } },
  "Resultado": { "Estado": "OK", "ConfianzaGlobal": 0.95, "EstadoCalidad": "Aprobado" }
}
```

Create `scripts/extraccion-estafeta/tests/fixtures/output-B.json` (usa casing Pascal en `DatosExtraidos` y añade un campo nuevo):

```json
{
  "Identificacion": { "Documento": "docB.pdf", "Guid": "22222222-2222-2222-2222-222222222222", "Tipologia": "cera.44.vado" },
  "Integridad": { "CRC32": "BBBB", "SHA256": "hashB" },
  "DatosExtraidos": { "Titular": "Ana", "Referencia": "REF-9" },
  "DetalleEjecucion": { "Extraccion": { "Modelo": "gpt", "ConfianzaPorCampo": { "Titular": 0.7, "Referencia": 0.6 } } },
  "Resultado": { "Estado": "OK", "ConfianzaGlobal": 0.8, "EstadoCalidad": "Revision" }
}
```

- [ ] **Step 2: Write the failing test**

Create `scripts/extraccion-estafeta/tests/OutputJson.Tests.ps1`:

```powershell
BeforeAll {
    . "$PSScriptRoot/../lib/OutputJson.ps1"
    $script:docA = Open-EEJsonDocument "$PSScriptRoot/fixtures/output-A.json"
    $script:rootA = $script:docA.RootElement
}
AfterAll { if ($script:docA) { $script:docA.Dispose() } }

Describe 'Open-EEJsonDocument' {
    It 'devuelve null si el fichero no existe' {
        Open-EEJsonDocument "$PSScriptRoot/fixtures/no-existe.json" | Should -BeNullOrEmpty
    }
}

Describe 'Get-EEPathValue' {
    It 'lee un string por path' {
        Get-EEPathValue $script:rootA 'Identificacion.Documento' | Should -Be 'docA.pdf'
    }
    It 'lee un numero como texto crudo' {
        Get-EEPathValue $script:rootA 'Resultado.ConfianzaGlobal' | Should -Be '0.95'
    }
    It 'lee anidado profundo' {
        Get-EEPathValue $script:rootA 'DetalleEjecucion.Extraccion.ConfianzaPorCampo.Titular' | Should -Be '0.98'
    }
    It 'devuelve vacio si el path no existe' {
        Get-EEPathValue $script:rootA 'Resultado.NoExiste' | Should -Be ''
    }
    It 'resuelve propiedades case-insensitive' {
        Get-EEPathValue $script:rootA 'identificacion.documento' | Should -Be 'docA.pdf'
    }
    It 'serializa objeto como JSON compacto' {
        Get-EEPathValue $script:rootA 'Integridad' | Should -Be '{"CRC32":"AAAA","SHA256":"hashA"}'
    }
}

Describe 'Get-EEObjectPropertyNames' {
    It 'devuelve los nombres de campo de DatosExtraidos en orden' {
        Get-EEObjectPropertyNames $script:rootA 'DatosExtraidos' | Should -Be @('Titular','Importe')
    }
    It 'devuelve vacio si el path no es objeto' {
        (Get-EEObjectPropertyNames $script:rootA 'Identificacion.Documento').Count | Should -Be 0
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests/OutputJson.Tests.ps1"`
Expected: FAIL (funciones no definidas).

- [ ] **Step 4: Write minimal implementation**

Create `scripts/extraccion-estafeta/lib/OutputJson.ps1`:

```powershell
Set-StrictMode -Version Latest

function Open-EEJsonDocument {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try {
        $text = [System.IO.File]::ReadAllText($Path)
        return [System.Text.Json.JsonDocument]::Parse($text)
    } catch {
        return $null
    }
}

function ConvertTo-EECellValue {
    param([System.Text.Json.JsonElement]$Value)
    $kind = $Value.ValueKind
    if ($kind -eq [System.Text.Json.JsonValueKind]::String) {
        $s = $Value.GetString(); if ($null -eq $s) { return '' } else { return $s }
    } elseif ($kind -eq [System.Text.Json.JsonValueKind]::Number) {
        return $Value.GetRawText()
    } elseif ($kind -eq [System.Text.Json.JsonValueKind]::True) {
        return 'true'
    } elseif ($kind -eq [System.Text.Json.JsonValueKind]::False) {
        return 'false'
    } elseif ($kind -eq [System.Text.Json.JsonValueKind]::Null -or $kind -eq [System.Text.Json.JsonValueKind]::Undefined) {
        return ''
    } elseif ($kind -eq [System.Text.Json.JsonValueKind]::Object -or $kind -eq [System.Text.Json.JsonValueKind]::Array) {
        return [System.Text.Json.JsonSerializer]::Serialize($Value)
    } else {
        return $Value.GetRawText()
    }
}

function Get-EETryGetProperty {
    param([System.Text.Json.JsonElement]$Element, [string]$Name)
    $out = [System.Text.Json.JsonElement]::new()
    if ($Element.TryGetProperty($Name, [ref]$out)) {
        return @{ Found = $true; Value = $out }
    }
    foreach ($p in $Element.EnumerateObject()) {
        if ([string]::Equals($p.Name, $Name, [System.StringComparison]::OrdinalIgnoreCase)) {
            return @{ Found = $true; Value = $p.Value }
        }
    }
    return @{ Found = $false; Value = [System.Text.Json.JsonElement]::new() }
}

function Get-EETryPath {
    param([System.Text.Json.JsonElement]$Root, [string]$Path)
    $value = $Root
    foreach ($segment in ($Path -split '\.')) {
        $seg = $segment.Trim()
        if ($seg -eq '') { continue }
        if ($value.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            return @{ Found = $false; Value = [System.Text.Json.JsonElement]::new() }
        }
        $r = Get-EETryGetProperty -Element $value -Name $seg
        if (-not $r.Found) { return @{ Found = $false; Value = [System.Text.Json.JsonElement]::new() } }
        $value = $r.Value
    }
    return @{ Found = $true; Value = $value }
}

function Get-EEPathValue {
    param([System.Text.Json.JsonElement]$Root, [string]$Path)
    $r = Get-EETryPath -Root $Root -Path $Path
    if ($r.Found) { return ConvertTo-EECellValue -Value $r.Value }
    return ''
}

function Get-EEObjectPropertyNames {
    param([System.Text.Json.JsonElement]$Root, [string]$Path)
    $r = Get-EETryPath -Root $Root -Path $Path
    if (-not $r.Found -or $r.Value.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { return @() }
    return @($r.Value.EnumerateObject() | ForEach-Object { $_.Name })
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests/OutputJson.Tests.ps1"`
Expected: PASS. Si el test de objeto compacto difiere en orden de claves, ajustar el fixture al orden que emite `System.Text.Json` (preserva orden de entrada).

- [ ] **Step 6: Commit**

```bash
git add scripts/extraccion-estafeta/lib/OutputJson.ps1 scripts/extraccion-estafeta/tests/OutputJson.Tests.ps1 scripts/extraccion-estafeta/tests/fixtures
git commit -m "feat(extraccion-estafeta): port del lector de output JSON (System.Text.Json)"
```

---

### Task 3: Construcción de tabla (headers, fields, filas)

**Files:**
- Modify: `scripts/extraccion-estafeta/lib/ExportTable.ps1`
- Modify: `scripts/extraccion-estafeta/tests/ExportTable.Tests.ps1`

**Interfaces:**
- Consumes: `Get-EEObjectPropertyNames`, `Get-EEPathValue`, `Open-EEJsonDocument` (de `OutputJson.ps1`).
- Produces:
  - `Get-EEPrefixHeaders() -> [string[]]`
  - `Get-EESuffixHeaders() -> [string[]]`
  - `Get-EEDatosExtraidosFields([System.Text.Json.JsonElement[]]$Roots) -> [string[]]`
  - `Get-EETableHeaders([string[]]$DatosExtraidosFields) -> [string[]]`
  - `Build-EEExportTable([object[]]$Documents) -> [pscustomobject]@{ Headers=[string[]]; Rows=[object[]] }` donde cada Document es `@{ HasOutput=[bool]; Root=[JsonElement] }` y cada fila es `[string[]]`.

- [ ] **Step 1: Write the failing test** (añadir a `ExportTable.Tests.ps1`)

Añadir al principio del fichero (dentro del `BeforeAll` existente, o crear uno nuevo):

```powershell
BeforeAll {
    . "$PSScriptRoot/../lib/OutputJson.ps1"
    . "$PSScriptRoot/../lib/ExportTable.ps1"
    $script:docA = Open-EEJsonDocument "$PSScriptRoot/fixtures/output-A.json"
    $script:docB = Open-EEJsonDocument "$PSScriptRoot/fixtures/output-B.json"
}
AfterAll {
    if ($script:docA) { $script:docA.Dispose() }
    if ($script:docB) { $script:docB.Dispose() }
}

Describe 'Get-EEDatosExtraidosFields' {
    It 'une campos de varios docs en orden de aparicion sin duplicar (case-insensitive)' {
        $roots = [System.Text.Json.JsonElement[]]@($script:docA.RootElement, $script:docB.RootElement)
        Get-EEDatosExtraidosFields $roots | Should -Be @('Titular','Importe','Referencia')
    }
}

Describe 'Get-EETableHeaders' {
    It 'intercala DatosExtraidos y ConfianzaPorCampo entre prefijo y sufijo' {
        $headers = Get-EETableHeaders @('Titular')
        $headers[0]  | Should -Be 'Identificacion.Documento'
        $headers | Should -Contain 'DatosExtraidos.Titular'
        $headers | Should -Contain 'DetalleEjecucion.Extraccion.ConfianzaPorCampo.Titular'
        $headers[-1] | Should -Be 'Resultado.MensajeReutilizacion'
    }
}

Describe 'Build-EEExportTable' {
    It 'genera fila por doc y fila vacia para doc sin output' {
        $docs = @(
            @{ HasOutput = $true;  Root = $script:docA.RootElement },
            @{ HasOutput = $false; Root = [System.Text.Json.JsonElement]::new() }
        )
        $table = Build-EEExportTable $docs
        $idx = [array]::IndexOf($table.Headers, 'DatosExtraidos.Titular')
        $table.Rows[0][$idx] | Should -Be 'Juan'
        $table.Rows[1][$idx] | Should -Be ''
        $table.Rows.Count | Should -Be 2
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests/ExportTable.Tests.ps1"`
Expected: FAIL (`Get-EEDatosExtraidosFields` etc. no definidos).

- [ ] **Step 3: Write minimal implementation** (añadir a `lib/ExportTable.ps1`)

```powershell
function Get-EEPrefixHeaders {
    return ,@(
        'Identificacion.Documento',
        'Identificacion.Guid',
        'Identificacion.Tipologia',
        'Identificacion.TipologiaFamilia',
        'Identificacion.TipologiaVersion',
        'Identificacion.FechaProceso',
        'Integridad.CRC32',
        'Integridad.SHA256',
        'Integridad.MD5',
        'Integridad.IdActivo',
        'Integridad.IdActivoEntrada',
        'Integridad.IdActivoCambiado'
    )
}

function Get-EESuffixHeaders {
    return ,@(
        'DetalleEjecucion.Extraccion.Modelo',
        'DetalleEjecucion.Extraccion.CamposConDuda',
        'DetalleEjecucion.AssetResolver.ActivosAAII',
        'DetalleEjecucion.AssetResolver.ActivosAACC',
        'DetalleEjecucion.AssetResolver.Mensaje',
        'DetalleEjecucion.Prompt',
        'Resultado.Estado',
        'Resultado.MensajeError',
        'Resultado.ConfianzaGlobal',
        'Resultado.EstadoCalidad',
        'Resultado.ConfianzaClasificacion',
        'Resultado.ConfianzaExtraccion',
        'Resultado.ConfianzaValidacion',
        'Resultado.ReutilizadaPorDuplicado',
        'Resultado.MensajeReutilizacion'
    )
}

function Get-EEDatosExtraidosFields {
    param([System.Text.Json.JsonElement[]]$Roots)
    $fields = New-Object 'System.Collections.Generic.List[string]'
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($root in $Roots) {
        foreach ($name in (Get-EEObjectPropertyNames -Root $root -Path 'DatosExtraidos')) {
            if ($seen.Add($name)) { $fields.Add($name) }
        }
    }
    return $fields.ToArray()
}

function Get-EETableHeaders {
    param([string[]]$DatosExtraidosFields)
    $headers = New-Object 'System.Collections.Generic.List[string]'
    $headers.AddRange([string[]](Get-EEPrefixHeaders))
    foreach ($f in $DatosExtraidosFields) {
        $headers.Add("DatosExtraidos.$f")
        $headers.Add("DetalleEjecucion.Extraccion.ConfianzaPorCampo.$f")
    }
    $headers.AddRange([string[]](Get-EESuffixHeaders))
    return $headers.ToArray()
}

function Build-EEExportTable {
    param([object[]]$Documents)
    $roots = New-Object 'System.Collections.Generic.List[System.Text.Json.JsonElement]'
    foreach ($doc in $Documents) { if ($doc.HasOutput) { $roots.Add($doc.Root) } }
    $fields = Get-EEDatosExtraidosFields ([System.Text.Json.JsonElement[]]$roots.ToArray())
    $headers = Get-EETableHeaders $fields
    $rows = New-Object 'System.Collections.Generic.List[object]'
    foreach ($doc in $Documents) {
        $row = New-Object 'System.Collections.Generic.List[string]'
        foreach ($h in $headers) {
            if ($doc.HasOutput) { $row.Add((Get-EEPathValue -Root $doc.Root -Path $h)) }
            else { $row.Add('') }
        }
        $rows.Add(,($row.ToArray()))
    }
    return [pscustomobject]@{ Headers = $headers; Rows = $rows.ToArray() }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests/ExportTable.Tests.ps1"`
Expected: PASS (todos, incluidos los de Task 1).

- [ ] **Step 5: Commit**

```bash
git add scripts/extraccion-estafeta/lib/ExportTable.ps1 scripts/extraccion-estafeta/tests/ExportTable.Tests.ps1
git commit -m "feat(extraccion-estafeta): construccion de tabla (headers dinamicos y filas)"
```

---

### Task 4: Writer de Excel (xlsx multi-hoja)

**Files:**
- Create: `scripts/extraccion-estafeta/lib/ExcelWriter.ps1`
- Test: `scripts/extraccion-estafeta/tests/ExcelWriter.Tests.ps1`

**Interfaces:**
- Produces: `Write-EEWorkbook([string]$Path, [object[]]$Sheets)` donde cada Sheet es `@{ Name=[string]; Headers=[string[]]; Rows=[object[]] }` (cada fila `[string[]]`). Escribe un xlsx OOXML con celdas `inlineStr`.

- [ ] **Step 1: Write the failing test**

Create `scripts/extraccion-estafeta/tests/ExcelWriter.Tests.ps1`:

```powershell
BeforeAll {
    . "$PSScriptRoot/../lib/ExcelWriter.ps1"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}

Describe 'Write-EEWorkbook' {
    It 'crea un xlsx valido con las hojas, cabeceras y valores esperados' {
        $out = Join-Path ([System.IO.Path]::GetTempPath()) ("ee-{0}.xlsx" -f ([guid]::NewGuid()))
        $sheets = @(
            @{ Name = 'Resumen'; Headers = @('Col1','Col2'); Rows = @( ,@('a','b'), ,@('c','d') ) }
        )
        Write-EEWorkbook -Path $out -Sheets $sheets
        Test-Path $out | Should -BeTrue

        $zip = [System.IO.Compression.ZipFile]::OpenRead($out)
        try {
            ($zip.Entries.FullName -contains 'xl/worksheets/sheet1.xml') | Should -BeTrue
            $entry = $zip.GetEntry('xl/worksheets/sheet1.xml')
            $reader = New-Object System.IO.StreamReader($entry.Open())
            $xml = $reader.ReadToEnd(); $reader.Dispose()
            $xml | Should -Match 'Col1'
            $xml | Should -Match '>a<'
            $xml | Should -Match '>d<'
        } finally { $zip.Dispose() }

        Remove-Item $out -Force
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests/ExcelWriter.Tests.ps1"`
Expected: FAIL (`Write-EEWorkbook` no definido).

- [ ] **Step 3: Write minimal implementation**

Create `scripts/extraccion-estafeta/lib/ExcelWriter.ps1` (port de `BatchExcelExportService`, generalizado a N hojas):

```powershell
Set-StrictMode -Version Latest

function Get-EEColumnName {
    param([int]$ColumnNumber)
    $name = ''
    while ($ColumnNumber -gt 0) {
        $ColumnNumber--
        $name = [char]([int][char]'A' + ($ColumnNumber % 26)) + $name
        $ColumnNumber = [math]::Floor($ColumnNumber / 26)
    }
    return $name
}

function Write-EETextEntry {
    param([System.IO.Compression.ZipArchive]$Archive, [string]$Name, [string]$Content)
    $entry = $Archive.CreateEntry($Name, [System.IO.Compression.CompressionLevel]::Optimal)
    $writer = New-Object System.IO.StreamWriter($entry.Open(), (New-Object System.Text.UTF8Encoding($false)))
    try { $writer.Write($Content) } finally { $writer.Dispose() }
}

function Write-EERow {
    param([System.Xml.XmlWriter]$Writer, [int]$RowNumber, [string[]]$Values)
    $Writer.WriteStartElement('row')
    $Writer.WriteAttributeString('r', $RowNumber.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    for ($i = 0; $i -lt $Values.Count; $i++) {
        $Writer.WriteStartElement('c')
        $Writer.WriteAttributeString('r', ('{0}{1}' -f (Get-EEColumnName ($i + 1)), $RowNumber))
        $Writer.WriteAttributeString('t', 'inlineStr')
        $Writer.WriteStartElement('is')
        $Writer.WriteStartElement('t')
        $Writer.WriteAttributeString('xml', 'space', $null, 'preserve')
        $v = $Values[$i]; if ($null -eq $v) { $v = '' }
        $Writer.WriteString($v)
        $Writer.WriteEndElement()
        $Writer.WriteEndElement()
        $Writer.WriteEndElement()
    }
    $Writer.WriteEndElement()
}

function Write-EEWorksheet {
    param([System.IO.Compression.ZipArchive]$Archive, [int]$Index, [object]$Sheet)
    $entry = $Archive.CreateEntry(('xl/worksheets/sheet{0}.xml' -f $Index), [System.IO.Compression.CompressionLevel]::Optimal)
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $settings.Indent = $false
    $writer = [System.Xml.XmlWriter]::Create($entry.Open(), $settings)
    try {
        $writer.WriteStartDocument()
        $writer.WriteStartElement('worksheet', 'http://schemas.openxmlformats.org/spreadsheetml/2006/main')
        $writer.WriteStartElement('sheetData')
        $rowNumber = 1
        Write-EERow -Writer $writer -RowNumber $rowNumber -Values ([string[]]$Sheet.Headers); $rowNumber++
        foreach ($row in $Sheet.Rows) {
            Write-EERow -Writer $writer -RowNumber $rowNumber -Values ([string[]]$row); $rowNumber++
        }
        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndDocument()
    } finally { $writer.Dispose() }
}

function Write-EEWorkbook {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][object[]]$Sheets)

    $contentTypes = @'
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
'@
    for ($i = 1; $i -le $Sheets.Count; $i++) {
        $contentTypes += "`n  <Override PartName=`"/xl/worksheets/sheet$i.xml`" ContentType=`"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml`"/>"
    }
    $contentTypes += "`n  <Override PartName=`"/xl/workbook.xml`" ContentType=`"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml`"/>`n</Types>"

    $rootRels = @'
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
'@

    $sheetsXml = ''
    $wbRels = ''
    for ($i = 1; $i -le $Sheets.Count; $i++) {
        $safeName = [System.Security.SecurityElement]::Escape($Sheets[$i-1].Name)
        $sheetsXml += "<sheet name=`"$safeName`" sheetId=`"$i`" r:id=`"rId$i`"/>"
        $wbRels += "<Relationship Id=`"rId$i`" Type=`"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet`" Target=`"worksheets/sheet$i.xml`"/>"
    }
    $workbookXml = "<workbook xmlns=`"http://schemas.openxmlformats.org/spreadsheetml/2006/main`" xmlns:r=`"http://schemas.openxmlformats.org/officeDocument/2006/relationships`"><sheets>$sheetsXml</sheets></workbook>"
    $workbookRels = "<Relationships xmlns=`"http://schemas.openxmlformats.org/package/2006/relationships`">$wbRels</Relationships>"

    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    $archive = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Write-EETextEntry -Archive $archive -Name '[Content_Types].xml' -Content $contentTypes
        Write-EETextEntry -Archive $archive -Name '_rels/.rels' -Content $rootRels
        Write-EETextEntry -Archive $archive -Name 'xl/workbook.xml' -Content $workbookXml
        Write-EETextEntry -Archive $archive -Name 'xl/_rels/workbook.xml.rels' -Content $workbookRels
        for ($i = 1; $i -le $Sheets.Count; $i++) {
            Write-EEWorksheet -Archive $archive -Index $i -Sheet $Sheets[$i-1]
        }
    } finally {
        $archive.Dispose()
        $stream.Dispose()
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests/ExcelWriter.Tests.ps1"`
Expected: PASS. (Verifica también manualmente abriendo el xlsx en Excel una vez, para confirmar que no reporta reparación.)

- [ ] **Step 5: Commit**

```bash
git add scripts/extraccion-estafeta/lib/ExcelWriter.ps1 scripts/extraccion-estafeta/tests/ExcelWriter.Tests.ps1
git commit -m "feat(extraccion-estafeta): writer xlsx OOXML multi-hoja (inlineStr)"
```

---

### Task 5: Builders puros del cliente de ingest

**Files:**
- Create: `scripts/extraccion-estafeta/lib/IngestClient.ps1`
- Test: `scripts/extraccion-estafeta/tests/IngestClient.Tests.ps1`

**Interfaces:**
- Produces:
  - `New-EEIngestMetadata([string]$FileName,[string]$ExpectedType,[string]$CorrelationId,[bool]$SkipGdcUpload) -> [ordered]` (estructura serializable)
  - `Get-EEIngestEndpoints([string]$BackendUrl,[string]$FunctionKey) -> [string[]]`
  - `Get-EEOutputFromStatus([string]$StatusJson) -> @{ RuntimeStatus=[string]; OutputJson=[string] | $null }`

- [ ] **Step 1: Write the failing test**

Create `scripts/extraccion-estafeta/tests/IngestClient.Tests.ps1`:

```powershell
BeforeAll {
    . "$PSScriptRoot/../lib/IngestClient.ps1"
}

Describe 'New-EEIngestMetadata' {
    It 'fija forceReprocess/skipDuplicateCheck y no incluye assetResolver' {
        $m = New-EEIngestMetadata 'd.pdf' 'cera.44.vado' 'cid-1' $true
        $m.instrucciones.expectedType       | Should -Be 'cera.44.vado'
        $m.instrucciones.forceReprocess     | Should -BeTrue
        $m.instrucciones.skipDuplicateCheck | Should -BeTrue
        $m.instrucciones.classificationOnly | Should -BeFalse
        $m.instrucciones.skipGDCUpload      | Should -BeTrue
        $m.instrucciones.Contains('assetResolver') | Should -BeFalse
        $m.documento.name                   | Should -Be 'd.pdf'
        $m.trazabilidad.submittedBy         | Should -Be 'ExtraccionEstafeta'
    }
}

Describe 'Get-EEIngestEndpoints' {
    It 'genera /api/ingest y fallback con code en query' {
        $eps = Get-EEIngestEndpoints 'https://f.azurewebsites.net/' 'KEY'
        $eps[0] | Should -Be 'https://f.azurewebsites.net/api/ingest?code=KEY'
        $eps[1] | Should -Be 'https://f.azurewebsites.net/api/IngestDocument?code=KEY'
    }
}

Describe 'Get-EEOutputFromStatus' {
    It 'extrae runtimeStatus y output serializado' {
        $json = '{"runtimeStatus":"Completed","output":{"Resultado":{"Estado":"OK"}}}'
        $r = Get-EEOutputFromStatus $json
        $r.RuntimeStatus | Should -Be 'Completed'
        $r.OutputJson    | Should -Match '"Estado":"OK"'
    }
    It 'devuelve OutputJson null si no hay output' {
        $r = Get-EEOutputFromStatus '{"runtimeStatus":"Running"}'
        $r.RuntimeStatus | Should -Be 'Running'
        $r.OutputJson    | Should -BeNullOrEmpty
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests/IngestClient.Tests.ps1"`
Expected: FAIL (funciones no definidas).

- [ ] **Step 3: Write minimal implementation** (builders puros; el HTTP se añade en Task 6)

Create `scripts/extraccion-estafeta/lib/IngestClient.ps1`:

```powershell
Set-StrictMode -Version Latest

function New-EEIngestMetadata {
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string]$ExpectedType,
        [Parameter(Mandatory)][string]$CorrelationId,
        [bool]$SkipGdcUpload = $true
    )
    return [ordered]@{
        instrucciones = [ordered]@{
            expectedType       = $ExpectedType
            classificationOnly = $false
            skipDuplicateCheck = $true
            forceReprocess     = $true
            skipGDCUpload      = $SkipGdcUpload
            classification     = [ordered]@{ provider = 'auto'; model = 'auto' }
            extraction         = [ordered]@{ provider = 'auto'; model = 'auto' }
        }
        documento = [ordered]@{
            name    = $FileName
            content = [ordered]@{ base64 = '' }
        }
        trazabilidad = [ordered]@{
            correlationId = $CorrelationId
            submittedBy   = 'ExtraccionEstafeta'
        }
    }
}

function Get-EEIngestEndpoints {
    param([Parameter(Mandatory)][string]$BackendUrl, [string]$FunctionKey)
    $base = $BackendUrl.Trim().TrimEnd('/')
    $paths = @('/api/ingest', '/api/IngestDocument')
    return @($paths | ForEach-Object {
        $ep = "$base$_"
        if (-not [string]::IsNullOrWhiteSpace($FunctionKey)) {
            $sep = if ($ep.Contains('?')) { '&' } else { '?' }
            $ep = "$ep${sep}code=$([uri]::EscapeDataString($FunctionKey))"
        }
        $ep
    })
}

function Get-EEOutputFromStatus {
    param([Parameter(Mandatory)][string]$StatusJson)
    $doc = [System.Text.Json.JsonDocument]::Parse($StatusJson)
    try {
        $root = $doc.RootElement
        $runtime = ''
        $rt = [System.Text.Json.JsonElement]::new()
        if ($root.TryGetProperty('runtimeStatus', [ref]$rt)) { $runtime = $rt.GetString() }
        $outputJson = $null
        $outEl = [System.Text.Json.JsonElement]::new()
        if ($root.TryGetProperty('output', [ref]$outEl) -and $outEl.ValueKind -ne [System.Text.Json.JsonValueKind]::Null) {
            $outputJson = $outEl.GetRawText()
        }
        return @{ RuntimeStatus = $runtime; OutputJson = $outputJson }
    } finally {
        $doc.Dispose()
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests/IngestClient.Tests.ps1"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add scripts/extraccion-estafeta/lib/IngestClient.ps1 scripts/extraccion-estafeta/tests/IngestClient.Tests.ps1
git commit -m "feat(extraccion-estafeta): builders puros de ingest (metadata, endpoints, parseo status)"
```

---

### Task 6: Cliente HTTP (ingest + polling) con mock local

**Files:**
- Modify: `scripts/extraccion-estafeta/lib/IngestClient.ps1`
- Modify: `scripts/extraccion-estafeta/tests/IngestClient.Tests.ps1`

**Interfaces:**
- Consumes: `New-EEIngestMetadata`, `Get-EEIngestEndpoints`, `Get-EEOutputFromStatus`.
- Produces:
  - `Invoke-EEIngest([string]$BackendUrl,[string]$FunctionKey,[string]$FilePath,[string]$ExpectedType,[string]$CorrelationId,[bool]$SkipGdcUpload) -> @{ InstanceId=[string]; StatusQueryUri=[string] }`
  - `Wait-EEDurableStatus([string]$StatusQueryUri,[string]$FunctionKey,[int]$TimeoutSeconds,[int]$PollIntervalSeconds) -> @{ RuntimeStatus=[string]; OutputJson=[string] | $null }`

- [ ] **Step 1: Write the failing test** (mock con `System.Net.HttpListener`)

Añadir a `IngestClient.Tests.ps1`:

```powershell
Describe 'Invoke-EEIngest + Wait-EEDurableStatus (contra mock local)' {
    It 'sube el fichero, obtiene statusQueryUri y hace polling hasta Completed' {
        $prefix = 'http://localhost:8791/'
        $listener = New-Object System.Net.HttpListener
        $listener.Prefixes.Add($prefix)
        $listener.Start()

        # Servidor mock en runspace aparte: /api/ingest -> statusQueryUri; /status -> Completed
        $server = [powershell]::Create()
        [void]$server.AddScript({
            param($listener, $prefix)
            $calls = 0
            while ($listener.IsListening) {
                $ctx = $listener.GetContext()
                $path = $ctx.Request.Url.AbsolutePath
                $resp = $ctx.Response
                if ($path -eq '/api/ingest' -or $path -eq '/api/IngestDocument') {
                    $body = "{""instanceId"":""inst-1"",""statusQueryUri"":""${prefix}status""}"
                } elseif ($path -eq '/status') {
                    $calls++
                    if ($calls -ge 2) { $body = '{"runtimeStatus":"Completed","output":{"Resultado":{"Estado":"OK"}}}' }
                    else { $body = '{"runtimeStatus":"Running"}' }
                } else { $body = '{}'; $resp.StatusCode = 404 }
                $buf = [System.Text.Encoding]::UTF8.GetBytes($body)
                $resp.ContentType = 'application/json'
                $resp.OutputStream.Write($buf, 0, $buf.Length)
                $resp.OutputStream.Close()
            }
        }).AddArgument($listener).AddArgument($prefix)
        $async = $server.BeginInvoke()

        try {
            $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("ee-{0}.pdf" -f ([guid]::NewGuid()))
            [System.IO.File]::WriteAllBytes($tmp, [byte[]](1,2,3,4))
            $ingest = Invoke-EEIngest -BackendUrl $prefix.TrimEnd('/') -FunctionKey '' -FilePath $tmp -ExpectedType 'cera.44.vado' -CorrelationId 'cid' -SkipGdcUpload $true
            $ingest.StatusQueryUri | Should -Match 'status'
            $status = Wait-EEDurableStatus -StatusQueryUri $ingest.StatusQueryUri -FunctionKey '' -TimeoutSeconds 20 -PollIntervalSeconds 1
            $status.RuntimeStatus | Should -Be 'Completed'
            $status.OutputJson    | Should -Match '"Estado":"OK"'
            Remove-Item $tmp -Force
        } finally {
            $listener.Stop(); $listener.Close()
            $server.Stop() | Out-Null
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests/IngestClient.Tests.ps1"`
Expected: FAIL (`Invoke-EEIngest` no definido).

- [ ] **Step 3: Write minimal implementation** (añadir a `lib/IngestClient.ps1`)

```powershell
function New-EEHttpClient {
    $handler = New-Object System.Net.Http.HttpClientHandler
    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromMinutes(5)
    return $client
}

function Invoke-EEIngest {
    param(
        [Parameter(Mandatory)][string]$BackendUrl,
        [string]$FunctionKey,
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string]$ExpectedType,
        [Parameter(Mandatory)][string]$CorrelationId,
        [bool]$SkipGdcUpload = $true
    )
    $fileName = [System.IO.Path]::GetFileName($FilePath)
    $metadata = New-EEIngestMetadata -FileName $fileName -ExpectedType $ExpectedType -CorrelationId $CorrelationId -SkipGdcUpload $SkipGdcUpload
    $metadataJson = ($metadata | ConvertTo-Json -Depth 10 -Compress)
    $fileBytes = [System.IO.File]::ReadAllBytes($FilePath)
    $endpoints = Get-EEIngestEndpoints -BackendUrl $BackendUrl -FunctionKey $FunctionKey
    $client = New-EEHttpClient
    try {
        $lastStatus = 404
        foreach ($endpoint in $endpoints) {
            $multipart = New-Object System.Net.Http.MultipartFormDataContent
            $metaContent = New-Object System.Net.Http.StringContent($metadataJson, [System.Text.Encoding]::UTF8, 'application/json')
            $multipart.Add($metaContent, 'metadata')
            $fileContent = New-Object System.Net.Http.ByteArrayContent(,$fileBytes)
            $fileContent.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue('application/pdf')
            $multipart.Add($fileContent, 'file', $fileName)

            $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, $endpoint)
            $req.Content = $multipart
            if (-not [string]::IsNullOrWhiteSpace($FunctionKey)) {
                [void]$req.Headers.TryAddWithoutValidation('x-functions-key', $FunctionKey)
            }
            $resp = $client.SendAsync($req).GetAwaiter().GetResult()
            $lastStatus = [int]$resp.StatusCode
            if ($lastStatus -eq 404) { continue }
            if ($lastStatus -eq 401) { throw "401 Unauthorized: revisa la function key de PROD." }
            $payload = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if (-not $resp.IsSuccessStatusCode) { throw "Error ingest: $lastStatus. $payload" }
            $obj = $payload | ConvertFrom-Json
            if ([string]::IsNullOrWhiteSpace($obj.statusQueryUri)) { throw "Respuesta de ingest sin statusQueryUri." }
            return @{ InstanceId = [string]$obj.instanceId; StatusQueryUri = [string]$obj.statusQueryUri }
        }
        throw "No se encontro endpoint de ingest. Ultimo estado HTTP: $lastStatus."
    } finally {
        $client.Dispose()
    }
}

function Wait-EEDurableStatus {
    param(
        [Parameter(Mandatory)][string]$StatusQueryUri,
        [string]$FunctionKey,
        [int]$TimeoutSeconds = 600,
        [int]$PollIntervalSeconds = 5
    )
    $client = New-EEHttpClient
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    try {
        while ((Get-Date) -lt $deadline) {
            $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, $StatusQueryUri)
            if (-not [string]::IsNullOrWhiteSpace($FunctionKey)) {
                [void]$req.Headers.TryAddWithoutValidation('x-functions-key', $FunctionKey)
            }
            $resp = $client.SendAsync($req).GetAwaiter().GetResult()
            $payload = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if ($resp.IsSuccessStatusCode) {
                $parsed = Get-EEOutputFromStatus $payload
                if ($parsed.RuntimeStatus -in @('Completed','Failed','Terminated')) { return $parsed }
            } elseif ([int]$resp.StatusCode -eq 401) {
                throw "401 Unauthorized consultando status: revisa la function key."
            }
            Start-Sleep -Seconds $PollIntervalSeconds
        }
        return @{ RuntimeStatus = 'Timeout'; OutputJson = $null }
    } finally {
        $client.Dispose()
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests/IngestClient.Tests.ps1"`
Expected: PASS. (Si el puerto 8791 está ocupado, cambiarlo en el test.)

- [ ] **Step 5: Commit**

```bash
git add scripts/extraccion-estafeta/lib/IngestClient.ps1 scripts/extraccion-estafeta/tests/IngestClient.Tests.ps1
git commit -m "feat(extraccion-estafeta): cliente HTTP de ingest y polling durable"
```

---

### Task 7: Salidas auxiliares (KPIs, log, run.json, resumen)

**Files:**
- Create: `scripts/extraccion-estafeta/lib/Outputs.ps1`
- Test: `scripts/extraccion-estafeta/tests/Outputs.Tests.ps1`

**Interfaces:**
- Consumes: `Write-EEWorkbook`, `Get-EEPathValue`, `Build-EEExportTable`.
- Produces:
  - `Get-EEKpiRow([string]$Tipologia,[object[]]$Results) -> [pscustomobject]` con propiedades `Tipologia,Total,Completados,Error,Revision,ConfianzaMedia`. Cada Result es `@{ Estado=[string]; EstadoCalidad=[string]; ConfianzaGlobal=[double] | $null }`.
  - `Write-EELogCsv([string]$Path,[object[]]$LogRows)` — LogRow: `@{ Tipologia; Fichero; Estado; InstanceId; CorrelationId; Inicio; Fin; DuracionSeg; Mensaje }`.
  - `Write-EERunJson([string]$Path,[hashtable]$Meta)`.
  - `Write-EEResumenWorkbook([string]$Path,[object[]]$KpiRows,[object[]]$TipologiaTables)` — TipologiaTable: `@{ Name=[string]; Headers=[string[]]; Rows=[object[]] }`. Escribe hoja `KPIs` + una hoja por tipología.

- [ ] **Step 1: Write the failing test**

Create `scripts/extraccion-estafeta/tests/Outputs.Tests.ps1`:

```powershell
BeforeAll {
    . "$PSScriptRoot/../lib/ExcelWriter.ps1"
    . "$PSScriptRoot/../lib/Outputs.ps1"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}

Describe 'Get-EEKpiRow' {
    It 'agrega totales y confianza media' {
        $results = @(
            @{ Estado='Completado'; EstadoCalidad='Aprobado'; ConfianzaGlobal=0.9 },
            @{ Estado='Completado'; EstadoCalidad='Revision'; ConfianzaGlobal=0.6 },
            @{ Estado='Error';      EstadoCalidad='';         ConfianzaGlobal=$null }
        )
        $k = Get-EEKpiRow 'cera.44.vado' $results
        $k.Total       | Should -Be 3
        $k.Completados | Should -Be 2
        $k.Error       | Should -Be 1
        $k.Revision    | Should -Be 1
        [math]::Round($k.ConfianzaMedia, 2) | Should -Be 0.75
    }
}

Describe 'Write-EELogCsv' {
    It 'escribe cabeceras y filas' {
        $out = Join-Path ([System.IO.Path]::GetTempPath()) ("ee-log-{0}.csv" -f ([guid]::NewGuid()))
        Write-EELogCsv -Path $out -LogRows @(
            @{ Tipologia='t'; Fichero='f.pdf'; Estado='Completado'; InstanceId='i'; CorrelationId='c'; Inicio='x'; Fin='y'; DuracionSeg=3; Mensaje='' }
        )
        (Get-Content $out -Raw) | Should -Match 'Tipologia'
        (Get-Content $out -Raw) | Should -Match 'f.pdf'
        Remove-Item $out -Force
    }
}

Describe 'Write-EEResumenWorkbook' {
    It 'crea xlsx con hoja KPIs y una por tipologia' {
        $out = Join-Path ([System.IO.Path]::GetTempPath()) ("ee-res-{0}.xlsx" -f ([guid]::NewGuid()))
        $kpis = @([pscustomobject]@{ Tipologia='t1'; Total=1; Completados=1; Error=0; Revision=0; ConfianzaMedia=0.9 })
        $tables = @(@{ Name='t1'; Headers=@('A'); Rows=@( ,@('v') ) })
        Write-EEResumenWorkbook -Path $out -KpiRows $kpis -TipologiaTables $tables
        $zip = [System.IO.Compression.ZipFile]::OpenRead($out)
        try { ($zip.Entries.FullName -contains 'xl/worksheets/sheet2.xml') | Should -BeTrue }
        finally { $zip.Dispose() }
        Remove-Item $out -Force
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests/Outputs.Tests.ps1"`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

Create `scripts/extraccion-estafeta/lib/Outputs.ps1`:

```powershell
Set-StrictMode -Version Latest

function Get-EEKpiRow {
    param([Parameter(Mandatory)][string]$Tipologia, [object[]]$Results)
    $total = $Results.Count
    $completados = @($Results | Where-Object { $_.Estado -eq 'Completado' }).Count
    $errores = @($Results | Where-Object { $_.Estado -eq 'Error' -or $_.Estado -eq 'Timeout' }).Count
    $revision = @($Results | Where-Object { $_.EstadoCalidad -eq 'Revision' }).Count
    $confianzas = @($Results | Where-Object { $null -ne $_.ConfianzaGlobal } | ForEach-Object { [double]$_.ConfianzaGlobal })
    $media = if ($confianzas.Count -gt 0) { ($confianzas | Measure-Object -Average).Average } else { $null }
    return [pscustomobject]@{
        Tipologia = $Tipologia; Total = $total; Completados = $completados
        Error = $errores; Revision = $revision; ConfianzaMedia = $media
    }
}

function Write-EELogCsv {
    param([Parameter(Mandatory)][string]$Path, [object[]]$LogRows)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $LogRows | ForEach-Object { [pscustomobject]$_ } |
        Select-Object Tipologia, Fichero, Estado, InstanceId, CorrelationId, Inicio, Fin, DuracionSeg, Mensaje |
        Export-Csv -Path $Path -NoTypeInformation -Encoding UTF8
}

function Write-EERunJson {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][hashtable]$Meta)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    ($Meta | ConvertTo-Json -Depth 8) | Set-Content -Path $Path -Encoding UTF8
}

function Write-EEResumenWorkbook {
    param(
        [Parameter(Mandatory)][string]$Path,
        [object[]]$KpiRows,
        [object[]]$TipologiaTables
    )
    $kpiHeaders = @('Tipologia','Total','Completados','Error','Revision','ConfianzaMedia')
    $kpiRowsArr = New-Object 'System.Collections.Generic.List[object]'
    foreach ($k in $KpiRows) {
        $cm = if ($null -ne $k.ConfianzaMedia) { [string]([math]::Round([double]$k.ConfianzaMedia, 4)) } else { '' }
        $kpiRowsArr.Add(,([string[]]@([string]$k.Tipologia,[string]$k.Total,[string]$k.Completados,[string]$k.Error,[string]$k.Revision,$cm)))
    }
    $sheets = New-Object 'System.Collections.Generic.List[object]'
    $sheets.Add(@{ Name = 'KPIs'; Headers = $kpiHeaders; Rows = $kpiRowsArr.ToArray() })
    foreach ($t in $TipologiaTables) {
        $name = $t.Name
        if ($name.Length -gt 31) { $name = $name.Substring(0, 31) }
        $sheets.Add(@{ Name = $name; Headers = $t.Headers; Rows = $t.Rows })
    }
    Write-EEWorkbook -Path $Path -Sheets $sheets.ToArray()
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests/Outputs.Tests.ps1"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add scripts/extraccion-estafeta/lib/Outputs.ps1 scripts/extraccion-estafeta/tests/Outputs.Tests.ps1
git commit -m "feat(extraccion-estafeta): salidas KPIs, log CSV, run.json y resumen xlsx"
```

---

### Task 8: Entry-point — params, config y descubrimiento (con -DryRun)

**Files:**
- Create: `scripts/extraccion-estafeta/Invoke-ExtraccionEstafeta.ps1`
- Test: `scripts/extraccion-estafeta/tests/Discovery.Tests.ps1`

**Interfaces:**
- Produces (funciones internas del script, dot-sourceables para test):
  - `Resolve-EEConfig([hashtable]$Params) -> [hashtable]` (mezcla `-ConfigPath` JSON + parámetros + env `DOCUMENTIA_FUNCTION_KEY`; parámetros pisan config).
  - `Get-EEWorkItems([string]$RootPath) -> [object[]]` — cada item `@{ Tipologia=[string]; FolderName=[string]; FolderPath=[string]; Files=[string[]] }` (PDFs, primer nivel).

- [ ] **Step 1: Write the failing test**

Create `scripts/extraccion-estafeta/tests/Discovery.Tests.ps1`:

```powershell
BeforeAll {
    . "$PSScriptRoot/../lib/ExportTable.ps1"
    # Dot-source solo las funciones del entry-point sin ejecutar el cuerpo:
    . "$PSScriptRoot/../Invoke-ExtraccionEstafeta.ps1" -LibOnly
}

Describe 'Get-EEWorkItems' {
    It 'descubre subcarpetas, mapea codigo y lista PDFs de primer nivel' {
        $root = Join-Path ([System.IO.Path]::GetTempPath()) ("ee-root-{0}" -f ([guid]::NewGuid()))
        New-Item -ItemType Directory -Path (Join-Path $root 'cera.44_vado') -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $root 'cera.44_vado/sub') -Force | Out-Null
        Set-Content -Path (Join-Path $root 'cera.44_vado/a.pdf') -Value 'x'
        Set-Content -Path (Join-Path $root 'cera.44_vado/b.txt') -Value 'x'
        Set-Content -Path (Join-Path $root 'cera.44_vado/sub/c.pdf') -Value 'x'

        $items = Get-EEWorkItems -RootPath $root
        $items.Count | Should -Be 1
        $items[0].Tipologia | Should -Be 'cera.44.vado'
        $items[0].Files.Count | Should -Be 1
        [System.IO.Path]::GetFileName($items[0].Files[0]) | Should -Be 'a.pdf'

        Remove-Item $root -Recurse -Force
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests/Discovery.Tests.ps1"`
Expected: FAIL (script / `-LibOnly` / `Get-EEWorkItems` no existen).

- [ ] **Step 3: Write minimal implementation** (cabecera del entry-point + funciones + guard `-LibOnly`)

Create `scripts/extraccion-estafeta/Invoke-ExtraccionEstafeta.ps1`:

```powershell
#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RootPath,
    [string]$BackendUrl,
    [string]$FunctionKey,
    [string]$ConfigPath,
    [int]$MaxParallel = 2,
    [int]$MaxRetries = 1,
    [int]$TimeoutSeconds = 600,
    [int]$PollIntervalSeconds = 5,
    [bool]$SkipGdcUpload = $true,
    [switch]$DryRun,
    [switch]$LibOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$LibDir = Join-Path $PSScriptRoot 'lib'
. (Join-Path $LibDir 'OutputJson.ps1')
. (Join-Path $LibDir 'ExportTable.ps1')
. (Join-Path $LibDir 'ExcelWriter.ps1')
. (Join-Path $LibDir 'IngestClient.ps1')
. (Join-Path $LibDir 'Outputs.ps1')

function Resolve-EEConfig {
    param([hashtable]$Params)
    $cfg = @{
        RootPath = $null; BackendUrl = $null; FunctionKey = $null
        MaxParallel = 2; MaxRetries = 1; TimeoutSeconds = 600; PollIntervalSeconds = 5; SkipGdcUpload = $true
    }
    if ($Params.ContainsKey('ConfigPath') -and $Params.ConfigPath -and (Test-Path $Params.ConfigPath)) {
        $fileCfg = Get-Content $Params.ConfigPath -Raw | ConvertFrom-Json
        foreach ($p in $fileCfg.PSObject.Properties) { $cfg[$p.Name] = $p.Value }
    }
    foreach ($key in @('RootPath','BackendUrl','FunctionKey','MaxParallel','MaxRetries','TimeoutSeconds','PollIntervalSeconds','SkipGdcUpload')) {
        if ($Params.ContainsKey($key) -and $null -ne $Params[$key] -and "$($Params[$key])" -ne '') { $cfg[$key] = $Params[$key] }
    }
    if ([string]::IsNullOrWhiteSpace([string]$cfg.FunctionKey) -and $env:DOCUMENTIA_FUNCTION_KEY) {
        $cfg.FunctionKey = $env:DOCUMENTIA_FUNCTION_KEY
    }
    return $cfg
}

function Get-EEWorkItems {
    param([Parameter(Mandatory)][string]$RootPath)
    if (-not (Test-Path -LiteralPath $RootPath -PathType Container)) {
        throw "RootPath no existe o no es carpeta: $RootPath"
    }
    $items = New-Object 'System.Collections.Generic.List[object]'
    foreach ($dir in (Get-ChildItem -LiteralPath $RootPath -Directory | Sort-Object Name)) {
        if ($dir.Name -eq 'Results') { continue }
        $pdfs = @(Get-ChildItem -LiteralPath $dir.FullName -File -Filter '*.pdf' | Sort-Object Name | ForEach-Object { $_.FullName })
        $items.Add([pscustomobject]@{
            Tipologia  = Get-EETipologiaCode $dir.Name
            FolderName = $dir.Name
            FolderPath = $dir.FullName
            Files      = $pdfs
        })
    }
    return $items.ToArray()
}

if ($LibOnly) { return }

# --- cuerpo principal (Task 9) ---
Invoke-EEMain -Params $PSBoundParameters
```

Nota: `Invoke-EEMain` se implementa en Task 9. Para que Task 8 pase (dot-source con `-LibOnly`), `if ($LibOnly) { return }` corta antes de invocar `Invoke-EEMain`.

- [ ] **Step 4: Run test to verify it passes**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests/Discovery.Tests.ps1"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add scripts/extraccion-estafeta/Invoke-ExtraccionEstafeta.ps1 scripts/extraccion-estafeta/tests/Discovery.Tests.ps1
git commit -m "feat(extraccion-estafeta): entry-point con config y descubrimiento de lote"
```

---

### Task 9: Orquestación end-to-end + README

**Files:**
- Modify: `scripts/extraccion-estafeta/Invoke-ExtraccionEstafeta.ps1`
- Create: `scripts/extraccion-estafeta/README.md`

**Interfaces:**
- Consumes: todas las funciones `lib/*`.
- Produces: `Invoke-EEMain([hashtable]$Params)` — orquesta descubrimiento, ingest paralelo (throttle `MaxParallel`), guardado de JSON, generación de Excel por tipología, `_resumen.xlsx`, `_log.csv`, `_run.json`. En `-DryRun` solo imprime el plan.

- [ ] **Step 1: Implementar `Invoke-EEMain`** (añadir antes de la línea `if ($LibOnly) { return }` de forma que la función quede definida; la llamada final permanece al pie del script)

```powershell
function Invoke-EEMain {
    param([hashtable]$Params)

    $cfg = Resolve-EEConfig -Params $Params
    if ([string]::IsNullOrWhiteSpace([string]$cfg.RootPath))   { throw "Falta -RootPath." }
    if ([string]::IsNullOrWhiteSpace([string]$cfg.BackendUrl)) { throw "Falta -BackendUrl." }
    $items = Get-EEWorkItems -RootPath $cfg.RootPath
    $resultsDir = Join-Path $cfg.RootPath 'Results'

    Write-Host "Extraccion Estafeta"
    Write-Host "  Backend : $($cfg.BackendUrl)"
    Write-Host "  Root    : $($cfg.RootPath)"
    Write-Host "  Salida  : $resultsDir"
    foreach ($it in $items) { Write-Host ("  - {0} ({1} PDFs) -> {2}" -f $it.FolderName, $it.Files.Count, $it.Tipologia) }

    if ($Params.ContainsKey('DryRun') -and $Params['DryRun']) {
        if ([string]::IsNullOrWhiteSpace([string]$cfg.FunctionKey)) { Write-Warning "Sin function key (se pediria en ejecucion real)." }
        Write-Host "DryRun: no se llama al backend."
        return
    }
    if ([string]::IsNullOrWhiteSpace([string]$cfg.FunctionKey)) { throw "Falta FunctionKey (parametro o env DOCUMENTIA_FUNCTION_KEY)." }

    $libDir = $LibDir
    $allLog = New-Object 'System.Collections.Generic.List[object]'
    $kpiRows = New-Object 'System.Collections.Generic.List[object]'
    $tipologiaTables = New-Object 'System.Collections.Generic.List[object]'

    foreach ($item in $items) {
        if ($item.Files.Count -eq 0) { continue }
        Write-Host "Procesando $($item.FolderName) ($($item.Files.Count) PDFs)..."
        $jsonDir = Join-Path (Join-Path $resultsDir $item.FolderName) 'json'
        New-Item -ItemType Directory -Path $jsonDir -Force | Out-Null

        $cfgLocal = $cfg
        $procResults = $item.Files | ForEach-Object -ThrottleLimit ([int]$cfg.MaxParallel) -Parallel {
            $file = $_
            . (Join-Path $using:libDir 'IngestClient.ps1')
            $c = $using:cfgLocal
            $expected = $using:item.Tipologia
            $jsonDir = $using:jsonDir
            $name = [System.IO.Path]::GetFileName($file)
            $correlationId = [guid]::NewGuid().ToString()
            $inicio = Get-Date
            $estado = 'Error'; $instanceId = ''; $mensaje = ''; $outputPath = $null
            $attempt = 0
            do {
                try {
                    $ingest = Invoke-EEIngest -BackendUrl $c.BackendUrl -FunctionKey $c.FunctionKey -FilePath $file -ExpectedType $expected -CorrelationId $correlationId -SkipGdcUpload ([bool]$c.SkipGdcUpload)
                    $instanceId = $ingest.InstanceId
                    $status = Wait-EEDurableStatus -StatusQueryUri $ingest.StatusQueryUri -FunctionKey $c.FunctionKey -TimeoutSeconds ([int]$c.TimeoutSeconds) -PollIntervalSeconds ([int]$c.PollIntervalSeconds)
                    if ($status.RuntimeStatus -eq 'Completed' -and $status.OutputJson) {
                        $outputPath = Join-Path $jsonDir ($name + '.json')
                        [System.IO.File]::WriteAllText($outputPath, $status.OutputJson)
                        $estado = 'Completado'
                    } elseif ($status.RuntimeStatus -eq 'Timeout') {
                        $estado = 'Timeout'; $mensaje = 'Timeout de polling'
                    } else {
                        $estado = 'Error'; $mensaje = $status.RuntimeStatus
                    }
                    break
                } catch {
                    $mensaje = $_.Exception.Message
                    if ($mensaje -match '401') { $estado = 'Error'; break }
                    $attempt++
                    if ($attempt -gt [int]$c.MaxRetries) { $estado = 'Error'; break }
                    Start-Sleep -Seconds 3
                }
            } while ($true)
            $fin = Get-Date
            [pscustomobject]@{
                Tipologia = $expected; Fichero = $name; Estado = $estado
                InstanceId = $instanceId; CorrelationId = $correlationId
                Inicio = $inicio.ToString('s'); Fin = $fin.ToString('s')
                DuracionSeg = [int]($fin - $inicio).TotalSeconds; Mensaje = $mensaje
                OutputPath = $outputPath
            }
        }

        # Construir tabla de la tipologia
        $docs = New-Object 'System.Collections.Generic.List[object]'
        $kpiInputs = New-Object 'System.Collections.Generic.List[object]'
        $openDocs = New-Object 'System.Collections.Generic.List[object]'
        foreach ($r in $procResults) {
            $jd = if ($r.OutputPath) { Open-EEJsonDocument $r.OutputPath } else { $null }
            if ($jd) {
                $docs.Add(@{ HasOutput = $true; Root = $jd.RootElement })
                $openDocs.Add($jd)
                $estadoCalidad = Get-EEPathValue $jd.RootElement 'Resultado.EstadoCalidad'
                $confRaw = Get-EEPathValue $jd.RootElement 'Resultado.ConfianzaGlobal'
                $conf = 0.0; $hasConf = [double]::TryParse($confRaw, [ref]$conf)
                $kpiInputs.Add(@{ Estado = $r.Estado; EstadoCalidad = $estadoCalidad; ConfianzaGlobal = (if ($hasConf) { $conf } else { $null }) })
            } else {
                $docs.Add(@{ HasOutput = $false; Root = [System.Text.Json.JsonElement]::new() })
                $kpiInputs.Add(@{ Estado = $r.Estado; EstadoCalidad = ''; ConfianzaGlobal = $null })
            }
            $allLog.Add($r)
        }
        $table = Build-EEExportTable $docs.ToArray()
        foreach ($jd in $openDocs) { $jd.Dispose() }

        $xlsxPath = Join-Path (Join-Path $resultsDir $item.FolderName) ($item.Tipologia + '.xlsx')
        Write-EEWorkbook -Path $xlsxPath -Sheets @(@{ Name = 'Resumen'; Headers = $table.Headers; Rows = $table.Rows })
        $kpiRows.Add((Get-EEKpiRow -Tipologia $item.Tipologia -Results $kpiInputs.ToArray()))
        $tipologiaTables.Add(@{ Name = $item.Tipologia; Headers = $table.Headers; Rows = $table.Rows })
        Write-Host "  -> $xlsxPath"
    }

    Write-EEResumenWorkbook -Path (Join-Path $resultsDir '_resumen.xlsx') -KpiRows $kpiRows.ToArray() -TipologiaTables $tipologiaTables.ToArray()
    Write-EELogCsv -Path (Join-Path $resultsDir '_log.csv') -LogRows $allLog.ToArray()
    Write-EERunJson -Path (Join-Path $resultsDir '_run.json') -Meta @{
        backendUrl = $cfg.BackendUrl; rootPath = $cfg.RootPath
        maxParallel = $cfg.MaxParallel; skipGdcUpload = $cfg.SkipGdcUpload
        tipologias = @($items | ForEach-Object { $_.Tipologia })
        totalDocumentos = $allLog.Count
        completados = @($allLog | Where-Object { $_.Estado -eq 'Completado' }).Count
    }
    Write-Host "Hecho. Salidas en $resultsDir"
}
```

Nota de sintaxis: sustituir la expresión `(if ($hasConf) {...} else {...})` por una asignación previa si el parser lo requiere:
```powershell
$confVal = $null; if ($hasConf) { $confVal = $conf }
$kpiInputs.Add(@{ Estado = $r.Estado; EstadoCalidad = $estadoCalidad; ConfianzaGlobal = $confVal })
```

- [ ] **Step 2: Verificar -DryRun (sin backend)**

Preparar una raíz de prueba y ejecutar:

```bash
pwsh -NoProfile -File scripts/extraccion-estafeta/Invoke-ExtraccionEstafeta.ps1 -RootPath "<carpeta_prueba>" -BackendUrl "https://x" -DryRun
```
Expected: lista subcarpetas → códigos y nº de PDFs; imprime "DryRun: no se llama al backend."; no crea `Results/`.

- [ ] **Step 3: Ejecutar toda la suite Pester**

Run: `pwsh -NoProfile -Command "Invoke-Pester scripts/extraccion-estafeta/tests -Output Detailed"`
Expected: PASS en todos los ficheros de test.

- [ ] **Step 4: Escribir README**

Create `scripts/extraccion-estafeta/README.md` con: propósito, requisitos (pwsh 7, function key por env `DOCUMENTIA_FUNCTION_KEY`), ejemplo de invocación real y `-DryRun`, descripción de salidas (`Results/`), y nota de que AssetResolver lo decide el orquestador.

Contenido mínimo:

```markdown
# Extracción Estafeta

Script PowerShell 7 que fuerza la extracción de un lote de PDFs organizados por subcarpeta de
tipología contra el backend de DocumentIA y genera un Excel por tipología (formato batch),
`_resumen.xlsx`, `_log.csv` y `_run.json` en `<RootPath>/Results/`.

## Requisitos
- PowerShell 7+ (`pwsh`).
- Function key de PROD en `DOCUMENTIA_FUNCTION_KEY` (o parámetro `-FunctionKey`).

## Uso
    $env:DOCUMENTIA_FUNCTION_KEY = "<clave>"
    pwsh -File Invoke-ExtraccionEstafeta.ps1 -RootPath "C:\docs\lote" -BackendUrl "https://<func-prod>.azurewebsites.net"

Vista previa sin llamar al backend:
    pwsh -File Invoke-ExtraccionEstafeta.ps1 -RootPath "C:\docs\lote" -BackendUrl "https://x" -DryRun

## Notas
- El script NO envía `assetResolver`: el orquestador lo ejecuta donde la tipología lo tenga configurado.
- `skipGDCUpload` por defecto true (extracción de prueba).
- Mapeo carpeta→tipología: `_` → `.` (p.ej. `cera.44_vado` → `cera.44.vado`).
```

- [ ] **Step 5: Commit**

```bash
git add scripts/extraccion-estafeta/Invoke-ExtraccionEstafeta.ps1 scripts/extraccion-estafeta/README.md
git commit -m "feat(extraccion-estafeta): orquestacion end-to-end y documentacion"
```

- [ ] **Step 6: Smoke real contra PROD (aceptación manual)**

Con una subcarpeta pequeña (1-2 PDFs) de una tipología conocida:

```bash
export DOCUMENTIA_FUNCTION_KEY="<clave-prod>"
pwsh -NoProfile -File scripts/extraccion-estafeta/Invoke-ExtraccionEstafeta.ps1 -RootPath "<carpeta_lote>" -BackendUrl "https://<func-prod>.azurewebsites.net" -MaxParallel 2
```
Expected: se crea `<carpeta_lote>/Results/` con el JSON por documento, `<codigo>.xlsx` por tipología, `_resumen.xlsx`, `_log.csv`, `_run.json`. Abrir el xlsx en Excel y comparar columnas/valores contra un export del batch para la misma tipología (deben coincidir). Verificar en un doc de tipología con AssetResolver configurado que `DetalleEjecucion.AssetResolver.*` viene poblado.

---

## Self-Review

**Spec coverage:**
- Propósito / forzar extracción / expectedType → Task 5,6,9.
- Sin `assetResolver` (orquestador decide) → Task 5 (test lo verifica) + README.
- Mapeo `_`→`.` → Task 1.
- Solo PDF primer nivel → Task 8.
- Excel formato batch (headers + FormatCellValue + writer) → Tasks 2,3,4.
- Resumen (KPIs + hoja por tipología) → Task 7.
- Log CSV + run.json → Task 7.
- Concurrencia 2 → Task 9 (`-ThrottleLimit`).
- Reintentos / 401 / timeout → Task 6,9.
- Params/config/env key → Task 8.
- `-DryRun` → Task 8,9.
- Testing Pester + fixtures → Tasks 2-9.
- Salidas en `<RootPath>/Results/` → Task 9.

**Placeholder scan:** el README de Task 9 se describe con contenido mínimo concreto; no quedan TODO/TBD.

**Type consistency:** `Build-EEExportTable` devuelve `Headers`/`Rows`; consumido igual en Tasks 4,7,9. `Get-EEKpiRow` propiedades usadas en `Write-EEResumenWorkbook`. `Invoke-EEIngest`/`Wait-EEDurableStatus` firmas coinciden entre Task 6 y Task 9. Sheet shape `@{ Name; Headers; Rows }` consistente entre Tasks 4,7,9.

## Execution Handoff

Ver mensaje de handoff tras guardar el plan.
