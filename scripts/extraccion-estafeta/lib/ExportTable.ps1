Set-StrictMode -Version Latest

function Get-EETipologiaCode {
    param([Parameter(Mandatory)][string]$FolderName)
    return $FolderName.Replace('_', '.')
}
