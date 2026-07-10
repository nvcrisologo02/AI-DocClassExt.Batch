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
