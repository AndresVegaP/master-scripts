
$hashBytes = [System.Security.Cryptography.SHA1]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($engineSource))
$hash = ([System.BitConverter]::ToString($hashBytes) -replace '-', '').Substring(0, 12)
$ns = "SpaEngine_$hash"
$engineType = "$ns.Engine" -as [type]
if (-not $engineType) {
    $src = $engineSource.Replace('SPA_NS', $ns)
    $addTypeArgs = @{ TypeDefinition = $src; Language = 'CSharp'; IgnoreWarnings = $true }
    if ($PSVersionTable.PSEdition -ne 'Core') { $addTypeArgs['ReferencedAssemblies'] = @('System.Core', 'System.Xml', 'System.Xml.Linq') }
    try { Add-Type @addTypeArgs -ErrorAction Stop }
    catch { throw "No se pudo compilar el motor de analisis: $($_.Exception.Message)" }
    $engineType = "$ns.Engine" -as [type]
}
$optType = "$ns.AnalyzerOptions" -as [type]

$opt = New-Object $optType
$opt.RepoRoot = $RepoPath
$opt.CsFiles = [string[]]$csFiles.ToArray()
$opt.SqlFiles = [string[]]$sqlFiles.ToArray()
$opt.ResxFiles = [string[]]$resxFiles.ToArray()
$opt.ConfigFiles = [string[]]$configFiles.ToArray()
$opt.PackagePrefixes = [string[]]$PackagePrefixes
$opt.ObjectPrefixes = [string[]]$ObjectPrefixes
$opt.MaxDepth = $MaxDepth

Write-Host '  Analizando ...'
$res = $engineType::Run($opt)

