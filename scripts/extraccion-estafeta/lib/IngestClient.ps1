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
