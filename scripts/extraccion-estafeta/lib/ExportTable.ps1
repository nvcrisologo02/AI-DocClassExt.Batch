Set-StrictMode -Version Latest

function Get-EETipologiaCode {
    param([Parameter(Mandatory)][string]$FolderName)
    return $FolderName.Replace('_', '.')
}

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
        $rowArray = $row.ToArray()
        $rows.Add($rowArray)
    }
    return [pscustomobject]@{ Headers = $headers; Rows = $rows.ToArray() }
}
