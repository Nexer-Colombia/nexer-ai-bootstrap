Set-StrictMode -Version 2.0

foreach ($folder in @('Private', 'Public')) {
    $path = Join-Path $PSScriptRoot $folder
    foreach ($file in @(Get-ChildItem -Path $path -Filter '*.ps1' -File -ErrorAction SilentlyContinue)) {
        . $file.FullName
    }
}
