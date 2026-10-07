
# ---------------------------------------------------------------- cruce con Swagger / OpenAPI
$swaggerInfo = $null
if (-not [string]::IsNullOrWhiteSpace($SwaggerPath)) {
    try {
        if ($SwaggerPath -match '^https?://') { $json = (Invoke-WebRequest -Uri $SwaggerPath -UseBasicParsing).Content }
        else {
            $swp = $SwaggerPath
            if (-not [System.IO.Path]::IsPathRooted($swp)) { $swp = Join-Path (Get-Location).Path $swp }
            $json = [System.IO.File]::ReadAllText($swp)
        }
        $doc = $json | ConvertFrom-Json
        $basePrefix = ''
        if ($doc.PSObject.Properties['basePath'] -and $doc.basePath) { $basePrefix = ([string]$doc.basePath).TrimEnd('/') }
        $norm = { param($v, $r) ($v.ToUpperInvariant() + ' ' + (($r -replace '\{[^}]*\}', '{}').TrimEnd('/').ToLowerInvariant())) }
        $swOps = @{}
        foreach ($p in $doc.paths.PSObject.Properties) {
            foreach ($m in $p.Value.PSObject.Properties) {
                if (@('get', 'post', 'put', 'delete', 'patch', 'head', 'options') -notcontains $m.Name) { continue }
                $route = $basePrefix + $p.Name
                $opId = ''
                if ($m.Value.PSObject.Properties['operationId']) { $opId = $m.Value.operationId }
                $swOps[(& $norm $m.Name $route)] = @{ Verb = $m.Name.ToUpperInvariant(); Route = $route; OpId = $opId }
            }
        }
        $codeOps = @{}
        foreach ($e in $endpoints) { $codeOps[(& $norm $e.Verb $e.Route)] = $e }
        $missingInCode = @($swOps.Keys | Where-Object { -not $codeOps.ContainsKey($_) -and -not $codeOps.ContainsKey((& $norm 'ANY' $swOps[$_].Route)) } | ForEach-Object { $swOps[$_] } | Sort-Object { $_.Route })
        $missingInSwagger = @($endpoints | Where-Object { $_.Verb -ne 'ANY' -and -not $swOps.ContainsKey((& $norm $_.Verb $_.Route)) })
        $swaggerInfo = @{ Total = $swOps.Count; MissingInCode = $missingInCode; MissingInSwagger = $missingInSwagger }
    }
    catch { Write-Warning "No se pudo leer el Swagger '$SwaggerPath': $($_.Exception.Message)" }
}

# ---------------------------------------------------------------- markdown
$sb = New-Object System.Text.StringBuilder
function W([string]$s) { [void]$sb.Append($s); [void]$sb.Append($nl) }
function ChainCell([string]$chain) {
    if ([string]::IsNullOrEmpty($chain)) { return '' }
    return ((($chain -split ' -> ') | ForEach-Object { '`' + $_ + '`' }) -join ' &rarr; ')
}