# ---------------------------------------------------------------- utilidades de render
$nl = "`n"
$outDirFull = [System.IO.Path]::GetDirectoryName($OutputPath)
$baseUri = New-Object System.Uri (($outDirFull.TrimEnd('\') + '\'))

# texto plano en celdas: sin saltos de linea, "|" escapado y < > & como entidades (Program.<top-level>, Repo<T>)
function Esc([string]$s) {
    if ($null -eq $s) { return '' }
    $s = $s -replace '\r?\n', ' '
    $s = $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
    return ($s -replace '\|', '\|')
}
# span de codigo: dentro no se interpretan entidades; si el texto tiene comillas invertidas se usa doble delimitador
function Code([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return '' }
    $v = ($s -replace '\r?\n', ' ') -replace '\|', '\|'
    if ($v.Contains('`')) { return '`` ' + $v + ' ``' }
    return '`' + $v + '`'
}
function LocLink($loc) {
    if ($null -eq $loc -or $null -eq $loc.File) { return '' }
    $full = $loc.File.Path
    $rel = $loc.File.Rel
    try { $href = $baseUri.MakeRelativeUri((New-Object System.Uri $full)).ToString() } catch { $href = $rel }
    return "[{0}:{1}]({2}#L{1})" -f (Esc $rel), $loc.Line, $href
}
function LocText($loc) { if ($null -eq $loc -or $null -eq $loc.File) { return '' } return "{0}:{1}" -f $loc.File.Rel, $loc.Line }
function EpCell($ep) {
    $h = $ep.HandlerName
    $s = '`' + $ep.Verb + ' ' + $ep.Route + '`'
    if ($h) { $s += '<br><sub>' + (Esc $h) + '</sub>' }
    return $s
}
function TipoText($r) {
    $t = switch ($r.Tipo) {
        'DIRECTO' { 'Llamado directamente' }
        'DIRECTO_EN_QUERY' { 'Llamado directamente (dentro de query)' }
        'MIGRADO_LISTO' { 'Migrado sin hijos (listo)' }
        'HIJO' {
            $base = if ($r.Level -gt 1) { "SP hijo nivel $($r.Level)" } else { 'SP hijo' }
            if ($r.ChildStatus -eq 'MIGRADO_EN_REPO') { "$base (ya migrado en el repo)" } else { "$base (pendiente)" }
        }
        'SOLO_COMENTARIO' { 'Solo en comentario (revisar)' }
        default { $r.Tipo }
    }
    if ($r.Inferred) { $t += ' &dagger;' }
    if ($r.Tipo -eq 'DIRECTO' -and $r.Form) { $t += '<br><sub>' + (Esc $r.Form) + '</sub>' }
    return $t
}
$tipoOrder = @{ 'DIRECTO' = 1; 'DIRECTO_EN_QUERY' = 2; 'MIGRADO_LISTO' = 3; 'HIJO' = 4; 'SOLO_COMENTARIO' = 5 }

$endpoints = @($res.Endpoints | Sort-Object Route, Verb, HandlerName)
# orden: endpoint, tipo, SP y cadena de hijos (los nietos quedan justo debajo de su hijo)
$rows = @($res.Rows | Sort-Object @{ e = { $_.Ep.Route } }, @{ e = { $_.Ep.Verb } }, @{ e = { $_.Ep.HandlerName } }, @{ e = { $tipoOrder[$_.Tipo] -replace '^4$', '3' } }, @{ e = { $_.Sp.Key } }, @{ e = { $_.ChildChain } })
$codeRows = @($rows | Where-Object { $_.Tipo -ne 'SOLO_COMENTARIO' })
$soloRows = @($rows | Where-Object { $_.Tipo -eq 'SOLO_COMENTARIO' })

function EpList($list) {
    $eps = @($list | ForEach-Object { '`' + $_.Ep.Verb + ' ' + $_.Ep.Route + '`' } | Sort-Object -Unique)
    return ($eps -join '<br>')
}
function LocList($locs) {
    $seen = @{}
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($l in $locs) { if ($null -eq $l) { continue }; $k = LocText $l; if (-not $seen.ContainsKey($k)) { $seen[$k] = 1; $out.Add((LocLink $l)) } }
    return ($out -join '<br>')
}

# ---------------------------------------------------------------- agregados por tipo
$directKeys = @{}
foreach ($r in $codeRows) { if ($r.Tipo -eq 'DIRECTO' -or $r.Tipo -eq 'DIRECTO_EN_QUERY') { $directKeys[$r.Sp.Key] = 1 } }

$migr = @{}   # key -> @{ Sp; Rows; HasChildren }
foreach ($r in $codeRows) {
    if ($r.Tipo -ne 'MIGRADO_LISTO' -and $r.Tipo -ne 'HIJO') { continue }
    $k = $r.Sp.Key
    if (-not $migr.ContainsKey($k)) { $migr[$k] = @{ Sp = $r.Sp; Rows = New-Object System.Collections.ArrayList; HasChildren = $false } }
    [void]$migr[$k].Rows.Add($r)
    if ($r.Tipo -eq 'HIJO') { $migr[$k].HasChildren = $true }
}
$readyKeys = @($migr.Keys | Where-Object { -not $migr[$_].HasChildren } | Sort-Object)
$withChildKeys = @($migr.Keys | Where-Object { $migr[$_].HasChildren } | Sort-Object)

$directGroups = @($codeRows | Where-Object { $_.Tipo -eq 'DIRECTO' -or $_.Tipo -eq 'DIRECTO_EN_QUERY' } | Group-Object { $_.Sp.Key } | Sort-Object Name)
$childRows = @($codeRows | Where-Object { $_.Tipo -eq 'HIJO' })
$childGroups = @($childRows | Group-Object { $_.Sp.Key + '|' + $_.ChildChain } | Sort-Object Name)

# inventario de pendientes: llamados directamente + hijos no migrados en el repo
$pending = @{}
foreach ($r in $codeRows) {
    $sp = $null; $why = $null
    if ($r.Tipo -eq 'DIRECTO') { $sp = $r.Sp; $why = 'Llamado directamente' }
    elseif ($r.Tipo -eq 'DIRECTO_EN_QUERY') { $sp = $r.Sp; $why = 'Llamado dentro de query' }
    elseif ($r.Tipo -eq 'HIJO' -and $r.ChildStatus -ne 'MIGRADO_EN_REPO') { $sp = $r.Child; $why = 'Hijo de ' + $r.Sp.Key }
    if ($null -eq $sp) { continue }
    $k = $sp.Key
    if (-not $pending.ContainsKey($k)) { $pending[$k] = @{ Sp = $sp; Why = New-Object System.Collections.Generic.HashSet[string]; Eps = New-Object System.Collections.Generic.HashSet[string] } }
    [void]$pending[$k].Why.Add($why)
    [void]$pending[$k].Eps.Add($r.Ep.Verb + ' ' + $r.Ep.Route)
}
$pendingList = @($pending.Values | Sort-Object @{ e = { $_.Eps.Count }; Descending = $true }, @{ e = { $_.Sp.Key } })

$epWithSp = @{}
foreach ($r in $rows) { $epWithSp[$r.Ep.Id] = 1 }
$epNoSp = @($endpoints | Where-Object { -not $epWithSp.ContainsKey($_.Id) })
$epWithCode = @{}
foreach ($r in $codeRows) { $epWithCode[$r.Ep.Id] = 1 }
