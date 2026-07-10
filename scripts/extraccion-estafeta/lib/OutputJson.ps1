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
        return [System.Text.Json.JsonSerializer]::Serialize[System.Text.Json.JsonElement]($Value)
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
    if (-not $r.Found -or $r.Value.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { return ,@() }
    return @($r.Value.EnumerateObject() | ForEach-Object { $_.Name })
}
