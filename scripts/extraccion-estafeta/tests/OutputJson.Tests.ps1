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
