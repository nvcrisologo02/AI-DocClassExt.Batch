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
