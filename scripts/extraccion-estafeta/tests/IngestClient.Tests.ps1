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
