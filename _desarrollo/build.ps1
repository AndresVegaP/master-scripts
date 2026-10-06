$ErrorActionPreference = 'Stop'
$d = $PSScriptRoot
$head = [IO.File]::ReadAllText((Join-Path $d 'ps\template-head.ps1'))
$tail = [IO.File]::ReadAllText((Join-Path $d 'ps\template-tail.ps1'))
$render = [IO.File]::ReadAllText((Join-Path $d 'ps\template-render.ps1'))
$parts = Get-ChildItem (Join-Path $d 'cs') -Filter *.cs | Sort-Object Name
$engine = ($parts | ForEach-Object { [IO.File]::ReadAllText($_.FullName).TrimEnd() }) -join "`r`n`r`n"
foreach ($l in ($engine -split "`n")) { if ($l.StartsWith("'@")) { throw "Linea invalida en el motor: $l" } }
$final = $head.Replace('#__ENGINE_SOURCE__#', $engine) + $tail + $render
$final = $final -replace "(?<!`r)`n", "`r`n"
$out = Join-Path (Split-Path $d -Parent) 'Analizar-SpEndpoints.ps1'
[IO.File]::WriteAllText($out, $final, (New-Object Text.UTF8Encoding($true)))
"BUILD OK -> $out ({0} lineas)" -f ($final -split "`n").Count
