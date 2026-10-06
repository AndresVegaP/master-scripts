param([string[]]$Names = @('A-ventas-mvc','B-inventario-mediatr','C-legacy-webapi2','D-cleanarch'), [switch]$NoRun)
$ErrorActionPreference = 'Stop'
$d = $PSScriptRoot
$script = Join-Path (Split-Path $d -Parent) 'Analizar-SpEndpoints.ps1'
$tot = 0; $totMiss = 0; $totExtra = 0
foreach ($n in $Names) {
  $fx = Join-Path $d "fixtures\$n"
  $out = Join-Path $d "out\$n.md"
  if (-not $NoRun) { & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -RepoPath $fx -OutputPath $out -ExportCsv | Out-Null }
  $csv = Import-Csv ([IO.Path]::ChangeExtension($out, '.csv')) -Delimiter ';'
  $got = @{}
  foreach ($r in $csv) {
    $tipo = $r.Tipo; if ($tipo -eq 'HIJO' -and [int]$r.Nivel -ge 2) { $tipo = 'HIJO_N' + $r.Nivel }
    $k = ('{0} {1}|{2}|{3}|{4}' -f $r.Verbo, $r.Ruta, $r.SP, $r.SpHijo, $tipo).ToUpperInvariant()
    $got[$k + '|' + ('{0}:{1}' -f $r.Archivo, $r.Linea).ToUpperInvariant()] = $r
  }
  $exp = @{}
  $lines = Get-Content (Join-Path $d "fixtures\$n.EXPECTED.md") -Encoding UTF8
  $in = $false
  foreach ($l in $lines) {
    if ($l -match '^## Filas esperadas') { $in = $true; continue }
    if ($in -and $l -match '^## ') { break }
    if (-not $in -or $l -notmatch '^\|') { continue }
    $c = $l.Trim('|').Split('|') | ForEach-Object { $_.Trim().Trim('`').Trim() }
    if ($c[0] -eq 'Endpoint' -or $c[0] -match '^-+$' -or $c.Count -lt 5) { continue }
    $hijo = if ($c[2] -eq '-' -or $c[2] -eq '') { '' } else { (($c[2] -replace [string][char]0x2192, '->') -replace ' *-+> *', ' -> ') }
    $ep = ($c[0] -replace '`','').Trim()
    $loc = ($c[4] -replace '`','').Trim()
    $k = ('{0}|{1}|{2}|{3}' -f $ep, $c[1], $hijo, $c[3]).ToUpperInvariant()
    $exp[$k + '|' + $loc.ToUpperInvariant()] = $l
  }
  $miss = @($exp.Keys | Where-Object { -not $got.ContainsKey($_) } | Sort-Object)
  $extra = @($got.Keys | Where-Object { -not $exp.ContainsKey($_) } | Sort-Object)
  "==== $n : esperadas $($exp.Count), obtenidas $($got.Count), faltan $($miss.Count), sobran $($extra.Count)"
  foreach ($m in $miss) { "  FALTA : $m" }
  foreach ($x in $extra) { "  SOBRA : $x" }
  $tot += $exp.Count; $totMiss += $miss.Count; $totExtra += $extra.Count
}
"TOTAL esperadas $tot, faltan $totMiss, sobran $totExtra"
