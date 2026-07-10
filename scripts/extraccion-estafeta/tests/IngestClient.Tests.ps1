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
