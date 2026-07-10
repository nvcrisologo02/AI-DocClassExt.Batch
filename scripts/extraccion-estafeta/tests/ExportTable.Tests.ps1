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
