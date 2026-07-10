BeforeAll {
    . "$PSScriptRoot/../lib/ExcelWriter.ps1"
    . "$PSScriptRoot/../lib/Outputs.ps1"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}

Describe 'Get-EEKpiRow' {
    It 'agrega totales y confianza media' {
        $results = @(
            @{ Estado='Completado'; EstadoCalidad='Aprobado'; ConfianzaGlobal=0.9 },
            @{ Estado='Completado'; EstadoCalidad='Revision'; ConfianzaGlobal=0.6 },
            @{ Estado='Error';      EstadoCalidad='';         ConfianzaGlobal=$null }
        )
        $k = Get-EEKpiRow 'cera.44.vado' $results
        $k.Total       | Should -Be 3
        $k.Completados | Should -Be 2
        $k.Error       | Should -Be 1
        $k.Revision    | Should -Be 1
        [math]::Round($k.ConfianzaMedia, 2) | Should -Be 0.75
    }
}

Describe 'Write-EELogCsv' {
    It 'escribe cabeceras y filas' {
        $out = Join-Path ([System.IO.Path]::GetTempPath()) ("ee-log-{0}.csv" -f ([guid]::NewGuid()))
        Write-EELogCsv -Path $out -LogRows @(
            @{ Tipologia='t'; Fichero='f.pdf'; Estado='Completado'; InstanceId='i'; CorrelationId='c'; Inicio='x'; Fin='y'; DuracionSeg=3; Mensaje='' }
        )
        (Get-Content $out -Raw) | Should -Match 'Tipologia'
        (Get-Content $out -Raw) | Should -Match 'f.pdf'
        Remove-Item $out -Force
    }
}

Describe 'Write-EEResumenWorkbook' {
    It 'crea xlsx con hoja KPIs y una por tipologia' {
        $out = Join-Path ([System.IO.Path]::GetTempPath()) ("ee-res-{0}.xlsx" -f ([guid]::NewGuid()))
        $kpis = @([pscustomobject]@{ Tipologia='t1'; Total=1; Completados=1; Error=0; Revision=0; ConfianzaMedia=0.9 })
        $tables = @(@{ Name='t1'; Headers=@('A'); Rows=@( ,@('v') ) })
        Write-EEResumenWorkbook -Path $out -KpiRows $kpis -TipologiaTables $tables
        $zip = [System.IO.Compression.ZipFile]::OpenRead($out)
        try { ($zip.Entries.FullName -contains 'xl/worksheets/sheet2.xml') | Should -BeTrue }
        finally { $zip.Dispose() }
        Remove-Item $out -Force
    }
}
