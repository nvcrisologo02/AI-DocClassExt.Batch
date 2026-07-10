Set-StrictMode -Version Latest

function Get-EEColumnName {
    param([int]$ColumnNumber)
    $name = ''
    while ($ColumnNumber -gt 0) {
        $ColumnNumber--
        $name = [char]([int][char]'A' + ($ColumnNumber % 26)) + $name
        $ColumnNumber = [math]::Floor($ColumnNumber / 26)
    }
    return $name
}

function Write-EETextEntry {
    param([System.IO.Compression.ZipArchive]$Archive, [string]$Name, [string]$Content)
    $entry = $Archive.CreateEntry($Name, [System.IO.Compression.CompressionLevel]::Optimal)
    $writer = New-Object System.IO.StreamWriter($entry.Open(), (New-Object System.Text.UTF8Encoding($false)))
    try { $writer.Write($Content) } finally { $writer.Dispose() }
}

function Write-EERow {
    param([System.Xml.XmlWriter]$Writer, [int]$RowNumber, [string[]]$Values)
    $Writer.WriteStartElement('row')
    $Writer.WriteAttributeString('r', $RowNumber.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    for ($i = 0; $i -lt $Values.Count; $i++) {
        $Writer.WriteStartElement('c')
        $Writer.WriteAttributeString('r', ('{0}{1}' -f (Get-EEColumnName ($i + 1)), $RowNumber))
        $Writer.WriteAttributeString('t', 'inlineStr')
        $Writer.WriteStartElement('is')
        $Writer.WriteStartElement('t')
        $Writer.WriteAttributeString('xml', 'space', $null, 'preserve')
        $v = $Values[$i]; if ($null -eq $v) { $v = '' }
        $Writer.WriteString($v)
        $Writer.WriteEndElement()
        $Writer.WriteEndElement()
        $Writer.WriteEndElement()
    }
    $Writer.WriteEndElement()
}

# Unwraps the single-element-array-wrapping produced when a caller builds a row using the
# `,@(...)` comma-operator literal (as the test fixtures in ExcelWriter.Tests.ps1 do); real
# callers (ExportTable's Build-EEExportTable) pass a plain [string[]], so this is a no-op for
# production data.
function ConvertTo-EERowValues {
    param([object]$Row)
    $values = $Row
    while ($values.Count -eq 1 -and $values[0] -is [object[]]) { $values = $values[0] }
    return [string[]]$values
}

function Write-EEWorksheet {
    param([System.IO.Compression.ZipArchive]$Archive, [int]$Index, [object]$Sheet)
    $entry = $Archive.CreateEntry(('xl/worksheets/sheet{0}.xml' -f $Index), [System.IO.Compression.CompressionLevel]::Optimal)
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $settings.Indent = $false
    $settings.CloseOutput = $true
    $writer = [System.Xml.XmlWriter]::Create($entry.Open(), $settings)
    try {
        $writer.WriteStartDocument()
        $writer.WriteStartElement('worksheet', 'http://schemas.openxmlformats.org/spreadsheetml/2006/main')
        $writer.WriteStartElement('sheetData')
        $rowNumber = 1
        Write-EERow -Writer $writer -RowNumber $rowNumber -Values ([string[]]$Sheet.Headers); $rowNumber++
        foreach ($row in $Sheet.Rows) {
            Write-EERow -Writer $writer -RowNumber $rowNumber -Values (ConvertTo-EERowValues -Row $row); $rowNumber++
        }
        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndDocument()
    } finally { $writer.Dispose() }
}

function Write-EEWorkbook {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][object[]]$Sheets)

    $contentTypes = @'
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
'@
    for ($i = 1; $i -le $Sheets.Count; $i++) {
        $contentTypes += "`n  <Override PartName=`"/xl/worksheets/sheet$i.xml`" ContentType=`"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml`"/>"
    }
    $contentTypes += "`n  <Override PartName=`"/xl/workbook.xml`" ContentType=`"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml`"/>`n</Types>"

    $rootRels = @'
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
'@

    $sheetsXml = ''
    $wbRels = ''
    for ($i = 1; $i -le $Sheets.Count; $i++) {
        $safeName = [System.Security.SecurityElement]::Escape($Sheets[$i-1].Name)
        $sheetsXml += "<sheet name=`"$safeName`" sheetId=`"$i`" r:id=`"rId$i`"/>"
        $wbRels += "<Relationship Id=`"rId$i`" Type=`"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet`" Target=`"worksheets/sheet$i.xml`"/>"
    }
    $workbookXml = "<workbook xmlns=`"http://schemas.openxmlformats.org/spreadsheetml/2006/main`" xmlns:r=`"http://schemas.openxmlformats.org/officeDocument/2006/relationships`"><sheets>$sheetsXml</sheets></workbook>"
    $workbookRels = "<Relationships xmlns=`"http://schemas.openxmlformats.org/package/2006/relationships`">$wbRels</Relationships>"

    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    $archive = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Write-EETextEntry -Archive $archive -Name '[Content_Types].xml' -Content $contentTypes
        Write-EETextEntry -Archive $archive -Name '_rels/.rels' -Content $rootRels
        Write-EETextEntry -Archive $archive -Name 'xl/workbook.xml' -Content $workbookXml
        Write-EETextEntry -Archive $archive -Name 'xl/_rels/workbook.xml.rels' -Content $workbookRels
        for ($i = 1; $i -le $Sheets.Count; $i++) {
            Write-EEWorksheet -Archive $archive -Index $i -Sheet $Sheets[$i-1]
        }
    } finally {
        $archive.Dispose()
        $stream.Dispose()
    }
}
