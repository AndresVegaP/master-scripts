<#
.SYNOPSIS
    Mapea, por cada endpoint de una API .NET + Dapper, los SP de Oracle (PCK_xxx.SP_xxx) que usa:
    llamados directamente, migrados a query (listos) y migrados con SP hijos pendientes.

.DESCRIPTION
    Analiza estaticamente el codigo C# del repositorio (sin compilarlo ni ejecutarlo):
      1. Detecta los endpoints (controllers ASP.NET Core / Web API 2, Minimal APIs con MapGroup,
         Carter, FastEndpoints y Azure Functions HTTP).
      2. Recorre el grafo de llamadas desde cada endpoint (servicios, repositorios, interfaces ->
         implementaciones, handlers MediatR/CQRS, clases base, metodos de extension, constantes,
         archivos .sql incrustados, .resx y appsettings).
      3. Clasifica cada SP encontrado:
           - Llamado directamente (CommandType.StoredProcedure, BEGIN ... END;, CALL, SELECT FN() FROM DUAL)
           - Llamado directamente dentro de una query sin SP migrado documentado
           - Migrado sin hijos (listo): un comentario nombra el SP y la query que lo reemplaza no llama SPs
           - SP hijo: la query migrada todavia llama a otros SP (pendientes), incluso de forma anidada
           - Solo en comentario: mencionado en el flujo pero sin codigo que lo respalde (revision manual)
      4. Genera un .md con la tabla principal (una fila por SP), tablas por tipo, inventario de
         pendientes, endpoints sin SP, advertencias y referencias no vinculadas a endpoints.

    Compatible con Windows PowerShell 5.1 y PowerShell 7+. No requiere dependencias externas.

.PARAMETER RepoPath
    Carpeta a analizar (raiz del repo o una subcarpeta, p.ej. repo\carpeta-api\src).
    Por defecto: la carpeta donde esta el script.

.PARAMETER OutputPath
    Ruta del .md de salida (o carpeta donde crearlo). Por defecto: <carpeta del script>\reporte-sp-endpoints.md

.PARAMETER PackagePrefixes
    Prefijos de package Oracle. Por defecto: PCK_, PKG_

.PARAMETER ObjectPrefixes
    Prefijos de procedimientos/funciones sueltos (sin package). Por defecto: SP_, FN_, PRC_

.PARAMETER ExcludePath
    Patrones comodin adicionales de carpetas/archivos a excluir (relativos al repo), p.ej. 'src/Legacy/*','*Migrations*'.

.PARAMETER IncludeTests
    Incluye los proyectos de test (por defecto se excluyen).

.PARAMETER ExportCsv
    Ademas del .md, genera un .csv (mismo nombre) con todas las filas y su traza de llamadas.

.PARAMETER IncludeTrace
    Agrega al .md un anexo con el arbol de llamadas recorrido por cada endpoint.

.PARAMETER SwaggerPath
    (Opcional) swagger.json / openapi.json (archivo o URL) para cruzar los endpoints documentados
    con los detectados en el codigo.

.PARAMETER MaxDepth
    Profundidad maxima del recorrido de llamadas. Por defecto 40.

.EXAMPLE
    .\Analizar-SpEndpoints.ps1
    Analiza la carpeta del script y deja reporte-sp-endpoints.md junto al script.

.EXAMPLE
    .\Analizar-SpEndpoints.ps1 -RepoPath ..\..\carpeta-api\src -OutputPath ..\reportes\sp.md -ExportCsv