$now = Get-Date -Format 'yyyy-MM-dd HH:mm'
W '# Mapa de SP por endpoint'
W ''
W ("> Generado el {0} con ``Analizar-SpEndpoints.ps1`` v{1}  " -f $now, $ScriptVersion)
W ("> Carpeta analizada: ``{0}``  " -f $RepoPath)
W ("> Prefijos de package: {0} &middot; Prefijos de SP/funciones sin package: {1}" -f (($PackagePrefixes | ForEach-Object { '`' + $_ + '`' }) -join ', '), (($ObjectPrefixes | ForEach-Object { '`' + $_ + '`' }) -join ', '))
W ''
W '## Resumen'
W ''
W '| Indicador | Valor |'
W '|---|---:|'
W ("| Archivos C# analizados | {0} |" -f $res.FilesCs)
W ("| Archivos .sql en el repo (solo cuentan si el código los referencia) | {0} |" -f $res.FilesSql)
W ("| Endpoints detectados | {0} |" -f $endpoints.Count)
W ("| Endpoints con SP asociado | {0} |" -f $epWithSp.Count)
W ("| Endpoints sin SP asociado | {0} |" -f $epNoSp.Count)
W ("| **SP listos** (migrados sin hijos) | **{0}** |" -f $readyKeys.Count)
W ("| **SP migrados con SP hijos** | **{0}** |" -f $withChildKeys.Count)
W ("| **SP llamados directamente** | **{0}** |" -f $directGroups.Count)
W ("| SP hijos distintos detectados | {0} |" -f (@($childRows | ForEach-Object { $_.Child.Key } | Sort-Object -Unique)).Count)
W ("| **Total de SP pendientes de migrar** (llamados + hijos no migrados) | **{0}** |" -f $pendingList.Count)
W ("| SP mencionados solo en comentarios (revisar) | {0} |" -f (@($soloRows | ForEach-Object { $_.Sp.Key } | Sort-Object -Unique)).Count)
W ("| Referencias a SP no vinculadas a ningún endpoint | {0} |" -f (@($res.Orphans | Where-Object { $_.Kind -eq 'codigo' })).Count)
W ("| Advertencias | {0} |" -f $res.Warnings.Count)
W ''
W '**Cómo leer la columna Tipo**'
W ''
W '- **Llamado directamente**: el flujo del endpoint ejecuta el SP tal cual. Debajo se indica cómo: nombre del SP con `CommandType.StoredProcedure`, bloque `BEGIN ... END;`, `CALL`/`EXEC` o `SELECT PCK.FN(...) FROM DUAL`. El SP sigue pendiente de migrar.'
W '- **Llamado directamente (dentro de query)**: el SP o la función aparece dentro de una query (`SELECT`, `INSERT`, ...) que no tiene un comentario de SP migrado.'
W '- **Migrado sin hijos (listo)**: un comentario nombra el SP y la query que lo reemplaza ya no llama a ningún SP.'
W '- **SP hijo**: la query que reemplazó al SP de la segunda columna todavía llama al SP de la tercera columna. El estado indica si ese hijo ya está migrado en otro lugar del repo. Si lo está, sus propios hijos aparecen como "nivel 2", "nivel 3", etc.'
W '- **Solo en comentario (revisar)**: el SP se nombra en el flujo del endpoint, pero no hay código que lo respalde. Pasa, por ejemplo, cuando el nombre del SP viene de una variable o cuando no se encontró la query.'
W '- &dagger; = asociación inferida: el SP migrado se toma del comentario de un método llamador (por ejemplo, la acción del controller), o la llamada se resolvió solo por el nombre del método.'
W ''

# --- 1. tabla principal
W '## 1. SP por endpoint (un SP por fila)'
W ''
if ($rows.Count -eq 0) { W '_No se encontraron SP asociados a endpoints._' }
else {
    W '| Endpoint | Package.SP llamado / migrado | SP hijo llamado dentro de la query | Tipo | Archivo:línea |'
    W '|---|---|---|---|---|'
    foreach ($r in $rows) {
        $loc = LocLink $r.Loc
        if ($r.Detail -and $r.Tipo -ne 'SOLO_COMENTARIO') { $loc += '<br><sub>' + (Esc $r.Detail) + '</sub>' }
        W ("| {0} | {1} | {2} | {3} | {4} |" -f (EpCell $r.Ep), (Code $r.Sp.Display), (ChainCell $r.ChildChain), (TipoText $r), $loc)
    }
}
W ''

# --- 2. listos
W '## 2. SP listos (migrados sin hijos)'
W ''
if ($readyKeys.Count -eq 0) { W '_Ninguno._' }
else {
    W '| SP | Endpoints que lo usan | Comentario que lo identifica | Query que lo reemplaza | Observaciones |'
    W '|---|---|---|---|---|'
    foreach ($k in $readyKeys) {
        $g = $migr[$k]
        $obs = @()
        if ($directKeys.ContainsKey($k)) {
            $where = @($codeRows | Where-Object { ($_.Tipo -eq 'DIRECTO' -or $_.Tipo -eq 'DIRECTO_EN_QUERY') -and $_.Sp.Key -eq $k } | ForEach-Object { '`' + $_.Ep.Verb + ' ' + $_.Ep.Route + '`' } | Sort-Object -Unique)
            $obs += ('&#9888; También se llama directamente en: ' + ($where -join ', '))
        }
        if (@($g.Rows | Where-Object { $_.Inferred }).Count -gt 0) { $obs += 'Asociación inferida &dagger;' }
        W ("| {0} | {1} | {2} | {3} | {4} |" -f (Code $g.Sp.Display), (EpList $g.Rows), (LocList ($g.Rows | ForEach-Object { $_.Loc })), (LocList ($g.Rows | ForEach-Object { $_.QueryLoc })), ($obs -join '<br>'))
    }
}
W ''

