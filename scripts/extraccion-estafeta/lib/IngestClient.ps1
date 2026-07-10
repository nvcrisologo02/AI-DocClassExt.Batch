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
    $lastError = $null
    $sawSuccess = $false
    try {
        while ((Get-Date) -lt $deadline) {
            $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, $StatusQueryUri)
            if (-not [string]::IsNullOrWhiteSpace($FunctionKey)) {
                [void]$req.Headers.TryAddWithoutValidation('x-functions-key', $FunctionKey)
            }
            $resp = $client.SendAsync($req).GetAwaiter().GetResult()
            $payload = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if ($resp.IsSuccessStatusCode) {
                $sawSuccess = $true
                $parsed = Get-EEOutputFromStatus $payload
                if ($parsed.RuntimeStatus -in @('Completed','Failed','Terminated')) { return $parsed }
            } elseif ([int]$resp.StatusCode -eq 401) {
                throw "401 Unauthorized consultando status: revisa la function key."
            } else {
                $truncated = if ($payload.Length -gt 300) { $payload.Substring(0, 300) } else { $payload }
                $lastError = "HTTP $([int]$resp.StatusCode): $truncated"
            }
            Start-Sleep -Seconds $PollIntervalSeconds
        }
        if (-not $sawSuccess -and $lastError) {
            return @{ RuntimeStatus = 'Error'; OutputJson = $null; Message = $lastError }
        }
        return @{ RuntimeStatus = 'Timeout'; OutputJson = $null }
    } finally {
        $client.Dispose()
    }
}