#>
[CmdletBinding()]
param(
    [Alias('Repo', 'Ruta', 'Path')]
    [string]$RepoPath,

    [Alias('Salida', 'Output', 'Out')]
    [string]$OutputPath,

    [string[]]$PackagePrefixes = @('PCK_', 'PKG_'),

    [string[]]$ObjectPrefixes = @('SP_', 'FN_', 'PRC_'),

    [string[]]$ExcludePath = @(),

    [switch]$IncludeTests,

    [switch]$ExportCsv,

    [switch]$IncludeTrace,

    [string]$SwaggerPath,

    [ValidateRange(1, 200)]
    [int]$MaxDepth = 40
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# Con "powershell -File", una lista 'A','B' llega como un unico texto "A,B": se separa aqui.
function Split-ListParam([string[]]$values) {
    $r = New-Object System.Collections.Generic.List[string]
    foreach ($v in @($values)) {
        if ($null -eq $v) { continue }
        foreach ($p in ($v -split ',')) {
            $x = $p.Trim().Trim("'").Trim('"').Trim()
            if ($x.Length -gt 0) { $r.Add($x) }
        }
    }
    return [string[]]$r.ToArray()
}
$PackagePrefixes = Split-ListParam $PackagePrefixes
$ObjectPrefixes = Split-ListParam $ObjectPrefixes
$ExcludePath = Split-ListParam $ExcludePath
foreach ($p in @($PackagePrefixes) + @($ObjectPrefixes)) {
    if ($p -notmatch '^[A-Za-z][A-Za-z0-9_$#]*$') { throw "Prefijo no valido: '$p'. Use solo letras, numeros y '_' (por ejemplo PCK_ o SP_)." }
}
if (@($PackagePrefixes).Count -eq 0) { $PackagePrefixes = @('PCK_', 'PKG_') }
if (@($ObjectPrefixes).Count -eq 0) { $ObjectPrefixes = @('SP_', 'FN_', 'PRC_') }
$ScriptVersion = '1.0.0'
$sw = [System.Diagnostics.Stopwatch]::StartNew()

# ---------------------------------------------------------------- rutas por defecto
$scriptDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptDir)) { $scriptDir = (Get-Location).Path }
if ([string]::IsNullOrWhiteSpace($RepoPath)) { $RepoPath = $scriptDir }
if (-not [System.IO.Path]::IsPathRooted($RepoPath)) { $RepoPath = Join-Path (Get-Location).Path $RepoPath }
$RepoPath = [System.IO.Path]::GetFullPath($RepoPath).TrimEnd('\', '/')
if (-not (Test-Path -LiteralPath $RepoPath -PathType Container)) { throw "La carpeta a analizar no existe: $RepoPath" }

if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $scriptDir 'reporte-sp-endpoints.md' }
if (-not [System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath = Join-Path (Get-Location).Path $OutputPath }
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if ((Test-Path -LiteralPath $OutputPath -PathType Container) -or $OutputPath.EndsWith('\') -or $OutputPath.EndsWith('/') -or [string]::IsNullOrEmpty([System.IO.Path]::GetExtension($OutputPath))) {
    $OutputPath = Join-Path $OutputPath 'reporte-sp-endpoints.md'
}
if ([System.IO.Path]::GetExtension($OutputPath) -ne '.md') {
    # el reporte siempre es .md (el CSV opcional se escribe al lado con el mismo nombre)
    $newOut = [System.IO.Path]::ChangeExtension($OutputPath, '.md')
    Write-Warning "La salida debe ser un .md: se usará '$newOut'."
    $OutputPath = $newOut
}
$outDir = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') {
    throw "PowerShell esta en modo '$($ExecutionContext.SessionState.LanguageMode)'. El script necesita FullLanguage para compilar su motor de analisis (Add-Type)."
}

Write-Host "Analizar-SpEndpoints v$ScriptVersion" -ForegroundColor Cyan
Write-Host "  Repositorio : $RepoPath"
Write-Host "  Salida      : $OutputPath"

# ---------------------------------------------------------------- descubrimiento de archivos
$excludedDirNames = @('bin', 'obj', '.git', '.vs', '.vscode', '.idea', 'node_modules', 'TestResults', '.github', '.gitlab', '.azuredevops', '.claude', '.config')
# solo se excluyen si estan en la raiz del repo (en otro nivel pueden ser modulos de codigo)
$excludedRootNames = @('packages', 'artifacts')
# carpetas de documentacion: se ignoran .sql/.resx/.json (analisis, scripts de BD) pero se leen los .cs (pueden ser modulos "Docs")
$docDirNames = @('docs', 'doc', 'documentation', 'documentacion', "documentaci$([char]0x00F3)n")

function Test-Excluded([string]$relPath) {
    foreach ($p in $ExcludePath) {
        if ([string]::IsNullOrWhiteSpace($p)) { continue }
        $pp = $p.Replace('\', '/').TrimEnd('/')
        if ($pp.Length -eq 0) { continue }
        if ($relPath -like $pp -or $relPath -like "$pp/*" -or $relPath -like "*/$pp" -or $relPath -like "*/$pp/*") { return $true }
    }
    return $false
}

function Get-RelPath([string]$full) {
    $r = $full.Substring($RepoPath.Length).TrimStart('\', '/')
    return $r.Replace('\', '/')
}

$csFiles = New-Object System.Collections.Generic.List[string]
$sqlFiles = New-Object System.Collections.Generic.List[string]
$resxFiles = New-Object System.Collections.Generic.List[string]
$configFiles = New-Object System.Collections.Generic.List[string]
$csprojFiles = New-Object System.Collections.Generic.List[string]
$skippedDirs = New-Object System.Collections.Generic.List[string]

$stack = New-Object System.Collections.Generic.Stack[object]
$visitedDirs = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$stack.Push(@($RepoPath, $false))
while ($stack.Count -gt 0) {
    $item = $stack.Pop()
    $dir = [string]$item[0]
    $inDocs = [bool]$item[1]
    # OneDrive marca las carpetas como reparse points: no se saltan, pero se evita recorrer dos veces
    # la misma carpeta y los ciclos de enlaces con un limite de profundidad
    if (-not $visitedDirs.Add($dir)) { continue }
    if (($dir.Length - $RepoPath.Length) -gt 2000 -or ($dir.Split('\').Count - $RepoPath.Split('\').Count) -gt 60) { continue }
    try {
        foreach ($d in [System.IO.Directory]::EnumerateDirectories($dir)) {
            $name = [System.IO.Path]::GetFileName($d)
            $rel = Get-RelPath $d
            $atRoot = -not $rel.Contains('/')
            if ($excludedDirNames -contains $name -or ($atRoot -and $excludedRootNames -contains $name) -or (Test-Excluded $rel)) { $skippedDirs.Add($rel); continue }
            $stack.Push(@($d, ($inDocs -or ($docDirNames -contains $name))))
        }
        foreach ($f in [System.IO.Directory]::EnumerateFiles($dir)) {
            $rel = Get-RelPath $f
            if (Test-Excluded $rel) { continue }
            $ext = [System.IO.Path]::GetExtension($f).ToLowerInvariant()
            $fn = [System.IO.Path]::GetFileName($f)
            switch ($ext) {
                '.cs' {
                    if ($fn -match '(?i)\.(g|g\.i|designer|generated|AssemblyInfo|AssemblyAttributes)\.cs$' -or $fn -match '(?i)^(AssemblyInfo|GlobalUsings\.g)\.cs$') { continue }
                    $csFiles.Add($f)
                }
                '.sql' { if (-not $inDocs) { $sqlFiles.Add($f) } }
                '.resx' { if (-not $inDocs) { $resxFiles.Add($f) } }
                '.json' { if (-not $inDocs -and $fn -like 'appsettings*.json') { $configFiles.Add($f) } }
                '.csproj' { $csprojFiles.Add($f) }
            }
        }
    }
    catch {
        Write-Warning "No se pudo leer la carpeta '$dir': $($_.Exception.Message)"
    }
}

# ---------------------------------------------------------------- proyectos de test
$testDirs = New-Object System.Collections.Generic.List[string]
if (-not $IncludeTests) {
    foreach ($p in $csprojFiles) {
        $isTest = $false
        try {
            $txt = [System.IO.File]::ReadAllText($p)
            if ($txt -match '(?i)<IsTestProject>\s*true' -or $txt -match '(?i)Include="(Microsoft\.NET\.Test\.Sdk|xunit[\w.]*|NUnit[\w.]*|MSTest\.[\w.]+|TngTech\.ArchUnitNET[\w.]*)"') { $isTest = $true }
        }
        catch { }
        if (-not $isTest -and [System.IO.Path]::GetFileNameWithoutExtension($p) -match '(?i)(\.|^)(Unit|Integration|Functional|Acceptance|Architecture|E2E)?Tests?$') { $isTest = $true }
        if ($isTest) { $testDirs.Add(([System.IO.Path]::GetDirectoryName($p)).TrimEnd('\') + '\') }
    }
    function Test-InTestDir([string]$f) {
        foreach ($t in $testDirs) { if ($f.StartsWith($t, [System.StringComparison]::OrdinalIgnoreCase)) { return $true } }
        $rel = Get-RelPath $f
        if ($rel -match '(?i)(^|/)(tests?|[\w.]+\.(Unit|Integration)?Tests?)/') {
            # carpeta de tests sin csproj propio
            return $true
        }
        return $false
    }
    $before = $csFiles.Count
    $kept = New-Object System.Collections.Generic.List[string]
    foreach ($f in $csFiles) { if (-not (Test-InTestDir $f)) { $kept.Add($f) } }
    $csFiles = $kept
    $excludedTests = $before - $csFiles.Count
}
else { $excludedTests = 0 }

Write-Host ("  Archivos    : {0} .cs, {1} .sql, {2} .resx, {3} appsettings  (excluidos {4} .cs de tests)" -f $csFiles.Count, $sqlFiles.Count, $resxFiles.Count, $configFiles.Count, $excludedTests)
if ($csFiles.Count -eq 0) { Write-Warning "No se encontraron archivos .cs para analizar en $RepoPath" }

# ---------------------------------------------------------------- motor de analisis (C# embebido)
$engineSource = @'
#__ENGINE_SOURCE__#
'@