# --- 3. migrados con hijos
W '## 3. SP migrados con SP hijos (pendientes)'
W ''
if ($childGroups.Count -eq 0) { W '_Ninguno._' }
else {
    W '| SP migrado | SP hijo (cadena) | Estado del hijo | Endpoints | Ubicación del hijo |'
    W '|---|---|---|---|---|'
    foreach ($g in $childGroups) {
        $r0 = $g.Group[0]
        $estado = if ($r0.ChildStatus -eq 'MIGRADO_EN_REPO') { 'Ya migrado en el repo' } else { '**Pendiente**' }
        if ($directKeys.ContainsKey($r0.Child.Key)) { $estado += '<br>&#9888; también se llama directamente' }
        W ("| {0} | {1} | {2} | {3} | {4} |" -f (Code $r0.Sp.Display), (ChainCell $r0.ChildChain), $estado, (EpList $g.Group), (LocList ($g.Group | ForEach-Object { $_.Loc })))
    }
}
W ''

# --- 4. llamados directamente
W '## 4. SP llamados directamente (pendientes)'
W ''
if ($directGroups.Count -eq 0) { W '_Ninguno._' }
else {
    W '| SP | Forma de llamada | Endpoints | Ubicación(es) | Observaciones |'
    W '|---|---|---|---|---|'
    foreach ($g in $directGroups) {
        $r0 = $g.Group[0]
        $formas = (@($g.Group | ForEach-Object { if ($_.Form) { $_.Form } else { 'directa' } } | Sort-Object -Unique) | ForEach-Object { Esc $_ }) -join '<br>'
        $obs = @()
        if ($migr.ContainsKey($g.Name)) { $obs += '&#9888; También figura como migrado en otro flujo' }
        W ("| {0} | {1} | {2} | {3} | {4} |" -f (Code $r0.Sp.Display), $formas, (EpList $g.Group), (LocList ($g.Group | ForEach-Object { $_.Loc })), ($obs -join '<br>'))
    }
}
W ''

# --- 5. inventario
W '## 5. Inventario consolidado de SP pendientes de migrar'
W ''
W 'Incluye los SP llamados directamente y los SP hijos que todavía no están migrados en el repo. Está ordenado por la cantidad de endpoints que dependen de cada uno, para ayudar a priorizar.'
W ''
if ($pendingList.Count -eq 0) { W '_No hay SP pendientes._' }
else {
    W '| # | SP pendiente | Motivo | N.&ordm; de endpoints | Endpoints |'
    W '|---:|---|---|---:|---|'
    $i = 0
    foreach ($p in $pendingList) {
        $i++
        $eps = @($p.Eps | Sort-Object | ForEach-Object { '`' + $_ + '`' }) -join '<br>'
        W ("| {0} | {1} | {2} | {3} | {4} |" -f $i, (Code $p.Sp.Display), (Esc ((@($p.Why) | Sort-Object) -join '; ')), $p.Eps.Count, $eps)
    }
}
W ''

# --- 6. endpoints sin SP
W '## 6. Endpoints sin SP asociado'
W ''
if ($epNoSp.Count -eq 0) { W '_Todos los endpoints tienen al menos un SP asociado._' }
else {
    W '| Endpoint | Handler | Motivo |'
    W '|---|---|---|'
    foreach ($e in $epNoSp) {
        $why = ''
        if ($res.EndpointNoSpReason.ContainsKey($e.Id)) { $why = $res.EndpointNoSpReason[$e.Id] }
        if ($e.Note) { $why += " ($($e.Note))" }
        W ("| {0} | {1} | {2} |" -f ('`' + $e.Verb + ' ' + $e.Route + '`'), (Esc $e.HandlerName), (Esc $why))
    }
}
W ''

