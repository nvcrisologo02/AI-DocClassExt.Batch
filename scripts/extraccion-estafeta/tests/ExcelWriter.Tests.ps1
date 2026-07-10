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

    It 'escribe correctamente un libro con dos hojas (ZipArchive CreateEntry no falla en la segunda hoja)' {
        $out = Join-Path ([System.IO.Path]::GetTempPath()) ("ee-{0}.xlsx" -f ([guid]::NewGuid()))

        $rows1 = New-Object System.Collections.Generic.List[object]
        $rows1.Add([string[]]@('a', 'b'))
        $rows1.Add([string[]]@('c', 'd'))

        $rows2 = New-Object System.Collections.Generic.List[object]
        $rows2.Add([string[]]@('x', 'y'))
        $rows2.Add([string[]]@('z', 'w'))

        $sheets = @(
            @{ Name = 'Resumen'; Headers = [string[]]@('Col1', 'Col2'); Rows = $rows1.ToArray() },
            @{ Name = 'Detalle'; Headers = [string[]]@('Col3', 'Col4'); Rows = $rows2.ToArray() }
        )
        Write-EEWorkbook -Path $out -Sheets $sheets
        Test-Path $out | Should -BeTrue

        $zip = [System.IO.Compression.ZipFile]::OpenRead($out)
        try {
            ($zip.Entries.FullName -contains 'xl/worksheets/sheet1.xml') | Should -BeTrue
            ($zip.Entries.FullName -contains 'xl/worksheets/sheet2.xml') | Should -BeTrue

            $entry2 = $zip.GetEntry('xl/worksheets/sheet2.xml')
            $reader2 = New-Object System.IO.StreamReader($entry2.Open())
            $xml2 = $reader2.ReadToEnd(); $reader2.Dispose()
            $xml2 | Should -Match 'Col3'
            $xml2 | Should -Match '>x<'
            $xml2 | Should -Match '>w<'
        } finally { $zip.Dispose() }

        Remove-Item $out -Force
    }
}
