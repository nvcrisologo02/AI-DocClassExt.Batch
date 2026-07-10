BeforeAll {
    . "$PSScriptRoot/../lib/ExcelWriter.ps1"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}

Describe 'Write-EEWorkbook' {
    It 'crea un xlsx valido con las hojas, cabeceras y valores esperados' {
        $out = Join-Path ([System.IO.Path]::GetTempPath()) ("ee-{0}.xlsx" -f ([guid]::NewGuid()))
        $sheets = @(
            @{ Name = 'Resumen'; Headers = @('Col1','Col2'); Rows = @( ,@('a','b'), ,@('c','d') ) }
        )
        Write-EEWorkbook -Path $out -Sheets $sheets
        Test-Path $out | Should -BeTrue

        $zip = [System.IO.Compression.ZipFile]::OpenRead($out)
        try {
            ($zip.Entries.FullName -contains 'xl/worksheets/sheet1.xml') | Should -BeTrue
            $entry = $zip.GetEntry('xl/worksheets/sheet1.xml')
            $reader = New-Object System.IO.StreamReader($entry.Open())
            $xml = $reader.ReadToEnd(); $reader.Dispose()
            $xml | Should -Match 'Col1'
            $xml | Should -Match '>a<'
            $xml | Should -Match '>d<'
        } finally { $zip.Dispose() }

        Remove-Item $out -Force
    }
}