# --- 7. revision manual
W '## 7. Revisión manual'
W ''
W '### 7.1 SP mencionados solo en comentarios'
W ''
if ($soloRows.Count -eq 0) { W '_Ninguno._' }
else {
    W '| Endpoint | SP | Dónde se menciona |'
    W '|---|---|---|'
    foreach ($r in $soloRows) { W ("| {0} | {1} | {2}<br><sub>{3}</sub> |" -f (EpCell $r.Ep), (Code $r.Sp.Display), (LocLink $r.Loc), (Esc $r.Detail)) }
}
W ''
W '### 7.2 Advertencias'
W ''
$warns = @($res.Warnings)
if ($warns.Count -eq 0) { W '_Sin advertencias._' }
else {
    W 'Una "llamada no resuelta" es una invocación a un método del repo que no se pudo conectar sin ambigüedad (por ejemplo, porque varias clases tienen un método con ese nombre y no se pudo deducir el tipo del receptor). Solo se listan las que podrían llevar a un SP. Si es así, ese SP podría faltar en el endpoint.'
    W ''
    W '| Categoría | Detalle | Endpoint | Ubicación |'
    W '|---|---|---|---|'
    foreach ($w in ($warns | Sort-Object Category, EndpointDisplay, Message)) {
        $epTxt = ''
        if ($w.EndpointDisplay) { $epTxt = '`' + $w.EndpointDisplay + '`' }
        W ("| {0} | {1} | {2} | {3} |" -f (Esc $w.Category), (Esc $w.Message), $epTxt, (LocLink $w.Loc))
    }
}
W ''
W '### 7.3 Referencias a SP no vinculadas a ningún endpoint'
W ''
W 'Son SP que aparecen en código al que no se llega desde ningún endpoint detectado: código muerto, jobs en segundo plano o flujos que el análisis no pudo conectar.'
W ''
$orph = @($res.Orphans | Sort-Object Kind, @{ e = { $_.Sp.Key } }, @{ e = { $_.Loc.File.Rel } }, @{ e = { $_.Loc.Line } })
if ($orph.Count -eq 0) { W '_Ninguna._' }
else {
    W '| SP | Tipo de referencia | Contexto | Archivo:línea | Detalle |'
    W '|---|---|---|---|---|'
    foreach ($o in $orph) {
        $kindTxt = 'Comentario'
        if ($o.Kind -eq 'codigo') { $kindTxt = 'Código' }
        W ("| {0} | {1} | {2} | {3} | {4} |" -f (Code $o.Sp.Display), $kindTxt, (Esc $o.Context), (LocLink $o.Loc), (Esc $o.Detail))
    }
}
W ''
if ($res.ParseErrors.Count -gt 0) {
    W '### 7.4 Archivos que no se pudieron leer'
    W ''
    foreach ($pe in $res.ParseErrors) { W ("- {0}" -f (Esc $pe)) }
    W ''
}

if ($null -ne $swaggerInfo) {
    W '## 8. Cruce con Swagger / OpenAPI'
    W ''
    W ("Operaciones en el documento: {0}. Endpoints detectados en el código: {1}." -f $swaggerInfo.Total, $endpoints.Count)
    W ''
    W '**Documentados en Swagger pero no detectados en el código**'
    W ''
    if ($swaggerInfo.MissingInCode.Count -eq 0) { W '_Ninguno._' }
    else {
        foreach ($m in $swaggerInfo.MissingInCode) {
            $opTxt = ''
            if ($m.OpId) { $opTxt = "(operationId: $($m.OpId))" }
            W ("- ``{0} {1}`` {2}" -f $m.Verb, $m.Route, $opTxt)
        }
    }
    W ''
    W '**Detectados en el código pero no documentados en Swagger**'
    W ''
    if ($swaggerInfo.MissingInSwagger.Count -eq 0) { W '_Ninguno._' } else { foreach ($e in $swaggerInfo.MissingInSwagger) { W ("- ``{0} {1}`` ({2})" -f $e.Verb, $e.Route, (Esc $e.HandlerName)) } }
    W ''
}

if ($IncludeTrace) {
    W '## Anexo: árbol de llamadas por endpoint'
    W ''
    W 'Métodos recorridos desde cada endpoint, en orden de visita. `[inferido]` = conexión resuelta solo por nombre.'
    W ''
    foreach ($e in $endpoints) {
        $tr = $null
        if ($res.EndpointTraces.ContainsKey($e.Id)) { $tr = $res.EndpointTraces[$e.Id] }
        W ("<details><summary><code>{0} {1}</code> &mdash; {2}</summary>" -f $e.Verb, [System.Net.WebUtility]::HtmlEncode($e.Route), [System.Net.WebUtility]::HtmlEncode($e.HandlerName))
        W ''
        W '```text'
        if ($null -ne $tr) { foreach ($line in $tr) { W $line } }
        W '```'
        W ''
        W '</details>'
        W ''
    }
}

