param([string]$Case)
$script = Join-Path (Split-Path $PSScriptRoot -Parent) 'Analizar-SpEndpoints.ps1'
$out = Join-Path $PSScriptRoot ("out\quick\" + (Split-Path $Case -Leaf) + ".md")
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -RepoPath $Case -OutputPath $out -ExportCsv | Out-Null
"### " + (Split-Path $Case -Leaf)
Import-Csv ([IO.Path]::ChangeExtension($out, '.csv')) -Delimiter ';' | ForEach-Object { "  {0} {1} | {2} | {3} | {4} | {5}:{6}" -f $_.Verbo, $_.Ruta, $_.SP, $_.SpHijo, $_.Tipo, $_.Archivo, $_.Linea }
$md = Get-Content $out -Encoding UTF8
$in = $false
foreach ($l in $md) { if ($l -match '^## 6\.') { $in = $true; continue }; if ($in -and $l -match '^## ') { break }; if ($in -and $l -match '^\| `') { "  SIN-SP: $l" } }
$in = $false
foreach ($l in $md) { if ($l -match '^### 7\.2') { $in = $true; continue }; if ($in -and $l -match '^### ') { break }; if ($in -and $l -match '^\| (?!Categor|---)') { "  WARN: $l" } }