W '## Metodología y límites'
W ''
W '- El análisis es **estático**: el script lee el código, pero no lo compila ni lo ejecuta. El grafo de llamadas se arma con:'
W '  - los tipos declarados: campos, parámetros, constructores primarios, `var x = new T()` y `foreach`;'
W '  - las implementaciones de interfaces y las clases base;'
W '  - los handlers MediatR/CQRS (`IRequestHandler<T>`, `ICommandHandler<T>`, ...);'
W '  - los métodos de extensión.'
W '- Cuando hay varios comentarios, el SP migrado se asocia a su query con el comentario más cercano. El orden de prioridad es:'
W '  1. comentario SQL dentro de la query;'
W '  2. comentario sobre la constante SQL;'
W '  3. comentario previo en el mismo método, dentro de su bloque `{ }`;'
W '  4. XML doc, comentarios o atributos del método (las menciones en logs, solo si no hay nada de lo anterior);'
W '  5. documentación del método de la interfaz o comentario de un método llamador (&dagger;);'
W '  6. comentario de la clase, solo si nombra un único SP.'
W '- Las queries armadas con `StringBuilder`, `+=`, `string.Format` o `AppendFormat` se unen en una sola query. Los strings que no son comandos se ignoran: comparaciones, valores de parámetros y respuestas HTTP.'
W '- Un comentario que nombra un SP que la misma query ya llama solo documenta esa llamada; no la convierte en migración.'
W '- Exclusiones:'
W '  - las carpetas `bin`, `obj`, `.git`, `node_modules` y `packages`, y las de documentación (`docs`, ...);'
W '  - los proyectos de test y los archivos generados (`*.g.cs`, `*.Designer.cs`);'
W '  - los `.sql` que el código C# no referencia por nombre.'
W '- No se detectan los nombres de SP que se arman en tiempo de ejecución concatenando variables. Si se encuentra un caso así, aparece como advertencia "SP dinámico".'
W ("- Tiempo de análisis: {0:N1} s." -f $sw.Elapsed.TotalSeconds)

$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($OutputPath, $sb.ToString(), $utf8)

# ---------------------------------------------------------------- CSV
if ($ExportCsv) {
    $csvPath = [System.IO.Path]::ChangeExtension($OutputPath, '.csv')
    $csv = foreach ($r in $rows) {
        $fileRel = ''; $line = ''; $qFile = ''; $qLine = ''
        if ($r.Loc -and $r.Loc.File) { $fileRel = $r.Loc.File.Rel; $line = $r.Loc.Line }
        if ($r.QueryLoc -and $r.QueryLoc.File) { $qFile = $r.QueryLoc.File.Rel; $qLine = $r.QueryLoc.Line }
        [pscustomobject]@{
            Verbo        = $r.Ep.Verb
            Ruta         = $r.Ep.Route
            Handler      = $r.Ep.HandlerName
            SP           = $r.Sp.Display
            SpHijo       = $r.ChildChain
            Tipo         = $r.Tipo
            EstadoHijo   = $r.ChildStatus
            Nivel        = $r.Level
            Forma        = $r.Form
            Archivo      = $fileRel
            Linea        = $line
            QueryArchivo = $qFile
            QueryLinea   = $qLine
            Inferido     = $r.Inferred
            Detalle      = $r.Detail
            Traza        = $r.Trace
        }
    }
    $csvText = ($csv | ConvertTo-Csv -NoTypeInformation -Delimiter ';') -join "`r`n"
    [System.IO.File]::WriteAllText($csvPath, $csvText, (New-Object System.Text.UTF8Encoding($true)))
    Write-Host "  CSV         : $csvPath"
}

$sw.Stop()
Write-Host ("Listo en {0:N1} s: {1} endpoints, {2} filas, {3} SP pendientes, {4} listos, {5} con hijos." -f $sw.Elapsed.TotalSeconds, $endpoints.Count, $rows.Count, $pendingList.Count, $readyKeys.Count, $withChildKeys.Count) -ForegroundColor Green
Write-Host "  Reporte     : $OutputPath"
