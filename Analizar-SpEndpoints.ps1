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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SPA_NS
{
    // =====================================================================
    //  MODELO
    // =====================================================================

    public class AnalyzerOptions
    {
        public string RepoRoot;
        public string[] CsFiles = new string[0];
        public string[] SqlFiles = new string[0];
        public string[] ResxFiles = new string[0];
        public string[] ConfigFiles = new string[0];
        public string[] PackagePrefixes = new string[] { "PCK_", "PKG_" };
        public string[] ObjectPrefixes = new string[] { "SP_", "FN_", "PRC_" };
        public int MaxDepth = 40;
        public int MaxNestedLevels = 6;
    }

    public enum TokKind { Ident, Number, Str, Chr, Punct }

    public class StrPart
    {
        public bool IsHole;
        public string Text;      // texto literal o expresion del hueco
    }

    public class StrLit
    {
        public List<StrPart> Parts = new List<StrPart>();
        public bool CountsLines;   // verbatim/raw: los saltos de linea del valor coinciden con el fuente
        public bool Interpolated;
        public string PlainValue()
        {
            var sb = new StringBuilder();
            foreach (var p in Parts) { if (p.IsHole) sb.Append("{?}"); else sb.Append(p.Text); }
            return sb.ToString();
        }
    }

    public class Token
    {
        public TokKind Kind;
        public int Start, End, Line, EndLine;
        public string Text;
        public StrLit Lit;
        public override string ToString() { return Kind + ":" + Text; }
    }

    public class Comment
    {
        public int Start, End, Line, EndLine;
        public string Text;
        public bool IsDoc, IsPreproc;
    }

    public class SourceFile
    {
        public string Path, Rel, Text;
        public int[] LineStarts;
        public List<Token> Toks = new List<Token>();
        public int[] Match;
        public List<Comment> Comments = new List<Comment>();
        public List<string> Usings = new List<string>();
        public Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal);   // using Alias = A.B.Tipo;
        public List<string> GlobalUsings = new List<string>();
        public Dictionary<string, string> GlobalAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        public bool IsSql;

        public int LineOf(int offset)
        {
            int lo = 0, hi = LineStarts.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (LineStarts[mid] <= offset) lo = mid; else hi = mid - 1;
            }
            return lo + 1;
        }

        public static int[] ComputeLineStarts(string text)
        {
            var l = new List<int>(); l.Add(0);
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') l.Add(i + 1);
            return l.ToArray();
        }

        // comentarios cuyo inicio esta en [from, to)
        public List<Comment> CommentsBetween(int from, int to)
        {
            var r = new List<Comment>();
            int lo = 0, hi = Comments.Count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (Comments[mid].Start < from) lo = mid + 1; else hi = mid; }
            for (int i = lo; i < Comments.Count && Comments[i].Start < to; i++) r.Add(Comments[i]);
            return r;
        }
    }

    public class TypeRef
    {
        public string Name;               // ultimo segmento sin genericos
        public string Qualified;          // texto completo (sin genericos)
        public List<TypeRef> Args = new List<TypeRef>();
        public override string ToString()
        {
            if (Args.Count == 0) return Name;
            return Name + "<" + string.Join(",", Args.Select(a => a.ToString()).ToArray()) + ">";
        }
    }

    public class ArgRef
    {
        public SourceFile File;
        public int S, E;   // rango de tokens [S, E)
    }

    public class AttrInfo
    {
        public SourceFile File;
        public List<ArgRef> PosArgs = new List<ArgRef>();
        public Dictionary<string, ArgRef> NamedArgs = new Dictionary<string, ArgRef>(StringComparer.OrdinalIgnoreCase);
        public string Name;
        public List<string> Positional = new List<string>();   // valor string si es literal, si no el texto crudo
        public List<bool> PositionalIsString = new List<bool>();
        public Dictionary<string, string> Named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> AllStrings = new List<string>();
        public int Line;
        public int StartOffset;
    }

    public class ParamInfo
    {
        public TypeRef Type;
        public string Name;
        public List<AttrInfo> Attrs = new List<AttrInfo>();
        public bool HasDefault, IsParams, IsThis;
    }

    public class MemberVar
    {
        public string Name;
        public TypeRef Type;
        public bool IsConst, IsStatic, IsReadonly, IsProperty, IsExprBody;
        public int InitStart = -1, InitEnd = -1;  // rango de tokens del inicializador
        public SourceFile File;
        public int Line;
        public int DeclStartOffset;
        public TypeDecl Owner;
        public List<Comment> Leading = new List<Comment>();
        public string Id;
    }

    public class TypeDecl
    {
        public string Name, Namespace, FullName, Kind;
        public SourceFile File;
        public int Line;
        public List<TypeRef> Bases = new List<TypeRef>();
        public List<AttrInfo> Attrs = new List<AttrInfo>();
        public bool IsAbstract, IsStatic, IsPartial, IsPublic;
        public List<string> TypeParams = new List<string>();
        public List<ArgRef> BaseCtorArgs = new List<ArgRef>();
        public TypeDecl Outer;
        public List<MethodDecl> Methods = new List<MethodDecl>();
        public Dictionary<string, MemberVar> Members = new Dictionary<string, MemberVar>(StringComparer.Ordinal);
        public List<ParamInfo> PrimaryCtor;
        public List<Comment> Leading = new List<Comment>();
        public List<SourceFile> Files = new List<SourceFile>();
        public List<string> Usings = new List<string>();
        public Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        public List<TypeRef> BaseCtorArgsOwner = new List<TypeRef>();
        public List<string> BaseCtorStrings = new List<string>();   // p.ej. CarterModule("/api/x")
        public TypeDecl MergedInto;
        public string Id;
        public override string ToString() { return FullName; }
    }

    public class TokRange { public int S, E; public TokRange(int s, int e) { S = s; E = e; } }

    public class MethodDecl
    {
        public string Name;
        public TypeDecl Owner;
        public SourceFile File;
        public int Line;
        public List<ParamInfo> Params = new List<ParamInfo>();
        public TypeRef ReturnType;
        public List<AttrInfo> Attrs = new List<AttrInfo>();
        public bool IsStatic, IsPublic, IsAbstract, IsVirtual, IsOverride, IsCtor, IsExtension, IsSynthetic, IsLocalFunction;
        public List<string> TypeParams = new List<string>();
        public MethodDecl Parent;                                   // metodo contenedor (funciones locales y lambdas)
        public List<MethodDecl> LocalFunctions = new List<MethodDecl>();
        public string AccessorKind;                                 // get / set / init / add / remove (cuerpos de propiedades, indexadores y eventos)
        public string ExplicitIface;                                // implementacion explicita: IFoo.Metodo
        public int BodyStart = -1, BodyEnd = -1;   // tokens [BodyStart, BodyEnd)
        public List<TokRange> Excluded = new List<TokRange>();
        public List<Comment> Leading = new List<Comment>();
        public int DeclStartOffset, DeclEndOffset;
        public string Id;
        public string DisplayName { get { return (Owner != null ? Owner.Name + "." : "") + Name; } }
        public bool HasBody { get { return BodyStart >= 0 && BodyEnd >= BodyStart; } }
        public override string ToString() { return DisplayName; }
    }

    // ----------------------------------------------------------------- SP

    public class SpName
    {
        public string Key;      // PAQUETE.MIEMBRO o MIEMBRO (mayusculas, sin esquema)
        public string Package;  // puede ser null
        public string Member;
        public string Schema;
        public string DbLink;
        public string Display { get { return Key; } }
        public bool SameAs(SpName o)
        {
            if (o == null) return false;
            if (!string.Equals(Member, o.Member, StringComparison.OrdinalIgnoreCase)) return false;
            if (Package == null || o.Package == null) return true;
            return string.Equals(Package, o.Package, StringComparison.OrdinalIgnoreCase);
        }
        public override string ToString() { return Display; }
    }

    public class Location
    {
        public SourceFile File;
        public int Line;
        public string Note;
        public Location(SourceFile f, int line) { File = f; Line = line; }
        public string Key { get { return (File != null ? File.Rel : "?") + ":" + Line; } }
    }

    public class Marker
    {
        public SpName Sp;
        public Location Loc;
        public string Source;    // sql-comment, const-comment, body-comment, method-comment, attribute, log, class-comment
        public string Excerpt;
        public string Id;
    }

    public class MarkerBlock
    {
        public List<Marker> Markers = new List<Marker>();
        public int Start, End, Line, EndLine;
        public bool Scoped;
        public int ScopeOpen = -1, ScopeClose = -1;   // bloque { } que contiene al comentario
        public string Id;
    }

    public class SpHit
    {
        public SpName Sp;
        public int Offset;        // dentro del valor del fragmento
        public Location Loc;
        public bool ViaConst;
    }

    public class FragPiece
    {
        public int ValueStart, ValueLength;
        public SourceFile File;
        public int Line;
        public bool CountsLines;
        public MemberVar Const;          // si viene de una constante
        public string ExternalKind;      // sqlfile / resx / config
    }

    public class Fragment
    {
        public string Id;
        public string Value = "";
        public List<FragPiece> Pieces = new List<FragPiece>();
        public MethodDecl Method;        // metodo donde se usa (null si es constante huerfana)
        public SourceFile File;          // archivo del uso
        public int Line, EndLine;        // linea del uso
        public int StartOffset, EndOffset;
        public int TokStart, TokEnd;
        public bool MessageContext;
        public bool SpContext;           // se usa como nombre de SP (CommandType.StoredProcedure / wrapper)
        public bool OnlyConstRef;        // el fragmento es solo una referencia a constante/recurso
        public MemberVar ConstRef;       // constante referenciada (si OnlyConstRef)
        public string Origin;            // literal / const / sqlfile / resx / config
        public List<Marker> InlineMarkers = new List<Marker>();
        public List<Marker> ConstMarkers = new List<Marker>();
        public MarkerBlock Block;
        public List<SpHit> Calls = new List<SpHit>();
        public string Kind;              // bare / pure / regular / other / empty
        public bool HasDynamicSp;
        public string Form;              // forma de invocacion (para llamadas directas)
        public bool Ignored;             // el string no es un comando (comparacion, valor de parametro, respuesta HTTP)
        public string GroupVar;          // StringBuilder o variable acumulada a la que pertenece la pieza
        public bool GroupStart;          // asignacion simple/declaracion: inicia un valor nuevo (no se concatena con el anterior)
        public bool GroupAppendLine;

        public Location LocOfValueOffset(int off)
        {
            FragPiece best = null;
            foreach (var p in Pieces) { if (off >= p.ValueStart && off < p.ValueStart + p.ValueLength) { best = p; break; } }
            if (best == null) foreach (var p in Pieces) if (p.ValueStart <= off) best = p;
            if (best == null) return new Location(File, Line);
            int line = best.Line;
            if (best.CountsLines)
            {
                int end = Math.Min(off, Value.Length);
                for (int i = best.ValueStart; i < end; i++) if (Value[i] == '\n') line++;
            }
            var loc = new Location(best.File, line);
            return loc;
        }
    }

    // ------------------------------------------------------------ Endpoints

    public class Endpoint
    {
        public string Verb, Route, Kind, HandlerName, OperationName;
        public MethodDecl Handler;
        public SourceFile File;
        public int Line;
        public List<Marker> ExtraMarkers = new List<Marker>();
        public string Note;
        public string Display { get { return Verb + " " + Route; } }
        public TypeDecl ViaType;   // tipo concreto del handler (controller derivado que hereda la accion)
        public string Id;
    }

    public class ResultRow
    {
        public Endpoint Ep;
        public SpName Sp;
        public SpName Child;
        public string ChildChain;     // "HIJO -> NIETO"
        public int Level;             // 0 = fila propia, 1 = hijo, 2 = nieto...
        public string Tipo;           // DIRECTO, DIRECTO_EN_QUERY, MIGRADO_LISTO, HIJO, SOLO_COMENTARIO
        public string ChildStatus;    // PENDIENTE / MIGRADO_EN_REPO
        public Location Loc;
        public Location QueryLoc;
        public string Detail;
        public bool Inferred;
        public string Trace;
        public string Origin;
        public string Form;
        public string SortKey()
        {
            return (Ep != null ? Ep.Route + " " + Ep.Verb : "") + "|" + (Sp != null ? Sp.Key : "") + "|" + (ChildChain ?? "");
        }
    }

    public class Warn
    {
        public string Category, Message;
        public Location Loc;
        public string EndpointDisplay;
    }

    public class OrphanRef
    {
        public SpName Sp;
        public Location Loc;
        public string Context;     // metodo/clase
        public string Kind;        // codigo / comentario
        public string Detail;
    }

    public class MigratedInfo
    {
        public SpName Sp;
        public Location MarkerLoc;
        public Location QueryLoc;
        public List<SpHit> Children = new List<SpHit>();
    }

    public class AnalysisResult
    {
        public List<Endpoint> Endpoints = new List<Endpoint>();
        public List<ResultRow> Rows = new List<ResultRow>();
        public List<Warn> Warnings = new List<Warn>();
        public List<OrphanRef> Orphans = new List<OrphanRef>();
        public Dictionary<string, List<MigratedInfo>> MigratedCatalog = new Dictionary<string, List<MigratedInfo>>();
        public Dictionary<string, string> EndpointNoSpReason = new Dictionary<string, string>();
        public Dictionary<string, List<string>> EndpointTraces = new Dictionary<string, List<string>>();
        public int FilesCs, FilesSql, Types, Methods;
        public List<string> ParseErrors = new List<string>();
        public List<string> EncodingNotes = new List<string>();   // archivos leidos como Windows-1252
        public string ConventionalTemplate;
    }

    // ------------------------------------------------------------ Utils

    public static class U
    {
        static readonly HashSet<string> Kw = new HashSet<string>(new string[] {
            "if","else","for","foreach","while","do","switch","case","catch","using","lock","return","nameof","typeof","sizeof",
            "default","checked","unchecked","fixed","when","new","throw","await","yield","in","is","as","var","base","this",
            "try","finally","goto","break","continue","out","ref","params","stackalloc","delegate","operator","get","set","init",
            "add","remove","where","select","from","orderby","group","into","let","join","on","equals","by","ascending","descending",
            "static","async","not","and","or","with","true","false","null","void","object","string","int","long","bool","decimal",
            "double","float","char","byte","short","uint","ulong","ushort","sbyte","dynamic","event","implicit","explicit","sealed",
            "readonly","const","volatile","unsafe","extern","internal","public","private","protected","abstract","virtual","override",
            "partial","class","struct","interface","enum","record","namespace","global","required","scoped","file","managed","unmanaged"
        });
        public static bool IsKeyword(string s) { return Kw.Contains(s); }

        static readonly HashSet<string> Mods = new HashSet<string>(new string[] {
            "public","private","protected","internal","static","abstract","sealed","partial","readonly","async","virtual",
            "override","new","extern","unsafe","volatile","const","required","file","ref"
        });
        public static bool IsModifier(string s) { return Mods.Contains(s); }

        public static string Upper(string s) { return s == null ? null : s.ToUpperInvariant(); }

        public static string StripQuotes(string s)
        {
            if (s == null) return null;
            return s.Replace("\"", "").Trim();
        }

        public static string OneLine(string s, int max)
        {
            if (s == null) return "";
            s = Regex.Replace(s, @"\s+", " ").Trim();
            if (s.Length > max) s = s.Substring(0, max) + "...";
            return s;
        }

        public static string Unwrap(TypeRef t)
        {
            if (t == null) return null;
            if ((t.Name == "Task" || t.Name == "ValueTask" || t.Name == "ActionResult" || t.Name == "Nullable") && t.Args.Count == 1) return Unwrap(t.Args[0]);
            return t.Name;
        }

        public static TypeRef UnwrapRef(TypeRef t)
        {
            if (t == null) return null;
            if ((t.Name == "Task" || t.Name == "ValueTask" || t.Name == "Nullable" || t.Name == "Lazy" || t.Name == "IOptions" || t.Name == "IOptionsMonitor" || t.Name == "IOptionsSnapshot" || t.Name == "ActionResult") && t.Args.Count == 1) return UnwrapRef(t.Args[0]);
            return t;
        }
    }
}

namespace SPA_NS
{
    // =====================================================================
    //  LEXER C#
    // =====================================================================
    public static class Lexer
    {
        static readonly string[] Punct3 = new string[] { "??=", "<<=" };
        static readonly string[] Punct2 = new string[] { "=>", "?.", "??", "::", "==", "!=", "&&", "||", "++", "--", "+=", "-=", "*=", "/=", "<=", "->", "%=", "&=", "|=", "^=" };

        public static void Lex(SourceFile f)
        {
            string s = f.Text;
            int n = s.Length;
            int i = 0;
            bool lineStart = true;
            var toks = f.Toks;
            var comments = f.Comments;
            while (i < n)
            {
                char c = s[i];
                if (c == '\n') { lineStart = true; i++; continue; }
                if (char.IsWhiteSpace(c) || c == '﻿') { i++; continue; }
                if (c == '#' && lineStart)
                {
                    int e = Eol(s, i);
                    AddComment(f, i, e, false, true);
                    i = e; continue;
                }
                lineStart = false;
                if (c == '/' && i + 1 < n && s[i + 1] == '/')
                {
                    int e = Eol(s, i);
                    bool doc = i + 2 < n && s[i + 2] == '/' && !(i + 3 < n && s[i + 3] == '/');
                    AddComment(f, i, e, doc, false);
                    i = e; continue;
                }
                if (c == '/' && i + 1 < n && s[i + 1] == '*')
                {
                    int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    e = e < 0 ? n : e + 2;
                    AddComment(f, i, e, i + 2 < n && s[i + 2] == '*', false);
                    i = e; continue;
                }
                if (c == '"' || ((c == '$' || c == '@') && i + 1 < n && (s[i + 1] == '"' || s[i + 1] == '$' || s[i + 1] == '@')))
                {
                    int e;
                    StrLit lit = LexString(s, i, out e);
                    if (lit != null)
                    {
                        var t = new Token { Kind = TokKind.Str, Start = i, End = e, Lit = lit };
                        t.Text = lit.PlainValue();
                        // sufijo u8
                        if (e + 1 < n && (s[e] == 'u' || s[e] == 'U') && s[e + 1] == '8') { e += 2; t.End = e; }
                        toks.Add(t);
                        i = e; continue;
                    }
                }
                if (c == '\'')
                {
                    int j = i + 1;
                    while (j < n && s[j] != '\'' && s[j] != '\n') { if (s[j] == '\\') j++; j++; }
                    int e = Math.Min(n, j + 1);
                    toks.Add(new Token { Kind = TokKind.Chr, Start = i, End = e, Text = s.Substring(i, e - i) });
                    i = e; continue;
                }
                if (IsIdStart(c) || (c == '@' && i + 1 < n && IsIdStart(s[i + 1])))
                {
                    int st = i;
                    if (c == '@') i++;
                    int j = i;
                    while (j < n && IsIdPart(s[j])) j++;
                    toks.Add(new Token { Kind = TokKind.Ident, Start = st, End = j, Text = s.Substring(i, j - i) });
                    i = j; continue;
                }
                if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(s[i + 1])))
                {
                    int j = i + 1;
                    while (j < n)
                    {
                        char d = s[j];
                        if (char.IsLetterOrDigit(d) || d == '_') { j++; continue; }
                        if (d == '.' && j + 1 < n && char.IsDigit(s[j + 1]) && s[j - 1] != '.') { j++; continue; }
                        break;
                    }
                    toks.Add(new Token { Kind = TokKind.Number, Start = i, End = j, Text = s.Substring(i, j - i) });
                    i = j; continue;
                }
                string p = null;
                foreach (var p3 in Punct3) if (string.CompareOrdinal(s, i, p3, 0, 3) == 0) { p = p3; break; }
                if (p == null) foreach (var p2 in Punct2) if (i + 1 < n && string.CompareOrdinal(s, i, p2, 0, 2) == 0) { p = p2; break; }
                if (p == null) p = c.ToString();
                toks.Add(new Token { Kind = TokKind.Punct, Start = i, End = i + p.Length, Text = p });
                i += p.Length;
            }
            ApplyPreprocessor(f);
            foreach (var t in f.Toks) { t.Line = f.LineOf(t.Start); t.EndLine = f.LineOf(Math.Max(t.Start, t.End - 1)); }
            ComputeMatch(f);
        }

        class PpBranch { public int Start, End; public bool? Cond; public bool IsElse; }

        // #if/#elif/#else/#endif: descarta ramas estaticamente falsas y, si las ramas desconocidas
        // desbalancean las llaves, conserva una sola (la del #else si existe) para no romper la estructura.
        static void ApplyPreprocessor(SourceFile f)
        {
            var dirs = f.Comments.Where(c => c.IsPreproc).ToList();
            if (!dirs.Any(d => Regex.IsMatch(d.Text, @"^\s*#\s*if\b"))) return;
            var defined = new HashSet<string>(StringComparer.Ordinal);
            var undefined = new HashSet<string>(StringComparer.Ordinal);
            var stack = new List<List<PpBranch>>();
            var drops = new List<int[]>();
            foreach (var d in dirs)
            {
                var mm = Regex.Match(d.Text, @"^\s*#\s*(\w+)\s*(.*)$");
                if (!mm.Success) continue;
                string kw = mm.Groups[1].Value, rest = Regex.Replace(mm.Groups[2].Value, @"//.*$", "").Trim();
                switch (kw)
                {
                    case "define": defined.Add(rest); undefined.Remove(rest); break;
                    case "undef": undefined.Add(rest); defined.Remove(rest); break;
                    case "if":
                        stack.Add(new List<PpBranch> { new PpBranch { Start = d.End, End = -1, Cond = EvalPp(rest, defined, undefined) } });
                        break;
                    case "elif":
                    case "else":
                        if (stack.Count == 0) break;
                        var g = stack[stack.Count - 1];
                        g[g.Count - 1].End = d.Start;
                        g.Add(new PpBranch { Start = d.End, End = -1, Cond = kw == "elif" ? EvalPp(rest, defined, undefined) : null, IsElse = kw == "else" });
                        break;
                    case "endif":
                        if (stack.Count == 0) break;
                        var grp = stack[stack.Count - 1];
                        stack.RemoveAt(stack.Count - 1);
                        grp[grp.Count - 1].End = d.Start;
                        DecideGroup(f, grp, drops);
                        break;
                }
            }
            if (drops.Count == 0) return;
            Func<int, bool> dropped = off => drops.Any(r => off >= r[0] && off < r[1]);
            f.Toks = f.Toks.Where(t => !dropped(t.Start)).ToList();
            f.Comments = f.Comments.Where(c => c.IsPreproc || !dropped(c.Start)).ToList();
        }

        static void DecideGroup(SourceFile f, List<PpBranch> grp, List<int[]> drops)
        {
            var keep = new List<PpBranch>();
            bool priorTrue = false, allPriorFalse = true;
            foreach (var b in grp)
            {
                bool? c = b.IsElse ? (priorTrue ? (bool?)false : (allPriorFalse ? (bool?)true : null)) : b.Cond;
                if (priorTrue) c = false;
                if (c == true) { priorTrue = true; keep.Clear(); keep.Add(b); continue; }
                if (c == null) { allPriorFalse = false; keep.Add(b); }
            }
            if (!priorTrue && keep.Count > 1)
            {
                // ramas desconocidas: si alguna desbalancea las llaves, se conserva una sola
                bool unbalanced = keep.Any(b => BraceDelta(f, b) != 0);
                if (unbalanced)
                {
                    var one = keep.FirstOrDefault(b => b.IsElse) ?? keep[0];
                    keep = new List<PpBranch> { one };
                }
            }
            foreach (var b in grp) if (!keep.Contains(b) && b.End > b.Start) drops.Add(new int[] { b.Start, b.End });
        }

        static int BraceDelta(SourceFile f, PpBranch b)
        {
            int d = 0;
            foreach (var t in f.Toks)
            {
                if (t.Start < b.Start) continue;
                if (t.Start >= b.End) break;
                if (t.Kind == TokKind.Punct) { if (t.Text == "{") d++; else if (t.Text == "}") d--; }
            }
            return d;
        }

        // evaluacion de tres estados: true / false / null (desconocido)
        static bool? EvalPp(string expr, HashSet<string> defined, HashSet<string> undefined)
        {
            var toks = Regex.Matches(expr, @"\w+|&&|\|\||==|!=|!|\(|\)").Cast<Match>().Select(x => x.Value).ToList();
            int pos = 0;
            return PpOr(toks, ref pos, defined, undefined);
        }

        static bool? PpOr(List<string> t, ref int p, HashSet<string> d, HashSet<string> u)
        {
            var l = PpAnd(t, ref p, d, u);
            while (p < t.Count && t[p] == "||") { p++; var r = PpAnd(t, ref p, d, u); l = (l == true || r == true) ? true : (l == false && r == false) ? (bool?)false : null; }
            return l;
        }

        static bool? PpAnd(List<string> t, ref int p, HashSet<string> d, HashSet<string> u)
        {
            var l = PpEq(t, ref p, d, u);
            while (p < t.Count && t[p] == "&&") { p++; var r = PpEq(t, ref p, d, u); l = (l == false || r == false) ? false : (l == true && r == true) ? (bool?)true : null; }
            return l;
        }

        static bool? PpEq(List<string> t, ref int p, HashSet<string> d, HashSet<string> u)
        {
            var l = PpUnary(t, ref p, d, u);
            while (p < t.Count && (t[p] == "==" || t[p] == "!="))
            {
                bool eq = t[p] == "=="; p++;
                var r = PpUnary(t, ref p, d, u);
                l = (l == null || r == null) ? null : (bool?)(eq ? l == r : l != r);
            }
            return l;
        }

        static bool? PpUnary(List<string> t, ref int p, HashSet<string> d, HashSet<string> u)
        {
            if (p >= t.Count) return null;
            string x = t[p];
            if (x == "!") { p++; var v = PpUnary(t, ref p, d, u); return v == null ? null : (bool?)!v.Value; }
            if (x == "(") { p++; var v = PpOr(t, ref p, d, u); if (p < t.Count && t[p] == ")") p++; return v; }
            p++;
            if (x == "true" || x == "1") return true;
            if (x == "false" || x == "0") return false;
            if (d.Contains(x)) return true;
            if (u.Contains(x)) return false;
            return null;
        }

        static void AddComment(SourceFile f, int s, int e, bool doc, bool pre)
        {
            var c = new Comment { Start = s, End = e, Text = f.Text.Substring(s, e - s), IsDoc = doc, IsPreproc = pre };
            c.Line = f.LineOf(s);
            c.EndLine = f.LineOf(Math.Max(s, e - 1));
            f.Comments.Add(c);
        }

        static int Eol(string s, int i)
        {
            int e = s.IndexOf('\n', i);
            if (e < 0) return s.Length;
            if (e > i && s[e - 1] == '\r') return e - 1;
            return e;
        }

        public static bool IsIdStart(char c) { return char.IsLetter(c) || c == '_'; }
        public static bool IsIdPart(char c)
        {
            if (char.IsLetterOrDigit(c) || c == '_') return true;
            var uc = char.GetUnicodeCategory(c);
            return uc == System.Globalization.UnicodeCategory.NonSpacingMark || uc == System.Globalization.UnicodeCategory.SpacingCombiningMark
                || uc == System.Globalization.UnicodeCategory.ConnectorPunctuation || uc == System.Globalization.UnicodeCategory.Format;
        }

        // Devuelve null si no es un string. 'end' = indice despues del string.
        public static StrLit LexString(string s, int i, out int end)
        {
            int n = s.Length;
            int j = i, dollars = 0;
            bool at = false;
            while (j < n && (s[j] == '$' || s[j] == '@'))
            {
                if (s[j] == '$') dollars++;
                else { if (at) break; at = true; }
                j++;
            }
            end = i;
            if (j >= n || s[j] != '"') return null;
            int q = 0;
            while (j + q < n && s[j + q] == '"') q++;
            var lit = new StrLit();
            lit.Interpolated = dollars > 0;
            if (!at && q >= 3)
            {
                // raw string literal
                lit.CountsLines = true;
                int k = j + q;
                var sb = new StringBuilder();
                while (k < n)
                {
                    if (s[k] == '"')
                    {
                        int r = 0; while (k + r < n && s[k + r] == '"') r++;
                        if (r >= q) { Flush(lit, sb); end = k + r; return lit; }
                        sb.Append(s, k, r); k += r; continue;
                    }
                    if (dollars > 0 && s[k] == '{')
                    {
                        int r = 0; while (k + r < n && s[k + r] == '{') r++;
                        if (r >= dollars)
                        {
                            sb.Append('{', r - dollars);
                            Flush(lit, sb);
                            string expr;
                            int he = ScanHole(s, k + r, dollars, out expr);
                            lit.Parts.Add(new StrPart { IsHole = true, Text = expr });
                            k = he; continue;
                        }
                        sb.Append(s, k, r); k += r; continue;
                    }
                    sb.Append(s[k]); k++;
                }
                Flush(lit, sb); end = n; return lit;
            }
            if (q == 2 && !at && dollars == 0)
            {
                lit.Parts.Add(new StrPart { Text = "" });
                end = j + 2; return lit;
            }
            if (at)
            {
                lit.CountsLines = true;
                int k = j + 1;
                var sb = new StringBuilder();
                while (k < n)
                {
                    char c = s[k];
                    if (c == '"')
                    {
                        if (k + 1 < n && s[k + 1] == '"') { sb.Append('"'); k += 2; continue; }
                        Flush(lit, sb); end = k + 1; return lit;
                    }
                    if (dollars > 0 && c == '{')
                    {
                        if (k + 1 < n && s[k + 1] == '{') { sb.Append('{'); k += 2; continue; }
                        Flush(lit, sb);
                        string expr;
                        int he = ScanHole(s, k + 1, 1, out expr);
                        lit.Parts.Add(new StrPart { IsHole = true, Text = expr });
                        k = he; continue;
                    }
                    if (dollars > 0 && c == '}' && k + 1 < n && s[k + 1] == '}') { sb.Append('}'); k += 2; continue; }
                    sb.Append(c); k++;
                }
                Flush(lit, sb); end = n; return lit;
            }
            {
                // string regular (posiblemente interpolado)
                int k = j + 1;
                var sb = new StringBuilder();
                while (k < n)
                {
                    char c = s[k];
                    if (c == '"') { Flush(lit, sb); end = k + 1; return lit; }
                    if (c == '\n') { Flush(lit, sb); end = k; return lit; }
                    if (c == '\\' && k + 1 < n)
                    {
                        char d = s[k + 1];
                        switch (d)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case '0': sb.Append(' '); break;
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '\'': sb.Append('\''); break;
                            case 'u':
                            case 'x':
                                {
                                    int m = k + 2; int v = 0; int cnt = 0;
                                    while (m < n && cnt < 4 && Uri.IsHexDigit(s[m])) { v = v * 16 + Convert.ToInt32(s[m].ToString(), 16); m++; cnt++; }
                                    sb.Append((char)v); k = m; continue;
                                }
                            default: sb.Append(' '); break;
                        }
                        k += 2; continue;
                    }
                    if (dollars > 0 && c == '{')
                    {
                        if (k + 1 < n && s[k + 1] == '{') { sb.Append('{'); k += 2; continue; }
                        Flush(lit, sb);
                        string expr;
                        int he = ScanHole(s, k + 1, 1, out expr);
                        lit.Parts.Add(new StrPart { IsHole = true, Text = expr });
                        k = he; continue;
                    }
                    if (dollars > 0 && c == '}' && k + 1 < n && s[k + 1] == '}') { sb.Append('}'); k += 2; continue; }
                    sb.Append(c); k++;
                }
                Flush(lit, sb); end = n; return lit;
            }
        }

        static void Flush(StrLit lit, StringBuilder sb)
        {
            if (sb.Length > 0 || lit.Parts.Count == 0) lit.Parts.Add(new StrPart { Text = sb.ToString() });
            sb.Length = 0;
        }

        // k = primer caracter del codigo del hueco. Devuelve indice despues del cierre.
        static int ScanHole(string s, int k, int closeBraces, out string expr)
        {
            int n = s.Length;
            int start = k, exprEnd = -1, depth = 0;
            while (k < n)
            {
                char c = s[k];
                if (c == '"' || ((c == '@' || c == '$') && k + 1 < n && (s[k + 1] == '"' || s[k + 1] == '@' || s[k + 1] == '$')))
                {
                    int e;
                    var l = LexString(s, k, out e);
                    if (l != null && e > k) { k = e; continue; }
                }
                // comentarios dentro del hueco: un apostrofo en "/* don't */" no es un literal char
                if (c == '/' && k + 1 < n && s[k + 1] == '/') { int e = s.IndexOf('\n', k); k = e < 0 ? n : e; continue; }
                if (c == '/' && k + 1 < n && s[k + 1] == '*') { int e = s.IndexOf("*/", k + 2, StringComparison.Ordinal); k = e < 0 ? n : e + 2; continue; }
                if (c == '\'')
                {
                    int j = k + 1;
                    while (j < n && s[j] != '\'' && s[j] != '\n') { if (s[j] == '\\') j++; j++; }
                    k = j + 1; continue;
                }
                if (c == '(' || c == '[' || c == '{') { depth++; k++; continue; }
                if (c == ')' || c == ']') { if (depth > 0) depth--; k++; continue; }
                if (c == '}')
                {
                    if (depth > 0) { depth--; k++; continue; }
                    int r = 0; while (k + r < n && s[k + r] == '}') r++;
                    if (r >= closeBraces)
                    {
                        if (exprEnd < 0) exprEnd = k;
                        expr = s.Substring(start, exprEnd - start).Trim();
                        return k + closeBraces;
                    }
                    k += r; continue;
                }
                if (c == ':' && depth == 0 && exprEnd < 0)
                {
                    if (k + 1 < n && s[k + 1] == ':') { k += 2; continue; }
                    exprEnd = k;
                    // formato: hasta la llave de cierre
                    while (k < n && s[k] != '}') k++;
                    continue;
                }
                if (c == ',' && depth == 0 && exprEnd < 0) { exprEnd = k; k++; continue; }
                if (c == '\n' && closeBraces == 1 && depth == 0 && exprEnd >= 0) { k++; continue; }
                k++;
            }
            if (exprEnd < 0) exprEnd = n;
            expr = s.Substring(start, Math.Max(0, exprEnd - start)).Trim();
            return n;
        }

        // Tabla de parejas () [] {}
        static void ComputeMatch(SourceFile f)
        {
            var t = f.Toks;
            var m = new int[t.Count];
            for (int i = 0; i < m.Length; i++) m[i] = -1;
            var stack = new List<int>();
            for (int i = 0; i < t.Count; i++)
            {
                if (t[i].Kind != TokKind.Punct) continue;
                string x = t[i].Text;
                if (x == "(" || x == "[" || x == "{") { stack.Add(i); continue; }
                string open = x == ")" ? "(" : x == "]" ? "[" : x == "}" ? "{" : null;
                if (open == null) continue;
                // buscar la apertura correspondiente (recuperacion ante desbalance)
                int k = stack.Count - 1;
                while (k >= 0 && t[stack[k]].Text != open) k--;
                if (k < 0) continue;
                int oi = stack[k];
                m[oi] = i; m[i] = oi;
                stack.RemoveRange(k, stack.Count - k);
            }
            f.Match = m;
        }
    }
}

namespace SPA_NS
{
    // =====================================================================
    //  PARSER ESTRUCTURAL (tolerante a errores)
    // =====================================================================
    public class Parser
    {
        SourceFile f;
        List<Token> t;
        int[] m;
        public List<TypeDecl> Types = new List<TypeDecl>();
        public List<MethodDecl> Methods = new List<MethodDecl>();
        TypeDecl topProgram;
        static int seq = 0;

        public Parser(SourceFile file) { f = file; t = file.Toks; m = file.Match; }

        public void Run()
        {
            ParseScope(0, t.Count, "", null, true);
        }

        bool IsP(int i, string p) { return i >= 0 && i < t.Count && t[i].Kind == TokKind.Punct && t[i].Text == p; }
        bool IsId(int i, string s) { return i >= 0 && i < t.Count && t[i].Kind == TokKind.Ident && t[i].Text == s; }
        bool IsIdent(int i) { return i >= 0 && i < t.Count && t[i].Kind == TokKind.Ident; }
        int M(int i) { return (i >= 0 && i < m.Length) ? m[i] : -1; }

        static readonly HashSet<string> TypeKw = new HashSet<string>(new string[] { "class", "struct", "interface", "enum", "record" });

        bool IsTypeDeclAt(int k)
        {
            if (!IsIdent(k)) return false;
            string x = t[k].Text;
            if (x == "class" || x == "struct" || x == "interface" || x == "enum") return IsIdent(k + 1);
            if (x == "record")
            {
                if (IsId(k + 1, "class") || IsId(k + 1, "struct")) return IsIdent(k + 2);
                return IsIdent(k + 1) && (IsP(k + 2, "(") || IsP(k + 2, "{") || IsP(k + 2, "<") || IsP(k + 2, ":") || IsP(k + 2, ";"));
            }
            return false;
        }

        void ParseScope(int s, int e, string ns, TypeDecl outer, bool global)
        {
            int i = s;
            var attrs = new List<AttrInfo>();
            var mods = new HashSet<string>();
            int declStart = -1;
            int guard = 0;
            while (i < e)
            {
                if (++guard > 2000000) break;
                var tk = t[i];
                if (tk.Kind == TokKind.Punct)
                {
                    if (tk.Text == ";") { i++; attrs = new List<AttrInfo>(); mods.Clear(); declStart = -1; continue; }
                    if (tk.Text == "[" && (outer != null || !global || attrs.Count > 0 || IsAttributeLike(i)))
                    {
                        int c = M(i);
                        if (c < 0 || c >= e) { i++; continue; }
                        if (declStart < 0) declStart = i;
                        ParseAttrSection(i, c, attrs);
                        i = c + 1; continue;
                    }
                    if (tk.Text == "}") { i++; continue; }
                }
                if (tk.Kind == TokKind.Ident)
                {
                    string x = tk.Text;
                    if (outer == null && (x == "using" || (x == "global" && IsId(i + 1, "using"))) && attrs.Count == 0)
                    {
                        int k = x == "global" ? i + 2 : i + 1;
                        if (!IsP(k, "(") && !IsId(k, "var") && !(IsIdent(k) && IsIdent(k + 1) && IsP(k + 2, "=")))
                        {
                            int semi = FindStmtEnd(k, e);
                            RecordUsing(k, semi, x == "global");
                            i = semi + 1; continue;
                        }
                    }
                    if (x == "namespace" && outer == null)
                    {
                        int k = i + 1;
                        var sb = new StringBuilder();
                        while (k < e && (IsIdent(k) || IsP(k, "."))) { sb.Append(t[k].Text); k++; }
                        string name = sb.ToString();
                        string full = ns.Length > 0 ? ns + "." + name : name;
                        if (IsP(k, "{"))
                        {
                            int c = M(k); if (c < 0 || c > e) c = e;
                            ParseScope(k + 1, c, full, null, false);
                            i = c + 1; attrs = new List<AttrInfo>(); mods.Clear(); declStart = -1; continue;
                        }
                        if (IsP(k, ";")) { ns = full; global = false; i = k + 1; continue; }
                        i = k; continue;
                    }
                    if (x == "extern" && IsId(i + 1, "alias")) { i = FindStmtEnd(i, e) + 1; continue; }
                    if (IsTypeDeclAt(i))
                    {
                        if (declStart < 0) declStart = i;
                        i = ParseType(i, e, declStart, attrs, mods, ns, outer);
                        attrs = new List<AttrInfo>(); mods.Clear(); declStart = -1; continue;
                    }
                    if (U.IsModifier(x) && (outer != null || !global || IsDeclAhead(i)))
                    {
                        if (declStart < 0) declStart = i;
                        mods.Add(x); i++; continue;
                    }
                    if (x == "delegate" && (outer != null || mods.Count > 0))
                    {
                        i = FindStmtEnd(i, e) + 1; attrs = new List<AttrInfo>(); mods.Clear(); declStart = -1; continue;
                    }
                }
                if (outer != null)
                {
                    if (declStart < 0) declStart = i;
                    int ni = ParseMember(i, e, declStart, attrs, mods, outer);
                    i = Math.Max(ni, i + 1);
                    attrs = new List<AttrInfo>(); mods.Clear(); declStart = -1; continue;
                }
                if (global)
                {
                    int ni = HandleTopLevel(i, e);
                    i = Math.Max(ni, i + 1);
                    attrs = new List<AttrInfo>(); mods.Clear(); declStart = -1; continue;
                }
                i++;
            }
        }

        bool IsAttributeLike(int i)
        {
            // en ambito global, '[' seguido de identificador y ']' o '(' => atributo
            int c = M(i);
            if (c < 0) return false;
            if (IsIdent(i + 1) && (IsP(i + 2, ":") || IsP(i + 2, "]") || IsP(i + 2, "(") || IsP(i + 2, "."))) return true;
            return false;
        }

        bool IsDeclAhead(int i)
        {
            int k = i;
            while (IsIdent(k) && U.IsModifier(t[k].Text)) k++;
            return IsTypeDeclAt(k) || IsId(k, "delegate");
        }

        void RecordUsing(int k, int semi, bool isGlobal)
        {
            bool isStatic = false;
            if (IsId(k, "static")) { isStatic = true; k++; }
            // alias: using Repo = A.B.ClientesRepository;  using IRepo = A.IGenerico<A.Cliente>;
            if (IsIdent(k) && IsP(k + 1, "="))
            {
                string alias = t[k].Text;
                var ab = new StringBuilder();
                for (int j = k + 2; j < semi; j++) { if (IsP(j, "<")) break; ab.Append(t[j].Text); }
                string target = ab.ToString().Replace("global::", "");
                if (target.Length > 0)
                {
                    f.Aliases[alias] = target;
                    if (isGlobal) f.GlobalAliases[alias] = target;
                }
                return;
            }
            var sb = new StringBuilder();
            for (int j = k; j < semi; j++)
            {
                if (IsP(j, "<")) break;
                sb.Append(t[j].Text);
            }
            string u = sb.ToString().Replace("global::", "");
            if (u.Length > 0)
            {
                string val = isStatic ? "static:" + u : u;
                f.Usings.Add(val);
                if (isGlobal) f.GlobalUsings.Add(val);
            }
        }

        int HandleTopLevel(int i, int e)
        {
            int k = i;
            int regionEnd = e;
            bool atStmtStart = true;
            while (k < e)
            {
                if (atStmtStart && k > i)
                {
                    int p = k;
                    while (IsP(p, "[") && M(p) > 0) p = M(p) + 1;
                    while (IsIdent(p) && U.IsModifier(t[p].Text)) p++;
                    if (IsTypeDeclAt(p) || IsId(p, "namespace")) { regionEnd = k; break; }
                }
                atStmtStart = false;
                var tk = t[k];
                if (tk.Kind == TokKind.Punct)
                {
                    if ((tk.Text == "(" || tk.Text == "[" || tk.Text == "{") && M(k) > k)
                    {
                        bool brace = tk.Text == "{";
                        k = M(k) + 1;
                        if (brace && !(IsP(k, ")") || IsP(k, ",") || IsP(k, ".") || IsP(k, ";") || IsId(k, "else") || IsId(k, "catch") || IsId(k, "finally") || IsId(k, "while"))) atStmtStart = true;
                        continue;
                    }
                    if (tk.Text == ";") { atStmtStart = true; k++; continue; }
                }
                k++;
            }
            if (regionEnd <= i) return i + 1;
            if (topProgram == null)
            {
                topProgram = new TypeDecl { Name = "Program", Namespace = "", FullName = "Program", Kind = "class", File = f, Line = t[i].Line, IsPartial = true };
                topProgram.Files.Add(f);
                topProgram.Usings = f.Usings;
                topProgram.Aliases = f.Aliases;
                topProgram.Id = "T" + (++seq);
                Types.Add(topProgram);
            }
            var md = new MethodDecl
            {
                Name = "<top-level>", Owner = topProgram, File = f, Line = t[i].Line, IsStatic = true, IsSynthetic = true,
                BodyStart = i, BodyEnd = regionEnd, DeclStartOffset = t[i].Start, DeclEndOffset = t[regionEnd - 1].End
            };
            md.Id = "M" + (++seq);
            topProgram.Methods.Add(md);
            Methods.Add(md);
            return regionEnd;
        }

        int ParseType(int i, int e, int declStart, List<AttrInfo> attrs, HashSet<string> mods, string ns, TypeDecl outer)
        {
            string kind = t[i].Text;
            int k = i + 1;
            if (kind == "record" && (IsId(k, "class") || IsId(k, "struct"))) k++;
            if (!IsIdent(k)) return i + 1;
            var td = new TypeDecl
            {
                Name = t[k].Text, Kind = kind, Namespace = ns, Outer = outer, File = f, Line = t[k].Line,
                Attrs = new List<AttrInfo>(attrs), IsAbstract = mods.Contains("abstract"), IsStatic = mods.Contains("static"), IsPartial = mods.Contains("partial"),
                IsPublic = mods.Contains("public")
            };
            td.Id = "T" + (++seq);
            td.FullName = (outer != null ? outer.FullName + "." : (ns.Length > 0 ? ns + "." : "")) + td.Name;
            td.Leading = CommentsBefore(declStart, k);
            td.Usings = f.Usings;
            td.Aliases = f.Aliases;
            td.Files.Add(f);
            k++;
            if (IsP(k, "<")) { int g = SkipGeneric(k); if (g > 0) { td.TypeParams = GenericParamNames(k, g); k = g + 1; } }
            if (IsP(k, "(") && M(k) > k) { int c = M(k); td.PrimaryCtor = ParseParams(k, c); k = c + 1; }
            if (IsP(k, ":"))
            {
                k++;
                int guard = 0;
                while (k < e && guard++ < 200)
                {
                    if (IsId(k, "where")) break;
                    int before = k;
                    var tr = ParseTypeRef(ref k);
                    if (tr != null) td.Bases.Add(tr);
                    if (IsP(k, "(") && M(k) > k)
                    {
                        int c = M(k);
                        for (int j = k + 1; j < c; j++) if (t[j].Kind == TokKind.Str) td.BaseCtorStrings.Add(t[j].Lit.PlainValue());
                        AddArgRanges(k, td.BaseCtorArgs);
                        k = c + 1;
                    }
                    if (IsP(k, ",")) { k++; continue; }
                    if (k == before) k++;
                    break;
                }
            }
            while (k < e && !IsP(k, "{") && !IsP(k, ";"))
            {
                if (IsP(k, "(") && M(k) > k) k = M(k) + 1; else k++;
            }
            Types.Add(td);
            if (IsP(k, ";")) return k + 1;
            if (IsP(k, "{"))
            {
                int c = M(k); if (c < 0 || c > e) c = e;
                if (kind != "enum") ParseScope(k + 1, c, ns, td, false);
                return c + 1;
            }
            return k;
        }

        int ParseMember(int i, int e, int declStart, List<AttrInfo> attrs, HashSet<string> mods, TypeDecl td)
        {
            int k = i;
            var x = t[k];
            // finalizador: ~Nombre() { }
            if (IsP(k, "~"))
            {
                if (IsIdent(k + 1) && IsP(k + 2, "(") && M(k + 2) > 0)
                {
                    var fm = NewMethod("~" + t[k + 1].Text, td, k + 1, declStart, attrs, mods);
                    int fk = M(k + 2) + 1;
                    ParseBody(ref fk, fm);
                    Register(fm, td);
                    return Math.Max(fk, i + 1);
                }
                return SkipUnknown(i, e);
            }
            if (x.Kind == TokKind.Ident && x.Text == td.Name && IsP(k + 1, "("))
            {
                int c = M(k + 1);
                if (c < 0) return SkipUnknown(i, e);
                var md = NewMethod(".ctor", td, k, declStart, attrs, mods);
                md.IsCtor = true;
                md.Params = ParseParams(k + 1, c);
                k = c + 1;
                if (IsP(k, ":"))
                {
                    k++;
                    if (IsIdent(k) && IsP(k + 1, "(") && M(k + 1) > 0)
                    {
                        if (t[k].Text == "base")
                        {
                            for (int q = k + 2; q < M(k + 1); q++) if (t[q].Kind == TokKind.Str) td.BaseCtorStrings.Add(t[q].Lit.PlainValue());
                            AddArgRanges(k + 1, td.BaseCtorArgs);
                        }
                        k = M(k + 1) + 1;
                    }
                }
                ParseBody(ref k, md);
                Register(md, td);
                return k;
            }
            // C# 14: extension(Tipo receptor) { miembros }
            if (x.Kind == TokKind.Ident && x.Text == "extension" && IsP(k + 1, "(") && M(k + 1) > 0 && IsP(M(k + 1) + 1, "{"))
            {
                int pc = M(k + 1);
                var recv = ParseParams(k + 1, pc);
                int open = pc + 1, close = M(open);
                if (close < 0) return SkipUnknown(i, e);
                int before = td.Methods.Count;
                ParseScope(open + 1, close, td.Namespace, td, false);
                if (recv.Count > 0)
                {
                    var rp = recv[0]; rp.IsThis = true;
                    for (int q = before; q < td.Methods.Count; q++)
                    {
                        var em = td.Methods[q];
                        if (em.Params.Count == 0 || !em.Params[0].IsThis) em.Params.Insert(0, rp);
                        em.IsExtension = true; em.IsStatic = true;
                    }
                }
                return close + 1;
            }
            if (x.Kind == TokKind.Ident && x.Text == "delegate") return SkipUnknown(i, e);
            // evento con accesores: event Tipo Nombre { add { } remove { } }
            if (x.Kind == TokKind.Ident && x.Text == "event")
            {
                int ek = k + 1;
                var et = ParseTypeRef(ref ek);
                if (et != null && IsIdent(ek) && IsP(ek + 1, "{") && M(ek + 1) > 0)
                {
                    ParseAccessors(t[ek].Text, ek, ek + 1, td, declStart, attrs, mods, null);
                    return M(ek + 1) + 1;
                }
                return SkipUnknown(i, e);
            }
            // operadores de conversion: implicit/explicit operator T(...)
            if (x.Kind == TokKind.Ident && (x.Text == "implicit" || x.Text == "explicit"))
                return ParseOperator(i, e, k, declStart, attrs, mods, td);
            if (x.Kind != TokKind.Ident && !IsP(k, "(")) return SkipUnknown(i, e);

            TypeRef type = ParseTypeRef(ref k);
            if (type == null) return SkipUnknown(i, e);
            if (IsId(k, "operator")) return ParseOperator(i, e, k, declStart, attrs, mods, td);
            // indexador: Tipo this[...] { get { } set { } }  /  => expr;
            if (IsId(k, "this") && IsP(k + 1, "[") && M(k + 1) > 0)
            {
                int bc = M(k + 1);
                var ip = ParseParams(k + 1, bc);
                int ik = bc + 1;
                if (IsP(ik, "{") && M(ik) > 0) { ParseAccessors("this[]", k, ik, td, declStart, attrs, mods, ip); return M(ik) + 1; }
                if (IsP(ik, "=>"))
                {
                    var gm = NewMethod("this[]", td, k, declStart, attrs, mods);
                    gm.AccessorKind = "get"; gm.Params = ip; gm.ReturnType = type;
                    ParseBody(ref ik, gm);
                    Register(gm, td);
                    return ik;
                }
                return SkipUnknown(i, e);
            }
            if (!IsIdent(k)) return SkipUnknown(i, e);
            string name = t[k].Text;
            int nameTok = k;
            string explicitIface = null;
            k++;
            int guard = 0;
            while (guard++ < 20)
            {
                if (IsP(k, "<"))
                {
                    int g = SkipGeneric(k);
                    if (g > 0 && IsP(g + 1, ".") && IsIdent(g + 2)) { explicitIface = name; name = t[g + 2].Text; nameTok = g + 2; k = g + 3; continue; }
                    break;
                }
                if (IsP(k, ".") && IsIdent(k + 1)) { explicitIface = name; name = t[k + 1].Text; nameTok = k + 1; k += 2; continue; }
                break;
            }
            List<string> mtp = null;
            if (IsP(k, "<")) { int g = SkipGeneric(k); if (g > 0) { mtp = GenericParamNames(k, g); k = g + 1; } }
            if (IsP(k, "("))
            {
                int c = M(k);
                if (c < 0) return SkipUnknown(i, e);
                var md = NewMethod(name, td, nameTok, declStart, attrs, mods);
                md.ReturnType = type;
                md.ExplicitIface = explicitIface;
                if (mtp != null) md.TypeParams = mtp;
                md.Params = ParseParams(k, c);
                md.IsExtension = md.Params.Count > 0 && md.Params[0].IsThis;
                k = c + 1;
                while (k < t.Count && !IsP(k, "{") && !IsP(k, "=>") && !IsP(k, ";"))
                {
                    if (IsP(k, "(") && M(k) > k) k = M(k) + 1; else k++;
                }
                // comentarios entre la firma y el cuerpo: "public X Foo() // Migrado de ..."
                if (k < t.Count) foreach (var cm in f.CommentsBetween(t[c].End, t[k].Start)) if (!cm.IsPreproc) md.Leading.Add(cm);
                ParseBody(ref k, md);
                if (td.Kind == "interface" && md.BodyStart < 0) md.IsAbstract = true;
                Register(md, td);
                return k;
            }
            if (IsP(k, "{"))
            {
                int c = M(k);
                if (c < 0) return SkipUnknown(i, e);
                var mv = NewVar(name, type, td, nameTok, declStart, mods);
                mv.IsProperty = true;
                for (int j = k + 1; j < c; j++)
                {
                    if (IsId(j, "get"))
                    {
                        if (IsP(j + 1, "=>")) { mv.InitStart = j + 2; mv.InitEnd = FindStmtEnd(j + 2, c); mv.IsExprBody = true; }
                        else if (IsP(j + 1, "{") && IsId(j + 2, "return")) { mv.InitStart = j + 3; mv.InitEnd = FindStmtEnd(j + 3, c); mv.IsExprBody = true; }
                        break;
                    }
                }
                // cuerpos de los accesores como metodos (para seguir llamadas y SP dentro de get/set)
                ParseAccessors(name, nameTok, k, td, declStart, attrs, mods, null, type, explicitIface);
                k = c + 1;
                if (IsP(k, "="))
                {
                    int end = FindStmtEnd(k + 1, t.Count);
                    mv.InitStart = k + 1; mv.InitEnd = end;
                    k = end + 1;
                }
                AddVar(td, mv);
                return k;
            }
            if (IsP(k, "=>"))
            {
                var mv = NewVar(name, type, td, nameTok, declStart, mods);
                mv.IsProperty = true; mv.IsExprBody = true;
                int end = FindStmtEnd(k + 1, t.Count);
                mv.InitStart = k + 1; mv.InitEnd = end;
                AddVar(td, mv);
                // propiedad de solo lectura con cuerpo de expresion: accesor get
                var gm = NewMethod(name, td, nameTok, declStart, attrs, mods);
                gm.AccessorKind = "get"; gm.ReturnType = type; gm.ExplicitIface = explicitIface;
                gm.BodyStart = k + 1; gm.BodyEnd = end;
                Register(gm, td);
                return end + 1;
            }
            if (IsP(k, "=") || IsP(k, ";") || IsP(k, ","))
            {
                int g2 = 0;
                while (g2++ < 200)
                {
                    var mv = NewVar(name, type, td, nameTok, declStart, mods);
                    if (IsP(k, "="))
                    {
                        int end = FindDeclEnd(k + 1, t.Count);
                        mv.InitStart = k + 1; mv.InitEnd = end;
                        k = end;
                    }
                    AddVar(td, mv);
                    if (IsP(k, ",") && IsIdent(k + 1)) { name = t[k + 1].Text; nameTok = k + 1; k += 2; continue; }
                    if (IsP(k, ";")) k++;
                    break;
                }
                return k;
            }
            return SkipUnknown(i, e);
        }

        void ParseAccessors(string name, int nameTok, int open, TypeDecl td, int declStart, List<AttrInfo> attrs, HashSet<string> mods, List<ParamInfo> idxParams)
        {
            ParseAccessors(name, nameTok, open, td, declStart, attrs, mods, idxParams, null, null);
        }

        // { [attr] [mod] get => x; set { ... } init; add {...} remove {...} }
        void ParseAccessors(string name, int nameTok, int open, TypeDecl td, int declStart, List<AttrInfo> attrs, HashSet<string> mods, List<ParamInfo> idxParams, TypeRef type, string explicitIface)
        {
            int close = M(open);
            if (close < 0) return;
            int j = open + 1;
            int guard = 0;
            while (j < close && guard++ < 50)
            {
                while (IsP(j, "[") && M(j) > j && M(j) < close) j = M(j) + 1;
                while (IsIdent(j) && (t[j].Text == "private" || t[j].Text == "protected" || t[j].Text == "internal" || t[j].Text == "public" || t[j].Text == "readonly")) j++;
                if (!IsIdent(j)) { j++; continue; }
                string kw = t[j].Text;
                if (kw != "get" && kw != "set" && kw != "init" && kw != "add" && kw != "remove") { j++; continue; }
                int b = j + 1;
                if (IsP(b, ";")) { j = b + 1; continue; }   // accesor automatico
                var am = NewMethod(name, td, nameTok, declStart, attrs, mods);
                am.AccessorKind = kw == "init" ? "set" : kw;
                am.ReturnType = type;
                am.ExplicitIface = explicitIface;
                if (idxParams != null) am.Params = new List<ParamInfo>(idxParams);
                if (kw != "get") am.Params.Add(new ParamInfo { Name = "value" });
                ParseBody(ref b, am);
                if (am.HasBody) Register(am, td);
                j = Math.Max(b, j + 1);
            }
        }

        // operadores: Tipo operator +(...) { }  /  implicit operator T(...) => ...
        int ParseOperator(int i, int e, int k, int declStart, List<AttrInfo> attrs, HashSet<string> mods, TypeDecl td)
        {
            int j = k;
            while (j < e && !IsP(j, "(")) j++;
            if (j >= e || M(j) < 0) return SkipUnknown(i, e);
            var sb = new StringBuilder("op_");
            for (int q = k + 1; q < j; q++) sb.Append(t[q].Text);
            var om = NewMethod(sb.ToString(), td, k, declStart, attrs, mods);
            om.Params = ParseParams(j, M(j));
            int b = M(j) + 1;
            ParseBody(ref b, om);
            if (om.HasBody) Register(om, td);
            return Math.Max(b, i + 1);
        }

        MethodDecl NewMethod(string name, TypeDecl td, int nameTok, int declStart, List<AttrInfo> attrs, HashSet<string> mods)
        {
            var md = new MethodDecl { Name = name, Owner = td, File = f, Line = t[nameTok].Line, Attrs = new List<AttrInfo>(attrs) };
            md.IsStatic = mods.Contains("static");
            md.IsPublic = mods.Contains("public") || td.Kind == "interface";
            md.IsAbstract = mods.Contains("abstract");
            md.IsVirtual = mods.Contains("virtual");
            md.IsOverride = mods.Contains("override");
            md.Leading = CommentsBefore(declStart, nameTok);
            md.DeclStartOffset = t[declStart].Start;
            md.Id = "M" + (++seq);
            return md;
        }

        MemberVar NewVar(string name, TypeRef type, TypeDecl td, int nameTok, int declStart, HashSet<string> mods)
        {
            var mv = new MemberVar { Name = name, Type = type, Owner = td, File = f, Line = t[nameTok].Line };
            mv.IsConst = mods.Contains("const");
            mv.IsStatic = mods.Contains("static") || mv.IsConst;
            mv.IsReadonly = mods.Contains("readonly");
            mv.Leading = CommentsBefore(declStart, nameTok);
            mv.DeclStartOffset = t[declStart].Start;
            mv.Id = "V" + (++seq);
            return mv;
        }

        void AddVar(TypeDecl td, MemberVar mv)
        {
            if (!td.Members.ContainsKey(mv.Name)) td.Members[mv.Name] = mv;
        }

        void Register(MethodDecl md, TypeDecl td)
        {
            md.DeclEndOffset = md.BodyEnd > 0 && md.BodyEnd <= t.Count ? t[Math.Min(md.BodyEnd, t.Count - 1)].End : md.DeclStartOffset;
            td.Methods.Add(md);
            Methods.Add(md);
        }

        void ParseBody(ref int k, MethodDecl md)
        {
            if (IsP(k, "{"))
            {
                int c = M(k); if (c < 0) c = t.Count;
                md.BodyStart = k + 1; md.BodyEnd = c;
                k = c + 1; return;
            }
            if (IsP(k, "=>"))
            {
                int end = FindStmtEnd(k + 1, t.Count);
                md.BodyStart = k + 1; md.BodyEnd = end;
                k = end + 1; return;
            }
            if (IsP(k, ";")) { k++; return; }
        }

        int SkipUnknown(int i, int e)
        {
            int k = i;
            while (k < e)
            {
                if (IsP(k, ";")) return k + 1;
                if (IsP(k, "{") && M(k) > k) return M(k) + 1;
                if ((IsP(k, "(") || IsP(k, "[")) && M(k) > k) { k = M(k) + 1; continue; }
                if (IsP(k, "}")) return k;
                k++;
            }
            return Math.Max(i + 1, k);
        }

        public int FindStmtEnd(int s, int limit)
        {
            for (int j = s; j < limit && j < t.Count; j++)
            {
                if (t[j].Kind == TokKind.Punct)
                {
                    string x = t[j].Text;
                    if ((x == "(" || x == "[" || x == "{") && M(j) > j) { j = M(j); continue; }
                    if (x == ";") return j;
                    if (x == "}" || x == ")" || x == "]") return j;
                }
            }
            return Math.Min(limit, t.Count);
        }

        int FindDeclEnd(int s, int limit)
        {
            for (int j = s; j < limit && j < t.Count; j++)
            {
                if (t[j].Kind == TokKind.Punct)
                {
                    string x = t[j].Text;
                    if ((x == "(" || x == "[" || x == "{") && M(j) > j) { j = M(j); continue; }
                    if (x == "<" && j > 0 && t[j - 1].Kind == TokKind.Ident) { int g = SkipGeneric(j); if (g > 0) { j = g; continue; } }
                    if (x == ";" || x == ",") return j;
                    if (x == "}" || x == ")" || x == "]") return j;
                }
            }
            return Math.Min(limit, t.Count);
        }

        public int SkipGeneric(int i)
        {
            int depth = 0;
            for (int k = i; k < t.Count && k < i + 300; k++)
            {
                var tk = t[k];
                if (tk.Kind == TokKind.Punct)
                {
                    string x = tk.Text;
                    if (x == "<") { depth++; continue; }
                    if (x == ">") { depth--; if (depth == 0) return k; continue; }
                    if (x == "," || x == "." || x == "?" || x == "*" || x == "::") continue;
                    if ((x == "(" || x == "[") && M(k) > k) { k = M(k); continue; }
                    return -1;
                }
                if (tk.Kind == TokKind.Ident) continue;
                return -1;
            }
            return -1;
        }

        public TypeRef ParseTypeRef(ref int k)
        {
            if (IsP(k, "("))
            {
                int c = M(k); if (c < 0) return null;
                var tup = new TypeRef { Name = "(tuple)", Qualified = "(tuple)" };
                k = c + 1;
                SkipTypeSuffix(ref k);
                return tup;
            }
            if (!IsIdent(k)) return null;
            string first = t[k].Text;
            if (first == "return" || first == "new" || first == "if" || first == "throw" || first == "await" || first == "var" && false) return null;
            var parts = new List<string>();
            var tr = new TypeRef();
            int guard = 0;
            while (guard++ < 30)
            {
                if (IsId(k, "global") && IsP(k + 1, "::")) k += 2;
                if (!IsIdent(k)) break;
                parts.Add(t[k].Text);
                k++;
                if (IsP(k, "<"))
                {
                    int g = SkipGeneric(k);
                    if (g > 0) { tr.Args = ParseGenericArgs(k, g); k = g + 1; }
                }
                if ((IsP(k, ".") || IsP(k, "::")) && IsIdent(k + 1) && !(IsP(k + 2, "(") && parts.Count > 0 && false)) { k++; continue; }
                break;
            }
            if (parts.Count == 0) return null;
            tr.Name = parts[parts.Count - 1];
            tr.Qualified = string.Join(".", parts.ToArray());
            SkipTypeSuffix(ref k);
            return tr;
        }

        void SkipTypeSuffix(ref int k)
        {
            int guard = 0;
            while (guard++ < 10)
            {
                if (IsP(k, "?") || IsP(k, "*")) { k++; continue; }
                if (IsP(k, "[") && (IsP(k + 1, "]") || IsP(k + 1, ",")) && M(k) > k) { k = M(k) + 1; continue; }
                break;
            }
        }

        List<TypeRef> ParseGenericArgs(int open, int close)
        {
            var list = new List<TypeRef>();
            int j = open + 1;
            int guard = 0;
            while (j < close && guard++ < 50)
            {
                int before = j;
                if (IsId(j, "in") || IsId(j, "out")) j++;
                var a = ParseTypeRef(ref j);
                if (a != null) list.Add(a);
                int depth = 0;
                while (j < close)
                {
                    if (IsP(j, "<")) depth++;
                    else if (IsP(j, ">")) depth--;
                    else if (IsP(j, "(") && M(j) > j) { j = M(j); }
                    else if (IsP(j, ",") && depth <= 0) break;
                    j++;
                }
                j++;
                if (j <= before) j = before + 1;
            }
            return list;
        }

        public List<ParamInfo> ParseParams(int open, int close)
        {
            var list = new List<ParamInfo>();
            int j = open + 1;
            int guard = 0;
            while (j < close && guard++ < 200)
            {
                int segEnd = j;
                while (segEnd < close)
                {
                    if ((IsP(segEnd, "(") || IsP(segEnd, "[") || IsP(segEnd, "{")) && M(segEnd) > segEnd) { segEnd = M(segEnd) + 1; continue; }
                    if (IsP(segEnd, "<") && segEnd > 0 && IsIdent(segEnd - 1)) { int g = SkipGeneric(segEnd); if (g > 0) { segEnd = g + 1; continue; } }
                    if (IsP(segEnd, ",")) break;
                    segEnd++;
                }
                var p = ParseOneParam(j, segEnd);
                if (p != null) list.Add(p);
                j = segEnd + 1;
            }
            return list;
        }

        ParamInfo ParseOneParam(int s, int e)
        {
            var p = new ParamInfo();
            int k = s;
            while (IsP(k, "[") && M(k) > k && M(k) < e) { ParseAttrSection(k, M(k), p.Attrs); k = M(k) + 1; }
            int guard = 0;
            while (IsIdent(k) && guard++ < 6)
            {
                string x = t[k].Text;
                if (x == "this") { p.IsThis = true; k++; continue; }
                if (x == "params") { p.IsParams = true; k++; continue; }
                if (x == "ref" || x == "out" || x == "in" || x == "scoped" || x == "readonly") { k++; continue; }
                break;
            }
            if (k >= e) return null;
            if (e - k == 1 && IsIdent(k)) { p.Name = t[k].Text; return p; }
            p.Type = ParseTypeRef(ref k);
            if (IsIdent(k) && k < e) { p.Name = t[k].Text; k++; }
            if (IsP(k, "=")) p.HasDefault = true;
            return p;
        }

        public void ParseAttrSection(int open, int close, List<AttrInfo> list)
        {
            int k = open + 1;
            if (IsIdent(k) && IsP(k + 1, ":"))
            {
                string tg = t[k].Text;
                if (tg == "assembly" || tg == "module") return;
                k += 2;
            }
            int guard = 0;
            while (k < close && guard++ < 50)
            {
                if (!IsIdent(k)) { k++; continue; }
                var a = new AttrInfo { Line = t[k].Line, StartOffset = t[k].Start, File = f };
                string name = t[k].Text; k++;
                while ((IsP(k, ".") || IsP(k, "::")) && IsIdent(k + 1)) { name = t[k + 1].Text; k += 2; }
                if (IsP(k, "<")) { int g = SkipGeneric(k); if (g > 0) k = g + 1; }
                if (name.EndsWith("Attribute") && name.Length > 9) name = name.Substring(0, name.Length - 9);
                a.Name = name;
                if (IsP(k, "(") && M(k) > k)
                {
                    int c = M(k);
                    int as0 = k + 1;
                    while (as0 < c)
                    {
                        int ae = as0;
                        while (ae < c)
                        {
                            if ((IsP(ae, "(") || IsP(ae, "[") || IsP(ae, "{")) && M(ae) > ae) { ae = M(ae) + 1; continue; }
                            if (IsP(ae, ",")) break;
                            ae++;
                        }
                        string key = null;
                        int vs = as0;
                        if (IsIdent(as0) && (IsP(as0 + 1, "=") || IsP(as0 + 1, ":")) && ae > as0 + 2) { key = t[as0].Text; vs = as0 + 2; }
                        string val; bool isStr = false;
                        if (ae - vs == 1 && t[vs].Kind == TokKind.Str) { val = t[vs].Lit.PlainValue(); isStr = true; }
                        else
                        {
                            var sb = new StringBuilder();
                            for (int q = vs; q < ae; q++) sb.Append(t[q].Kind == TokKind.Str ? "\"" + t[q].Lit.PlainValue() + "\"" : t[q].Text);
                            val = sb.ToString();
                        }
                        for (int q = vs; q < ae; q++) if (t[q].Kind == TokKind.Str) a.AllStrings.Add(t[q].Lit.PlainValue());
                        var ar = new ArgRef { File = f, S = vs, E = ae };
                        if (key != null) { a.Named[key] = val; a.NamedArgs[key] = ar; }
                        else { a.Positional.Add(val); a.PositionalIsString.Add(isStr); a.PosArgs.Add(ar); }
                        as0 = ae + 1;
                    }
                    k = c + 1;
                }
                list.Add(a);
                if (IsP(k, ",")) k++;
            }
        }

        // nombres de los parametros genericos de "<T, in U, [Attr] V>"
        List<string> GenericParamNames(int open, int close)
        {
            var r = new List<string>();
            int depth = 0;
            for (int j = open + 1; j < close; j++)
            {
                if (IsP(j, "<")) { depth++; continue; }
                if (IsP(j, ">")) { depth--; continue; }
                if (IsP(j, "[") && M(j) > j) { j = M(j); continue; }
                if (depth == 0 && IsIdent(j) && t[j].Text != "in" && t[j].Text != "out" && (IsP(j + 1, ",") || j + 1 == close)) r.Add(t[j].Text);
            }
            return r;
        }

        // rangos de los argumentos de una lista "( a, b, c )" que empieza en 'open'
        void AddArgRanges(int open, List<ArgRef> into)
        {
            int c = M(open);
            if (c < 0) return;
            int s = open + 1;
            for (int j = open + 1; j <= c; j++)
            {
                if (j < c && (IsP(j, "(") || IsP(j, "[") || IsP(j, "{")) && M(j) > j) { j = M(j); continue; }
                if (j == c || IsP(j, ","))
                {
                    if (j > s) into.Add(new ArgRef { File = f, S = s, E = j });
                    s = j + 1;
                }
            }
        }

        public List<Comment> CommentsBefore(int declStartTok, int nameTok)
        {
            int from = declStartTok > 0 ? t[declStartTok - 1].End : 0;
            int prevLine = declStartTok > 0 ? t[declStartTok - 1].EndLine : -1;
            int to = t[nameTok].Start;
            var r = new List<Comment>();
            foreach (var c in f.CommentsBetween(from, to))
            {
                if (c.Line == prevLine && declStartTok > 0) continue; // comentario de cola del miembro anterior
                if (c.IsPreproc && !c.Text.TrimStart('#', ' ', '\t').StartsWith("region", StringComparison.OrdinalIgnoreCase)) continue;
                r.Add(c);
            }
            return r;
        }
    }
}

namespace SPA_NS
{
    // =====================================================================
    //  INDICE DE CODIGO Y RESOLUCION DE LLAMADAS
    // =====================================================================

    public class Seg
    {
        public string Name;
        public bool IsCall, IsIndexer;
        public int Argc;
        public int ArgOpen = -1;
        public List<TypeRef> GenericArgs = new List<TypeRef>();
        public TypeRef CastType;          // receptor "((T)x)" o "(x as T)"
        public List<Seg> Inner;           // receptor "(expr)" o "(await expr)"
    }

    public class CallSite
    {
        public string Name;
        public List<Seg> Receiver = new List<Seg>();
        public int Argc;
        public int Tok;
        public int Line;
        public bool IsNew, IsMethodGroup;
        public TypeRef NewType;
        public int ArgOpen = -1;
        public string PropertyAccess;     // get / set: acceso a propiedad o indexador con cuerpo
    }

    // destino de una llamada: metodo y tipo concreto a traves del cual se llega (para despacho virtual)
    public class Target
    {
        public MethodDecl M;
        public TypeDecl Via;
        public bool Inferred;
    }

    public class LocalInfo
    {
        public TypeRef Type;
        public int ExprTok = -1;
        public int ForeachTok = -1;
        public bool Resolving;
        public TypeSet Cache;
        public bool Known;    // tipo conocido (aunque sea externo)
    }

    public class TypeSet
    {
        public List<TypeDecl> Types = new List<TypeDecl>();
        public List<TypeRef> Refs = new List<TypeRef>();   // tipos declarados (con argumentos genericos)
        public bool Known;      // se conoce el tipo (aunque no este en el repo)
        public bool Static;     // acceso estatico por nombre de tipo
        public bool IsBase;     // receptor "base"
        public bool IsThis;     // receptor "this"
        public bool OpenGeneric;// tipo con parametros genericos sin ligar (ICommandHandler<TCommand>)
    }

    public class ResxEntry { public string FileBase, Key, Value, Comment; public SourceFile File; public int Line, CommentLine; }
    public class ConfigEntry { public string Path, Leaf, Value; public SourceFile File; public int Line; }

    public class CodeIndex
    {
        public AnalyzerOptions Opt;
        public List<SourceFile> Files = new List<SourceFile>();
        public List<SourceFile> SqlFiles = new List<SourceFile>();
        public List<TypeDecl> Types = new List<TypeDecl>();
        public List<MethodDecl> Methods = new List<MethodDecl>();
        public Dictionary<string, List<TypeDecl>> TypesByName = new Dictionary<string, List<TypeDecl>>(StringComparer.Ordinal);
        public Dictionary<string, List<MethodDecl>> MethodsByName = new Dictionary<string, List<MethodDecl>>(StringComparer.Ordinal);
        public Dictionary<string, List<MethodDecl>> ExtensionsByName = new Dictionary<string, List<MethodDecl>>(StringComparer.Ordinal);
        public Dictionary<string, List<TypeDecl>> Implementors = new Dictionary<string, List<TypeDecl>>(StringComparer.Ordinal);     // por nombre (bases externas)
        public Dictionary<string, List<TypeDecl>> ImplementorsById = new Dictionary<string, List<TypeDecl>>(StringComparer.Ordinal); // por tipo resuelto
        public Dictionary<string, List<TypeDecl>> AncestorCache = new Dictionary<string, List<TypeDecl>>();
        public Dictionary<string, HashSet<string>> AncestorNames = new Dictionary<string, HashSet<string>>();
        public Dictionary<string, List<Target>> RequestHandlers = new Dictionary<string, List<Target>>(StringComparer.Ordinal); // por Id del tipo request
        public HashSet<string> RequestTypeNames = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, List<SourceFile>> SqlByName = new Dictionary<string, List<SourceFile>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<ResxEntry>> ResxByKey = new Dictionary<string, List<ResxEntry>>(StringComparer.Ordinal);
        public HashSet<string> ResxBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ConfigEntry> ConfigByPath = new Dictionary<string, ConfigEntry>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<ConfigEntry>> ConfigByLeaf = new Dictionary<string, List<ConfigEntry>>(StringComparer.Ordinal);
        public Dictionary<string, string> OptionsSections = new Dictionary<string, string>(StringComparer.Ordinal); // clase de opciones -> seccion
        public HashSet<string> OptionsClasses = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<SourceFile, Parser> Parsers = new Dictionary<SourceFile, Parser>();
        public Dictionary<string, List<TypeDecl>> NestedByOuter = new Dictionary<string, List<TypeDecl>>();
        public HashSet<string> ReferencedConsts = new HashSet<string>();
        public HashSet<string> PropertyNames = new HashSet<string>(StringComparer.Ordinal);   // propiedades/indexadores con cuerpo
        public bool TrackRefs;
        Dictionary<string, Dictionary<string, LocalInfo>> localsCache = new Dictionary<string, Dictionary<string, LocalInfo>>();
        Dictionary<string, List<CallSite>> callsCache = new Dictionary<string, List<CallSite>>();
        Dictionary<string, string> constCache = new Dictionary<string, string>();
        HashSet<string> constResolving = new HashSet<string>();
        int localFnSeq = 0;

        static readonly HashSet<string> HandlerMethodNames = new HashSet<string>(new string[] {
            "Handle","HandleAsync","Consume","ConsumeAsync","Execute","ExecuteAsync","Process","ProcessAsync","Run","RunAsync","Invoke","InvokeAsync" });
        public static readonly Regex DispatchRx = new Regex(@"^(Send|SendAsync|Publish|PublishAsync|Dispatch\w*|Execute\w*|Handle\w*|Process\w*|Ask\w*|Request\w*|Invoke\w*|Mediate\w*|Enviar\w*|Despachar\w*|Ejecutar\w*|Procesar\w*|Publicar\w*|Manejar\w*|Notificar\w*|Emitir\w*|Raise\w*|Add\w*Event\w*|Enqueue\w*|Encolar\w*|Schedule\w*)$");
        static readonly HashSet<string> PredefTypes = new HashSet<string>(new string[] {
            "void","int","string","bool","long","decimal","double","float","object","char","byte","short","uint","ulong","ushort","sbyte","dynamic","var" });

        public Parser P(SourceFile f)
        {
            Parser p;
            if (!Parsers.TryGetValue(f, out p)) { p = new Parser(f); Parsers[f] = p; }
            return p;
        }

        static bool IsP(List<Token> t, int i, string s) { return i >= 0 && i < t.Count && t[i].Kind == TokKind.Punct && t[i].Text == s; }
        static bool IsI(List<Token> t, int i) { return i >= 0 && i < t.Count && t[i].Kind == TokKind.Ident; }

        // ------------------------------------------------------------ build
        public void Build(List<TypeDecl> rawTypes, List<MethodDecl> rawMethods)
        {
            // fusion de partial / tipos con el mismo nombre completo
            var byFull = new Dictionary<string, TypeDecl>(StringComparer.Ordinal);
            foreach (var td in rawTypes)
            {
                TypeDecl prim;
                if (byFull.TryGetValue(td.FullName, out prim) && prim.Kind == td.Kind)
                {
                    td.MergedInto = prim;
                    foreach (var md in td.Methods) { md.Owner = prim; prim.Methods.Add(md); }
                    foreach (var kv in td.Members) { kv.Value.Owner = prim; if (!prim.Members.ContainsKey(kv.Key)) prim.Members[kv.Key] = kv.Value; }
                    foreach (var b in td.Bases) if (!prim.Bases.Any(x => x.Name == b.Name)) prim.Bases.Add(b);
                    prim.Attrs.AddRange(td.Attrs);
                    foreach (var f in td.Files) if (!prim.Files.Contains(f)) prim.Files.Add(f);
                    prim.Leading.AddRange(td.Leading);
                    if (prim.PrimaryCtor == null) prim.PrimaryCtor = td.PrimaryCtor;
                    if (prim.TypeParams.Count == 0) prim.TypeParams = td.TypeParams;
                    prim.BaseCtorStrings.AddRange(td.BaseCtorStrings);
                    prim.BaseCtorArgs.AddRange(td.BaseCtorArgs);
                    prim.IsAbstract |= td.IsAbstract; prim.IsStatic |= td.IsStatic; prim.IsPublic |= td.IsPublic;
                    foreach (var u in td.Usings) if (!prim.Usings.Contains(u)) prim.Usings = new List<string>(prim.Usings.Concat(new string[] { u }));
                }
                else if (!byFull.ContainsKey(td.FullName)) { byFull[td.FullName] = td; Types.Add(td); }
                else { Types.Add(td); }
            }
            foreach (var td in Types)
            {
                if (td.Outer == null) continue;
                var o = td.Outer.MergedInto ?? td.Outer;
                List<TypeDecl> nl;
                if (!NestedByOuter.TryGetValue(o.Id, out nl)) { nl = new List<TypeDecl>(); NestedByOuter[o.Id] = nl; }
                nl.Add(td);
            }
            foreach (var td in Types)
            {
                List<TypeDecl> l;
                if (!TypesByName.TryGetValue(td.Name, out l)) { l = new List<TypeDecl>(); TypesByName[td.Name] = l; }
                l.Add(td);
            }
            foreach (var td in Types)
            {
                foreach (var md in td.Methods)
                {
                    Methods.Add(md);
                    List<MethodDecl> l;
                    if (!MethodsByName.TryGetValue(md.Name, out l)) { l = new List<MethodDecl>(); MethodsByName[md.Name] = l; }
                    l.Add(md);
                    if (md.IsExtension)
                    {
                        if (!ExtensionsByName.TryGetValue(md.Name, out l)) { l = new List<MethodDecl>(); ExtensionsByName[md.Name] = l; }
                        l.Add(md);
                    }
                }
            }
            // funciones locales (incluidas las de Program.cs con top-level statements)
            foreach (var md in Methods.ToList()) if (md.HasBody) FindLocalFunctions(md);
            foreach (var md in Methods) if (md.AccessorKind != null) PropertyNames.Add(md.Name);
            // ancestros e implementadores
            foreach (var td in Types)
            {
                foreach (var n in GetAncestorNames(td))
                {
                    List<TypeDecl> l;
                    if (!Implementors.TryGetValue(n, out l)) { l = new List<TypeDecl>(); Implementors[n] = l; }
                    if (!l.Contains(td)) l.Add(td);
                }
                foreach (var a in Ancestors(td))
                {
                    List<TypeDecl> l;
                    if (!ImplementorsById.TryGetValue(a.Id, out l)) { l = new List<TypeDecl>(); ImplementorsById[a.Id] = l; }
                    if (!l.Contains(td)) l.Add(td);
                }
            }
            RegisterHandlers();
            FindOptionsBindings();
            foreach (var sf in SqlFiles)
            {
                string name = System.IO.Path.GetFileName(sf.Path);
                List<SourceFile> l;
                if (!SqlByName.TryGetValue(name, out l)) { l = new List<SourceFile>(); SqlByName[name] = l; }
                l.Add(sf);
            }
        }

        // ------------------------------------------------------------ funciones locales
        void FindLocalFunctions(MethodDecl m)
        {
            var t = m.File.Toks; var mt = m.File.Match;
            var p = P(m.File);
            for (int k = m.BodyStart; k < m.BodyEnd && k < t.Count; k++)
            {
                bool stmtStart = k == m.BodyStart || IsP(t, k - 1, ";") || IsP(t, k - 1, "{") || IsP(t, k - 1, "}") || IsP(t, k - 1, "]");
                if (!stmtStart || !IsI(t, k)) continue;
                int j = k;
                while (IsI(t, j) && (t[j].Text == "static" || t[j].Text == "async" || t[j].Text == "unsafe" || t[j].Text == "extern")) j++;
                if (!IsI(t, j)) continue;
                string first = t[j].Text;
                if (U.IsKeyword(first) && !PredefTypes.Contains(first)) continue;
                int typeStart = j;
                var rt = p.ParseTypeRef(ref j);
                if (rt == null || j == typeStart || !IsI(t, j) || U.IsKeyword(t[j].Text)) continue;
                int nameTok = j;
                j++;
                List<string> tps = null;
                if (IsP(t, j, "<")) { int g = p.SkipGeneric(j); if (g < 0) continue; tps = new List<string>(); for (int q = j + 1; q < g; q++) if (IsI(t, q)) tps.Add(t[q].Text); j = g + 1; }
                if (!IsP(t, j, "(") || mt[j] < j) continue;
                int close = mt[j];
                int b = close + 1;
                while (IsI(t, b) && t[b].Text == "where") { while (b < m.BodyEnd && !IsP(t, b, "{") && !IsP(t, b, "=>")) b++; }
                if (!(IsP(t, b, "{") || IsP(t, b, "=>"))) continue;
                var lf = new MethodDecl
                {
                    Name = t[nameTok].Text, Owner = m.Owner, File = m.File, Line = t[nameTok].Line, IsLocalFunction = true, Parent = m,
                    ReturnType = rt, IsStatic = m.IsStatic, DeclStartOffset = t[k].Start
                };
                if (tps != null) lf.TypeParams = tps;
                lf.Id = "LF" + (++localFnSeq);
                lf.Params = p.ParseParams(j, close);
                if (IsP(t, b, "{")) { lf.BodyStart = b + 1; lf.BodyEnd = mt[b] > b ? mt[b] : m.BodyEnd; }
                else { lf.BodyStart = b + 1; lf.BodyEnd = p.FindStmtEnd(b + 1, m.BodyEnd); }
                lf.DeclEndOffset = t[Math.Min(lf.BodyEnd, t.Count - 1)].End;
                lf.Leading = m.File.CommentsBetween(k > 0 ? t[k - 1].End : 0, t[nameTok].Start);
                m.LocalFunctions.Add(lf);
                Methods.Add(lf);
            }
        }

        public bool InLocalFunction(MethodDecl m, int tok)
        {
            foreach (var lf in m.LocalFunctions) if (tok >= lf.BodyStart - 1 && tok <= lf.BodyEnd) return true;
            return false;
        }

        // ------------------------------------------------------------ handlers (MediatR / CQRS)
        void RegisterHandlers()
        {
            foreach (var td in Types)
            {
                if (td.Kind == "interface" || td.IsAbstract) continue;
                var reqRefs = HandlerRequestRefs(td);
                if (reqRefs.Count == 0) continue;
                var hm = new List<MethodDecl>();
                foreach (var a in new TypeDecl[] { td }.Concat(Ancestors(td)))
                    foreach (var md in a.Methods) if (HandlerMethodNames.Contains(md.Name) && md.HasBody && !hm.Any(x => x.Name == md.Name && x.Params.Count == md.Params.Count)) hm.Add(md);
                if (hm.Count == 0) continue;
                foreach (var rr in reqRefs)
                {
                    foreach (var req in ResolveTypeRef(rr, td))
                    {
                        List<Target> l;
                        if (!RequestHandlers.TryGetValue(req.Id, out l)) { l = new List<Target>(); RequestHandlers[req.Id] = l; }
                        foreach (var x in hm) if (!l.Any(y => y.M == x && y.Via == td)) l.Add(new Target { M = x, Via = td });
                        RequestTypeNames.Add(req.Name);
                    }
                }
            }
        }

        static TypeRef Subst(TypeRef r, Dictionary<string, TypeRef> map)
        {
            if (r == null) return null;
            TypeRef m;
            if (r.Args.Count == 0 && map.TryGetValue(r.Name, out m)) return m;
            var n = new TypeRef { Name = r.Name, Qualified = r.Qualified };
            foreach (var a in r.Args) n.Args.Add(Subst(a, map));
            return n;
        }

        static bool IsHandlerBaseName(string n)
        {
            return n.EndsWith("Handler") || n.EndsWith("Consumer") || n == "IHandleMessages" || n.EndsWith("HandlerBase");
        }

        // primer argumento generico de cada interfaz *Handler<T>, sustituyendo parametros de clases base genericas
        List<TypeRef> HandlerRequestRefs(TypeDecl td)
        {
            var r = new List<TypeRef>();
            var queue = new Queue<KeyValuePair<KeyValuePair<TypeRef, TypeDecl>, Dictionary<string, TypeRef>>>();
            foreach (var b in td.Bases) queue.Enqueue(new KeyValuePair<KeyValuePair<TypeRef, TypeDecl>, Dictionary<string, TypeRef>>(new KeyValuePair<TypeRef, TypeDecl>(b, td), new Dictionary<string, TypeRef>()));
            var seen = new HashSet<string>();
            int guard = 0;
            while (queue.Count > 0 && guard++ < 200)
            {
                var it = queue.Dequeue();
                var b = Subst(it.Key.Key, it.Value);
                var ctx = it.Key.Value;
                if (b.Args.Count > 0 && IsHandlerBaseName(b.Name))
                {
                    var a0 = b.Args[0];
                    if (!td.TypeParams.Contains(a0.Name) && TypesByName.ContainsKey(a0.Name) && !r.Any(x => x.ToString() == a0.ToString())) r.Add(a0);
                }
                foreach (var bd in ResolveTypeRef(b, ctx))
                {
                    if (!seen.Add(bd.Id + "|" + b.ToString())) continue;
                    var map = new Dictionary<string, TypeRef>();
                    for (int i = 0; i < bd.TypeParams.Count && i < b.Args.Count; i++) map[bd.TypeParams[i]] = b.Args[i];
                    foreach (var bb in bd.Bases) queue.Enqueue(new KeyValuePair<KeyValuePair<TypeRef, TypeDecl>, Dictionary<string, TypeRef>>(new KeyValuePair<TypeRef, TypeDecl>(bb, bd), map));
                }
            }
            return r;
        }

        // ------------------------------------------------------------ opciones (IOptions<T>) y secciones
        void FindOptionsBindings()
        {
            foreach (var td in Types)
            {
                foreach (var kv in td.Members)
                {
                    if (kv.Key != "SectionName" && kv.Key != "Section" && kv.Key != "ConfigSection") continue;
                    string v = ConstValue(kv.Value);
                    if (v != null) OptionsSections[td.Name] = v;
                }
            }
            foreach (var m in Methods)
            {
                if (!m.HasBody) continue;
                var t = m.File.Toks; var mt = m.File.Match;
                for (int k = m.BodyStart; k < m.BodyEnd && k < t.Count; k++)
                {
                    if (!IsI(t, k)) continue;
                    string x = t[k].Text;
                    if ((x == "IOptions" || x == "IOptionsMonitor" || x == "IOptionsSnapshot" || x == "Configure" || x == "AddOptions" || x == "Get" || x == "Bind") && IsP(t, k + 1, "<"))
                    {
                        int g = P(m.File).SkipGeneric(k + 1);
                        if (g < 0 || !IsI(t, k + 2)) continue;
                        string tn = t[g - 1].Kind == TokKind.Ident ? t[g - 1].Text : t[k + 2].Text;
                        if (!TypesByName.ContainsKey(tn)) continue;
                        if (x != "Get" && x != "Bind") OptionsClasses.Add(tn);
                        // seccion: GetSection("X") en la misma sentencia, o BindConfiguration("X")
                        int stmtEnd = P(m.File).FindStmtEnd(k, m.BodyEnd);
                        int stmtStart = k;
                        while (stmtStart > m.BodyStart && !IsP(t, stmtStart - 1, ";") && !IsP(t, stmtStart - 1, "{") && !IsP(t, stmtStart - 1, "}")) stmtStart--;
                        for (int q = stmtStart; q < stmtEnd; q++)
                        {
                            if (IsI(t, q) && (t[q].Text == "GetSection" || t[q].Text == "BindConfiguration") && IsP(t, q + 1, "(") && mt[q + 1] > q)
                            {
                                var ev = EvalStringExpr(m.File, q + 2, mt[q + 1], m.Owner, true);
                                if (ev != null && !OptionsSections.ContainsKey(tn)) OptionsSections[tn] = ev.Value;
                                if (x == "Get" || x == "Bind") OptionsClasses.Add(tn);
                                break;
                            }
                        }
                    }
                }
            }
            foreach (var tn in OptionsSections.Keys) OptionsClasses.Add(tn);
            foreach (var td in Types)
            {
                foreach (var mv in td.Members.Values)
                    if (mv.Type != null && (mv.Type.Name == "IOptions" || mv.Type.Name == "IOptionsMonitor" || mv.Type.Name == "IOptionsSnapshot") && mv.Type.Args.Count == 1) OptionsClasses.Add(mv.Type.Args[0].Name);
                if (td.PrimaryCtor != null)
                    foreach (var p in td.PrimaryCtor)
                        if (p.Type != null && (p.Type.Name == "IOptions" || p.Type.Name == "IOptionsMonitor" || p.Type.Name == "IOptionsSnapshot") && p.Type.Args.Count == 1) OptionsClasses.Add(p.Type.Args[0].Name);
            }
            foreach (var m in Methods)
                foreach (var p in m.Params)
                    if (p.Type != null && (p.Type.Name == "IOptions" || p.Type.Name == "IOptionsMonitor" || p.Type.Name == "IOptionsSnapshot") && p.Type.Args.Count == 1) OptionsClasses.Add(p.Type.Args[0].Name);
        }

        // valor de configuracion de "recv.Propiedad" cuando recv es una clase de opciones. ambiguous = varias secciones posibles.
        public ConfigEntry OptionsValue(TypeSet recv, string leaf, out bool ambiguous)
        {
            ambiguous = false;
            foreach (var td in recv.Types)
            {
                if (!OptionsClasses.Contains(td.Name)) continue;
                string sec;
                if (OptionsSections.TryGetValue(td.Name, out sec))
                {
                    ConfigEntry ce;
                    if (ConfigByPath.TryGetValue(sec + ":" + leaf, out ce)) return ce;
                    return null;
                }
                List<ConfigEntry> l;
                if (!ConfigByLeaf.TryGetValue(leaf, out l)) return null;
                // seccion desconocida: por convencion, una seccion con el nombre de la clase
                string conv = td.Name.EndsWith("Options") ? td.Name.Substring(0, td.Name.Length - 7) : td.Name.EndsWith("Settings") ? td.Name.Substring(0, td.Name.Length - 8) : td.Name;
                var byConv = l.Where(c => c.Path.Equals(conv + ":" + leaf, StringComparison.OrdinalIgnoreCase) || c.Path.Equals(td.Name + ":" + leaf, StringComparison.OrdinalIgnoreCase)).ToList();
                if (byConv.Count == 1) return byConv[0];
                if (l.Count == 1) return l[0];
                ambiguous = true;
                return null;
            }
            return null;
        }

        // ------------------------------------------------------------ ancestros
        public List<TypeRef> AllBaseRefs(TypeDecl td)
        {
            var r = new List<TypeRef>();
            var seen = new HashSet<string>();
            var q = new Queue<TypeDecl>(); q.Enqueue(td);
            seen.Add(td.Id);
            while (q.Count > 0)
            {
                var x = q.Dequeue();
                foreach (var b in x.Bases)
                {
                    r.Add(b);
                    foreach (var bt in ResolveTypeRef(b, x))
                        if (seen.Add(bt.Id)) q.Enqueue(bt);
                }
            }
            return r;
        }

        public HashSet<string> GetAncestorNames(TypeDecl td)
        {
            HashSet<string> s;
            if (AncestorNames.TryGetValue(td.Id, out s)) return s;
            s = new HashSet<string>(StringComparer.Ordinal);
            AncestorNames[td.Id] = s;
            foreach (var b in AllBaseRefs(td)) s.Add(b.Name);
            return s;
        }

        public List<TypeDecl> Ancestors(TypeDecl td)
        {
            List<TypeDecl> r;
            if (AncestorCache.TryGetValue(td.Id, out r)) return r;
            r = new List<TypeDecl>();
            AncestorCache[td.Id] = r;
            var seen = new HashSet<string>(); seen.Add(td.Id);
            var q = new Queue<TypeDecl>(); q.Enqueue(td);
            while (q.Count > 0)
            {
                var x = q.Dequeue();
                foreach (var b in x.Bases)
                    foreach (var bt in ResolveTypeRef(b, x))
                        if (seen.Add(bt.Id)) { r.Add(bt); q.Enqueue(bt); }
            }
            return r;
        }

        public bool DerivesFrom(TypeDecl td, string baseName)
        {
            return GetAncestorNames(td).Contains(baseName);
        }

        public bool IsSubtypeOf(TypeDecl td, TypeDecl baseT)
        {
            if (td == null || baseT == null) return false;
            if (td == baseT) return true;
            return Ancestors(td).Contains(baseT);
        }

        // ------------------------------------------------------------ tipos
        public List<TypeDecl> ResolveTypeName(string name, TypeDecl ctx)
        {
            List<TypeDecl> c;
            // alias: using Repo = A.B.ClientesRepository;
            string aliasTarget;
            if (name != null && ctx != null && ctx.Aliases != null && ctx.Aliases.TryGetValue(name, out aliasTarget))
            {
                var aq = ResolveQualifiedNoAlias(aliasTarget.Split('.').ToList());
                if (aq.Count > 0) return aq;
            }
            if (name == null || !TypesByName.TryGetValue(name, out c)) return new List<TypeDecl>();
            if (c.Count == 1 || ctx == null) return c;
            ctx = ctx.MergedInto ?? ctx;
            string cns = ctx.Namespace ?? "";
            // 1) el mismo tipo, anidados en el contexto o hermanos anidados del mismo tipo contenedor
            var tier = c.Where(x => x == ctx || x.FullName.StartsWith(ctx.FullName + ".")
                || (x.Outer != null && (ctx.FullName == x.Outer.FullName || ctx.FullName.StartsWith(x.Outer.FullName + ".")))).ToList();
            if (tier.Count > 0) return tier;
            // 2) mismo namespace (solo tipos de primer nivel)
            tier = c.Where(x => x.Outer == null && (x.Namespace ?? "") == cns).ToList();
            if (tier.Count > 0) return tier;
            // 3) namespaces contenedores
            tier = c.Where(x => x.Outer == null && (x.Namespace ?? "").Length > 0 && cns.StartsWith(x.Namespace + ".")).ToList();
            if (tier.Count > 0) return tier;
            // 4) usings del archivo
            tier = c.Where(x => x.Outer == null && ctx.Usings.Contains(x.Namespace ?? "")).ToList();
            if (tier.Count > 0) return tier;
            return c;
        }

        public List<TypeDecl> ResolveTypeRef(TypeRef tr, TypeDecl ctx)
        {
            if (tr == null) return new List<TypeDecl>();
            if (tr.Qualified != null && tr.Qualified.Contains("."))
            {
                List<TypeDecl> all;
                if (TypesByName.TryGetValue(tr.Name, out all))
                {
                    var q = all.Where(x => x.FullName == tr.Qualified || x.FullName.EndsWith("." + tr.Qualified)).ToList();
                    if (q.Count > 0) return q;
                }
            }
            return ResolveTypeName(tr.Name, ctx);
        }

        // tipo por nombre calificado "A.B.Tipo" (namespace y/o tipos contenedores)
        public List<TypeDecl> ResolveQualified(List<string> names, TypeDecl ctx)
        {
            if (names.Count == 0) return new List<TypeDecl>();
            if (names.Count == 1) return ResolveTypeName(names[0], ctx);
            string aliasTarget;
            if (ctx != null && ctx.Aliases != null && ctx.Aliases.TryGetValue(names[0], out aliasTarget))
                names = aliasTarget.Split('.').Concat(names.Skip(1)).ToList();
            return ResolveQualifiedNoAlias(names);
        }

        List<TypeDecl> ResolveQualifiedNoAlias(List<string> names)
        {
            if (names.Count == 0) return new List<TypeDecl>();
            string qual = string.Join(".", names.ToArray());
            List<TypeDecl> all;
            if (TypesByName.TryGetValue(names[names.Count - 1], out all))
            {
                var q = all.Where(x => x.FullName == qual || x.FullName.EndsWith("." + qual)).ToList();
                if (q.Count > 0) return q;
            }
            return new List<TypeDecl>();
        }

        // ------------------------------------------------------------ miembros
        public MemberVar FindMember(TypeDecl td, string name)
        {
            if (td == null) return null;
            MemberVar mv;
            if (td.Members.TryGetValue(name, out mv)) return mv;
            foreach (var a in Ancestors(td)) if (a.Members.TryGetValue(name, out mv)) return mv;
            return null;
        }

        public IEnumerable<TypeDecl> SelfAndOuters(TypeDecl td)
        {
            var x = td;
            while (x != null) { yield return x.MergedInto ?? x; x = x.Outer; }
        }

        // Metodos con ese nombre en td y sus ancestros. Un override/new en un tipo mas derivado oculta
        // la version de los ancestros con la misma cantidad de parametros.
        public List<MethodDecl> MethodsNamed(TypeDecl td, string name, bool withAncestors)
        {
            var r = new List<MethodDecl>();
            var covered = new HashSet<int>();
            foreach (var md in td.Methods) if (md.Name == name && !md.IsLocalFunction) { r.Add(md); if (md.HasBody || td.Kind != "interface") covered.Add(md.Params.Count); }
            if (!withAncestors) return r;
            foreach (var a in Ancestors(td))
            {
                var here = new List<int>();
                foreach (var md in a.Methods)
                {
                    if (md.Name != name || md.IsLocalFunction || r.Contains(md)) continue;
                    if (covered.Contains(md.Params.Count) && (a.Kind == "interface" || md.IsVirtual || md.IsAbstract || md.IsOverride || !md.HasBody || td.Kind != "interface")) continue;
                    r.Add(md);
                    if (md.HasBody) here.Add(md.Params.Count);
                }
                foreach (var h in here) covered.Add(h);
            }
            return r;
        }

        static bool ArgcOk(MethodDecl md, int argc, bool ext)
        {
            if (argc < 0) return true;
            int total = md.Params.Count - (ext && md.IsExtension ? 1 : 0);
            int req = md.Params.Count(p => !p.HasDefault && !p.IsParams) - (ext && md.IsExtension ? 1 : 0);
            bool hasParams = md.Params.Any(p => p.IsParams);
            if (argc < req) return false;
            if (argc > total && !hasParams) return false;
            return true;
        }

        public List<MethodDecl> FilterArgc(List<MethodDecl> l, int argc, bool ext)
        {
            var f = l.Where(x => ArgcOk(x, argc, ext)).ToList();
            return f.Count > 0 ? f : l;
        }

        List<Target> FilterArgcT(List<Target> l, int argc, bool ext)
        {
            var f = l.Where(x => ArgcOk(x.M, argc, ext)).ToList();
            return f.Count > 0 ? f : l;
        }

        static void AddT(List<Target> r, MethodDecl m, TypeDecl via, bool inferred)
        {
            if (r.Any(x => x.M == m && x.Via == via)) return;
            r.Add(new Target { M = m, Via = via, Inferred = inferred });
        }

        // bases de un tipo con los argumentos genericos sustituidos a lo largo de la jerarquia:
        // ClientesRepository : Repository<Cliente>, Repository<T> : IRepository<T>  =>  IRepository<Cliente>
        Dictionary<string, List<TypeRef>> substBaseCache = new Dictionary<string, List<TypeRef>>();
        public List<TypeRef> SubstitutedBaseRefs(TypeDecl td)
        {
            List<TypeRef> r;
            if (substBaseCache.TryGetValue(td.Id, out r)) return r;
            r = new List<TypeRef>();
            substBaseCache[td.Id] = r;
            var queue = new Queue<KeyValuePair<KeyValuePair<TypeRef, TypeDecl>, Dictionary<string, TypeRef>>>();
            foreach (var b in td.Bases) queue.Enqueue(new KeyValuePair<KeyValuePair<TypeRef, TypeDecl>, Dictionary<string, TypeRef>>(new KeyValuePair<TypeRef, TypeDecl>(b, td), new Dictionary<string, TypeRef>()));
            var seen = new HashSet<string>();
            int guard = 0;
            while (queue.Count > 0 && guard++ < 300)
            {
                var it = queue.Dequeue();
                var b = Subst(it.Key.Key, it.Value);
                r.Add(b);
                foreach (var bd in ResolveTypeRef(b, it.Key.Value))
                {
                    if (!seen.Add(bd.Id + "|" + b.ToString())) continue;
                    var map = new Dictionary<string, TypeRef>();
                    for (int i = 0; i < bd.TypeParams.Count && i < b.Args.Count; i++) map[bd.TypeParams[i]] = b.Args[i];
                    foreach (var bb in bd.Bases) queue.Enqueue(new KeyValuePair<KeyValuePair<TypeRef, TypeDecl>, Dictionary<string, TypeRef>>(new KeyValuePair<TypeRef, TypeDecl>(bb, bd), map));
                }
            }
            return r;
        }

        // true si el implementador "it" es compatible con los argumentos genericos cerrados del receptor (IRepository<Producto>)
        bool GenericCompatible(TypeDecl it, TypeDecl recvType, List<TypeRef> recvRefs)
        {
            if (recvRefs == null) return true;
            var rr = recvRefs.FirstOrDefault(x => x.Name == recvType.Name && x.Args.Count > 0);
            if (rr == null) return true;
            bool any = false;
            foreach (var b in SubstitutedBaseRefs(it))
            {
                if (b.Name != recvType.Name || b.Args.Count != rr.Args.Count) continue;
                any = true;
                bool ok = true;
                for (int i = 0; i < b.Args.Count; i++)
                {
                    string ba = b.Args[i].Name, ra = rr.Args[i].Name;
                    if (ba == ra) continue;
                    // parametro generico propio del implementador (Repository<T> sin cerrar)
                    if (it.TypeParams.Contains(ba)) continue;
                    ok = false; break;
                }
                if (ok) return true;
            }
            return !any;
        }

        public List<Target> FindMethods(List<TypeDecl> types, string name, int argc, List<TypeRef> recvRefs, bool noPoly)
        {
            var r = new List<Target>();
            foreach (var td in types)
            {
                var direct = MethodsNamed(td, name, true);
                // las implementaciones explicitas (IFoo.Metodo) solo son accesibles a traves de la interfaz
                if (td.Kind != "interface") direct = direct.Where(x => x.ExplicitIface == null).ToList();
                foreach (var md in direct) AddT(r, md, td, false);
                if (noPoly) continue;
                bool poly = td.Kind == "interface" || td.IsAbstract || direct.Any(x => x.IsAbstract || x.IsVirtual || !x.HasBody);
                if (!poly) continue;
                List<TypeDecl> impls;
                if (!ImplementorsById.TryGetValue(td.Id, out impls)) continue;
                var concrete = impls.Where(x => x != td && x.Kind != "interface" && !x.IsAbstract).ToList();
                if (concrete.Count == 0) concrete = impls.Where(x => x != td && x.Kind != "interface").ToList();
                foreach (var it in concrete)
                {
                    if (!GenericCompatible(it, td, recvRefs)) continue;
                    var ms = MethodsNamed(it, name, true);
                    bool hasExplicit = td.Kind == "interface" && ms.Any(x => x.ExplicitIface == td.Name);
                    foreach (var md in ms)
                    {
                        if (md.ExplicitIface != null && md.ExplicitIface != td.Name) continue;
                        if (hasExplicit && md.ExplicitIface == null && md.Owner == it) continue;   // la explicita gana via esa interfaz
                        AddT(r, md, it, false);
                    }
                }
            }
            return FilterArgcT(r, argc, false);
        }

        // ------------------------------------------------------------ locales
        public Dictionary<string, LocalInfo> Locals(MethodDecl m)
        {
            Dictionary<string, LocalInfo> d;
            if (localsCache.TryGetValue(m.Id, out d)) return d;
            d = new Dictionary<string, LocalInfo>(StringComparer.Ordinal);
            localsCache[m.Id] = d;
            if (!m.HasBody) return d;
            var t = m.File.Toks;
            var p = P(m.File);
            for (int k = m.BodyStart; k < m.BodyEnd && k < t.Count; k++)
            {
                if (t[k].Kind != TokKind.Ident) continue;
                string x = t[k].Text;
                if (x == "var" && k + 2 < t.Count && t[k + 1].Kind == TokKind.Ident && t[k + 2].Kind == TokKind.Ident && t[k + 2].Text == "in" && k > 1 && IsP(t, k - 1, "(") && IsI(t, k - 2) && t[k - 2].Text == "foreach")
                {
                    if (!d.ContainsKey(t[k + 1].Text)) d[t[k + 1].Text] = new LocalInfo { ForeachTok = k + 3 };
                    continue;
                }
                if (x == "var" && k + 2 < t.Count && t[k + 1].Kind == TokKind.Ident && IsP(t, k + 2, "="))
                {
                    if (d.ContainsKey(t[k + 1].Text)) continue;
                    var li = new LocalInfo { ExprTok = k + 3 };
                    // var x = new T(...): tipo declarado conocido
                    if (IsI(t, k + 3) && t[k + 3].Text == "new" && !IsP(t, k + 4, "("))
                    {
                        int j2 = k + 4;
                        var ntr = p.ParseTypeRef(ref j2);
                        if (ntr != null) { li.Type = ntr; li.Known = true; }
                    }
                    d[t[k + 1].Text] = li;
                    continue;
                }
                if (U.IsKeyword(x) && !PredefTypes.Contains(x)) continue;
                if (k > 0 && (IsP(t, k - 1, ".") || IsP(t, k - 1, "?."))) continue;
                bool candidate = PredefTypes.Contains(x) || TypesByName.ContainsKey(x) || IsP(t, k + 1, "<") || (k + 1 < t.Count && t[k + 1].Kind == TokKind.Ident && char.IsUpper(x[0]));
                if (!candidate) continue;
                int j = k;
                var tr = p.ParseTypeRef(ref j);
                if (tr == null || j >= t.Count || t[j].Kind != TokKind.Ident || U.IsKeyword(t[j].Text)) continue;
                if (IsP(t, j + 1, "=") || IsP(t, j + 1, ";") || IsP(t, j + 1, ")") || IsP(t, j + 1, ",") || (j + 1 < t.Count && t[j + 1].Kind == TokKind.Ident && t[j + 1].Text == "in"))
                {
                    if (!d.ContainsKey(t[j].Text)) d[t[j].Text] = new LocalInfo { Type = tr, Known = true, ExprTok = IsP(t, j + 1, "=") ? j + 2 : -1 };
                }
            }
            return d;
        }

        bool IsTypeParam(MethodDecl m, string name)
        {
            for (var x = m; x != null; x = x.Parent) if (x.TypeParams.Contains(name)) return true;
            for (var o = m.Owner; o != null; o = o.Outer) if (o.TypeParams.Contains(name)) return true;
            return false;
        }

        bool HasOpenArgs(MethodDecl m, TypeRef tr)
        {
            if (tr == null) return false;
            foreach (var a in tr.Args) if (IsTypeParam(m, a.Name) || HasOpenArgs(m, a)) return true;
            return false;
        }

        TypeSet FromRef(MethodDecl m, TypeRef tr, TypeDecl ctx)
        {
            var ts = new TypeSet();
            if (tr == null) return ts;
            ts.Known = true;
            var u = U.UnwrapRef(tr);
            ts.Refs.Add(u);
            ts.Types.AddRange(ResolveTypeRef(u, ctx));
            if (m != null && HasOpenArgs(m, u)) ts.OpenGeneric = true;
            return ts;
        }

        // tipo de un nombre simple dentro del contexto de un metodo
        public TypeSet TypeOfName(MethodDecl m, string name, int depth)
        {
            var ts = new TypeSet();
            if (depth > 8) return ts;
            var owner = m.Owner;
            if (name == "this") { ts.Known = true; ts.IsThis = true; if (owner != null) ts.Types.Add(owner); return ts; }
            if (name == "base") { ts.Known = true; ts.IsBase = true; if (owner != null) foreach (var b in owner.Bases) ts.Types.AddRange(ResolveTypeRef(b, owner).Where(x => x.Kind != "interface")); return ts; }
            for (var scope = m; scope != null; scope = scope.Parent)
            {
                LocalInfo li;
                if (scope.HasBody && Locals(scope).TryGetValue(name, out li))
                {
                    if (li.Cache != null) return li.Cache;
                    if (li.Resolving) return ts;
                    li.Resolving = true;
                    TypeSet res;
                    if (li.Type != null) res = FromRef(scope, li.Type, owner);
                    else if (li.ForeachTok >= 0)
                    {
                        var elem = ForeachElement(scope, li.ForeachTok);
                        res = elem != null ? FromRef(scope, elem, owner) : new TypeSet();
                    }
                    else res = InferExprType(scope, li.ExprTok, depth + 1);
                    li.Cache = res; li.Resolving = false; li.Known = res.Known;
                    return res;
                }
                foreach (var p in scope.Params)
                    if (p.Name == name) { if (p.Type == null) return ts; return FromRef(scope, p.Type, owner); }
            }
            foreach (var o in SelfAndOuters(owner))
            {
                var mv = FindMember(o, name);
                if (mv != null) { if (mv.Type == null) return ts; return FromRef(m, mv.Type, mv.Owner); }
                if (o.PrimaryCtor != null)
                    foreach (var p in o.PrimaryCtor)
                        if (p.Name == name) { if (p.Type == null) return ts; return FromRef(m, p.Type, o); }
            }
            var tn = ResolveTypeName(name, owner);
            if (tn.Count > 0) { ts.Types.AddRange(tn); ts.Known = true; ts.Static = true; return ts; }
            return ts;
        }

        public TypeSet InferExprType(MethodDecl m, int k, int depth)
        {
            var t = m.File.Toks;
            var r = new TypeSet();
            if (k < 0 || k >= t.Count || depth > 8) return r;
            if (IsI(t, k) && t[k].Text == "await") k++;
            if (IsI(t, k) && t[k].Text == "new")
            {
                int j = k + 1;
                if (IsP(t, j, "(")) return r;
                var tr = P(m.File).ParseTypeRef(ref j);
                return FromRef(m, tr, m.Owner);
            }
            if (IsP(t, k, "("))
            {
                int c = m.File.Match[k];
                if (c > k + 1 && IsI(t, k + 1) && (IsI(t, c + 1) || IsP(t, c + 1, "(")))
                {
                    int j = k + 1;
                    var tr = P(m.File).ParseTypeRef(ref j);
                    if (tr != null && j == c) return FromRef(m, tr, m.Owner);
                }
                return r;
            }
            if (!IsI(t, k)) return r;
            var segs = ParseChainForward(m.File, k);
            if (segs.Count == 0) return r;
            var last = segs[segs.Count - 1];
            if (last.IsCall && last.GenericArgs.Count > 0 && (last.Name.StartsWith("Get") || last.Name.StartsWith("Resolve") || last.Name.StartsWith("Create")))
                return FromRef(m, last.GenericArgs[0], m.Owner);
            // "x as T" al final de la expresion
            return ResolveChain(m, segs, depth + 1);
        }

        public List<Seg> ParseChainForward(SourceFile f, int k)
        {
            var t = f.Toks;
            var segs = new List<Seg>();
            var p = P(f);
            int guard = 0;
            while (IsI(t, k) && guard++ < 20)
            {
                var s = new Seg { Name = t[k].Text };
                k++;
                if (IsP(t, k, "<")) { int g = p.SkipGeneric(k); if (g > 0 && IsP(t, g + 1, "(")) { s.GenericArgs = ParseGenArgs(f, k, g); k = g + 1; } }
                if (IsP(t, k, "(") && f.Match[k] > k) { s.IsCall = true; s.Argc = CountArgs(f, k); s.ArgOpen = k; k = f.Match[k] + 1; }
                while (IsP(t, k, "[") && f.Match[k] > k) { k = f.Match[k] + 1; s.IsIndexer = true; }
                if (IsP(t, k, "!")) k++;
                segs.Add(s);
                if (IsP(t, k, ".") || IsP(t, k, "?.")) { k++; continue; }
                break;
            }
            return segs;
        }

        List<TypeRef> ParseGenArgs(SourceFile f, int open, int close)
        {
            var r = new List<TypeRef>();
            var p = P(f);
            int j = open + 1;
            while (j < close)
            {
                int b = j;
                var tr = p.ParseTypeRef(ref j);
                if (tr != null) r.Add(tr);
                int depth = 0;
                while (j < close)
                {
                    if (IsP(f.Toks, j, "<")) depth++;
                    else if (IsP(f.Toks, j, ">")) depth--;
                    else if (IsP(f.Toks, j, ",") && depth <= 0) break;
                    j++;
                }
                j++;
                if (j <= b) j = b + 1;
            }
            return r;
        }

        public int CountArgs(SourceFile f, int open)
        {
            var t = f.Toks; var m = f.Match;
            int close = m[open];
            if (close < 0) return -1;
            if (close == open + 1) return 0;
            int n = 1;
            var p = P(f);
            for (int k = open + 1; k < close; k++)
            {
                if (t[k].Kind != TokKind.Punct) continue;
                string x = t[k].Text;
                if ((x == "(" || x == "[" || x == "{") && m[k] > k) { k = m[k]; continue; }
                if (x == "<" && k > 0 && t[k - 1].Kind == TokKind.Ident) { int g = p.SkipGeneric(k); if (g > 0 && g < close && (IsP(t, g + 1, "(") || IsP(t, g + 1, "."))) { k = g; continue; } }
                if (x == ",") n++;
            }
            return n;
        }

        static readonly HashSet<string> DictNames = new HashSet<string>(new string[] { "IDictionary", "Dictionary", "IReadOnlyDictionary", "ConcurrentDictionary", "SortedDictionary", "ImmutableDictionary" });
        static readonly HashSet<string> CollectionNames = new HashSet<string>(new string[] {
            "IEnumerable", "IList", "List", "ICollection", "IReadOnlyList", "IReadOnlyCollection", "HashSet", "ISet", "IAsyncEnumerable", "Collection", "ObservableCollection", "IQueryable", "ImmutableList", "ImmutableArray" });

        // tipo del elemento al indexar una coleccion
        TypeSet IndexerElement(MethodDecl m, TypeSet cur, TypeDecl ctx)
        {
            foreach (var r in cur.Refs)
            {
                if (DictNames.Contains(r.Name) && r.Args.Count == 2) return FromRef(m, r.Args[1], ctx);
                if (CollectionNames.Contains(r.Name) && r.Args.Count == 1) return FromRef(m, r.Args[0], ctx);
            }
            // arreglo T[]: el TypeRef ya es el elemento
            if (cur.Types.Count > 0) return cur;
            return new TypeSet { Known = cur.Known };
        }

        public TypeSet ResolveChain(MethodDecl m, List<Seg> segs, int depth)
        {
            var cur = new TypeSet();
            if (segs.Count == 0 || depth > 10) return cur;
            int start = 1;
            var s0 = segs[0];
            if (s0.CastType != null) cur = FromRef(m, s0.CastType, m.Owner);
            else if (s0.Inner != null) cur = ResolveChain(m, s0.Inner, depth + 1);
            else if (s0.IsCall)
            {
                var targets = new List<MethodDecl>();
                foreach (var lf in LocalFunctionsInScope(m)) if (lf.Name == s0.Name) targets.Add(lf);
                if (targets.Count == 0) foreach (var o in SelfAndOuters(m.Owner)) { targets.AddRange(MethodsNamed(o, s0.Name, true)); if (targets.Count > 0) break; }
                if (targets.Count > 0) cur = ReturnTypes(targets);
                else
                {
                    // delegado Func<T> guardado en un campo/parametro/local: _factory()
                    var dt = TypeOfName(m, s0.Name, depth + 1);
                    var fr = dt.Refs.FirstOrDefault(x => x.Name == "Func" && x.Args.Count > 0);
                    if (fr != null) cur = FromRef(m, fr.Args[fr.Args.Count - 1], m.Owner);
                }
            }
            else
            {
                cur = TypeOfName(m, s0.Name, depth);
                if (cur.Types.Count == 0 && !cur.Known && segs.Count > 1)
                {
                    // nombre calificado por namespace: A.B.Tipo.Miembro
                    for (int i = 1; i < segs.Count; i++)
                    {
                        if (segs[i].IsCall) break;
                        var q = ResolveQualified(segs.Take(i + 1).Select(x => x.Name).ToList(), m.Owner);
                        if (q.Count > 0) { cur = new TypeSet { Types = q, Known = true, Static = true }; start = i + 1; break; }
                    }
                }
            }
            if (s0.IsIndexer) cur = IndexerElement(m, cur, m.Owner);
            for (int i = start; i < segs.Count; i++)
            {
                var s = segs[i];
                var next = new TypeSet();
                if (cur.Types.Count == 0) { next.Known = false; return next; }
                if (s.IsCall)
                {
                    var targets = FindMethods(cur.Types, s.Name, s.Argc, cur.Refs, cur.OpenGeneric);
                    next = ReturnTypes(targets.Select(x => x.M).ToList());
                }
                else
                {
                    bool found = false;
                    foreach (var td in cur.Types)
                    {
                        var mv = FindMember(td, s.Name);
                        if (mv != null)
                        {
                            found = true;
                            if (mv.Type == null) continue;
                            var ft = FromRef(m, mv.Type, mv.Owner);
                            next.Known = true; next.Types.AddRange(ft.Types); next.Refs.AddRange(ft.Refs);
                            continue;
                        }
                        List<TypeDecl> nl;
                        var nested = NestedByOuter.TryGetValue(td.Id, out nl) ? nl.Where(x => x.Name == s.Name).ToList() : new List<TypeDecl>();
                        if (nested.Count > 0) { found = true; next.Types.AddRange(nested); next.Known = true; next.Static = true; }
                    }
                    // Lazy<T>.Value, IOptions<T>.Value, Task<T>.Result: identidad (el tipo ya se desenvolvio)
                    if (!found && (s.Name == "Value" || s.Name == "CurrentValue" || s.Name == "Result")) next = cur;
                }
                if (s.IsIndexer) next = IndexerElement(m, next, m.Owner);
                cur = next;
            }
            return cur;
        }

        TypeSet ReturnTypes(List<MethodDecl> targets)
        {
            var ts = new TypeSet();
            foreach (var md in targets)
            {
                if (md.ReturnType == null) continue;
                ts.Known = true;
                var u = U.UnwrapRef(md.ReturnType);
                ts.Refs.Add(u);
                foreach (var x in ResolveTypeRef(u, md.Owner)) if (!ts.Types.Contains(x)) ts.Types.Add(x);
            }
            return ts;
        }

        public IEnumerable<MethodDecl> LocalFunctionsInScope(MethodDecl m)
        {
            for (var x = m; x != null; x = x.Parent) foreach (var lf in x.LocalFunctions) yield return lf;
        }

        // ------------------------------------------------------------ llamadas
        public List<CallSite> Calls(MethodDecl m)
        {
            List<CallSite> r;
            if (callsCache.TryGetValue(m.Id, out r)) return r;
            r = new List<CallSite>();
            callsCache[m.Id] = r;
            if (!m.HasBody) return r;
            var f = m.File; var t = f.Toks; var mt = f.Match;
            var p = P(f);
            for (int k = m.BodyStart; k < m.BodyEnd && k < t.Count; k++)
            {
                var tk = t[k];
                if (tk.Kind != TokKind.Ident) continue;
                if ((tk.Text == "nameof" || tk.Text == "typeof" || tk.Text == "sizeof" || tk.Text == "default") && IsP(t, k + 1, "(") && mt[k + 1] > k)
                {
                    k = mt[k + 1];
                    continue;
                }
                if (tk.Text == "new")
                {
                    int j = k + 1;
                    if (IsP(t, j, "(") || IsP(t, j, "[") || IsP(t, j, "{")) continue;
                    var tr = p.ParseTypeRef(ref j);
                    if (tr != null)
                    {
                        r.Add(new CallSite { IsNew = true, NewType = tr, Name = tr.Name, Tok = k, Line = tk.Line });
                        k = j - 1;   // new A.B.Query(...): el nombre del tipo no es una llamada
                    }
                    continue;
                }
                if (U.IsKeyword(tk.Text) && tk.Text != "this" && tk.Text != "base") continue;
                if (k > 0 && IsI(t, k - 1) && t[k - 1].Text == "new") continue;
                int a = k + 1;
                if (IsP(t, a, "<")) { int g = p.SkipGeneric(a); if (g > 0 && IsP(t, g + 1, "(")) a = g + 1; }
                bool afterDot = IsP(t, k - 1, ".") || IsP(t, k - 1, "?.");
                if (IsP(t, a, "(") && mt[a] > a)
                {
                    // descartar declaraciones de funciones locales: "Tipo Nombre(" con tipo previo, o seguidas de { / => / where
                    if (!afterDot && k > m.BodyStart && IsI(t, k - 1) && !U.IsKeyword(t[k - 1].Text) && IsDeclContext(t, k - 1)) continue;
                    int after = mt[a] + 1;
                    if (!afterDot && (IsP(t, after, "{") || IsP(t, after, "=>") || (IsI(t, after) && t[after].Text == "where"))) continue;
                    if (tk.Text == "this" || tk.Text == "base") continue;
                    var cs = new CallSite { Name = tk.Text, Tok = k, Line = tk.Line, ArgOpen = a, Argc = CountArgs(f, a) };
                    if (afterDot) { int st; cs.Receiver = WalkBack(f, k - 1, out st); }
                    r.Add(cs);
                    continue;
                }
                // acceso a propiedad o indexador con cuerpo: _repo.Total / _repo.Limite = v / _repo[id]
                if (PropertyNames.Count > 0)
                {
                    int st;
                    bool isIdx = IsP(t, k + 1, "[") && mt[k + 1] > k && PropertyNames.Contains("this[]") && !U.IsKeyword(tk.Text);
                    if (isIdx)
                    {
                        var recvI = afterDot ? WalkBack(f, k - 1, out st) : new List<Seg>();
                        recvI.Add(new Seg { Name = tk.Text });
                        r.Add(new CallSite { Name = "this[]", Receiver = recvI, Argc = -2, Tok = k, Line = tk.Line, PropertyAccess = IsAssign(t, mt[k + 1] + 1) ? "set" : "get" });
                    }
                    bool initProp = !afterDot && (IsP(t, k - 1, "{") || IsP(t, k - 1, ",")) && IsAssign(t, k + 1);
                    if (PropertyNames.Contains(tk.Text) && !initProp && (afterDot || (!Locals(m).ContainsKey(tk.Text) && !m.Params.Any(pp => pp.Name == tk.Text))))
                    {
                        var recvP = afterDot ? WalkBack(f, k - 1, out st) : new List<Seg>();
                        r.Add(new CallSite { Name = tk.Text, Receiver = recvP, Argc = -2, Tok = k, Line = tk.Line, PropertyAccess = (!isIdx && IsAssign(t, k + 1)) ? "set" : "get" });
                        continue;
                    }
                }
                // grupo de metodos pasado como delegado: (X) , X ,  obj.X , Tipo.X
                if ((IsP(t, k + 1, ",") || IsP(t, k + 1, ")")) && !U.IsKeyword(tk.Text)
                    && (afterDot || (!Locals(m).ContainsKey(tk.Text) && !m.Params.Any(pp => pp.Name == tk.Text))))
                {
                    int chainStart = k;
                    var recv = new List<Seg>();
                    if (afterDot) recv = WalkBack(f, k - 1, out chainStart);
                    if (IsP(t, chainStart - 1, "(") || IsP(t, chainStart - 1, ","))
                        r.Add(new CallSite { Name = tk.Text, Tok = k, Line = tk.Line, IsMethodGroup = true, Receiver = recv, Argc = -1 });
                }
            }
            return r;
        }

        static bool IsAssign(List<Token> t, int i)
        {
            if (i < 0 || i >= t.Count || t[i].Kind != TokKind.Punct) return false;
            string x = t[i].Text;
            return x == "=" || x == "+=" || x == "-=" || x == "*=" || x == "/=" || x == "%=" || x == "&=" || x == "|=" || x == "^=" || x == "??=";
        }

        static bool IsDeclContext(List<Token> t, int typeTok)
        {
            // "async Task Foo(" o "int Foo(" o "static void Foo(" al inicio de sentencia
            int p = typeTok - 1;
            while (p >= 0 && t[p].Kind == TokKind.Ident && (t[p].Text == "static" || t[p].Text == "async" || t[p].Text == "unsafe")) p--;
            if (p < 0) return true;
            if (t[p].Kind == TokKind.Punct && (t[p].Text == ";" || t[p].Text == "{" || t[p].Text == "}")) return true;
            if (t[p].Kind == TokKind.Punct && t[p].Text == ">") return true;
            return false;
        }

        public List<Seg> WalkBack(SourceFile f, int dotTok)
        {
            int st;
            return WalkBack(f, dotTok, out st);
        }

        // Receptor de una llamada "a.b().c[0].Metodo(": devuelve los segmentos y el token donde empieza la cadena
        public List<Seg> WalkBack(SourceFile f, int dotTok, out int startTok)
        {
            var t = f.Toks; var mt = f.Match;
            var segs = new List<Seg>();
            int p = dotTok;
            startTok = dotTok + 1;
            int guard = 0;
            while ((IsP(t, p, ".") || IsP(t, p, "?.")) && guard++ < 20)
            {
                int q = p - 1;
                if (IsP(t, q, "!")) q--;
                if (IsP(t, q, ")") && mt[q] >= 0)
                {
                    int open = mt[q];
                    int ni = open - 1;
                    if (IsP(t, ni, ">"))
                    {
                        int depth = 0; int z = ni;
                        for (; z >= 0; z--) { if (IsP(t, z, ">")) depth++; else if (IsP(t, z, "<")) { depth--; if (depth == 0) break; } }
                        ni = z - 1;
                    }
                    if (IsI(t, ni) && (!U.IsKeyword(t[ni].Text) || t[ni].Text == "this" || t[ni].Text == "base"))
                    {
                        segs.Insert(0, new Seg { Name = t[ni].Text, IsCall = true, Argc = CountArgs(f, open), ArgOpen = open });
                        startTok = ni; p = ni - 1; continue;
                    }
                    // expresion entre parentesis: ((T)x).M()  (x as T).M()  (await x.GetAsync()).M()
                    segs.Insert(0, ParenSeg(f, open, q));
                    startTok = open;
                    break;
                }
                if (IsP(t, q, "]") && mt[q] >= 0)
                {
                    int open = mt[q];
                    if (IsI(t, open - 1)) { segs.Insert(0, new Seg { Name = t[open - 1].Text, IsIndexer = true }); startTok = open - 1; p = open - 2; continue; }
                    break;
                }
                if (IsI(t, q)) { segs.Insert(0, new Seg { Name = t[q].Text }); startTok = q; p = q - 1; continue; }
                break;
            }
            return segs;
        }

        Seg ParenSeg(SourceFile f, int open, int close)
        {
            var t = f.Toks; var mt = f.Match;
            var p = P(f);
            var s = new Seg { Name = "(expr)" };
            int k = open + 1;
            // cast: ((T)x)
            if (IsP(t, k, "(") && mt[k] > k && mt[k] < close)
            {
                int j = k + 1;
                var tr = p.ParseTypeRef(ref j);
                if (tr != null && j == mt[k]) { s.CastType = tr; return s; }
            }
            // x as T
            for (int q = k; q < close; q++)
            {
                if ((IsP(t, q, "(") || IsP(t, q, "[")) && mt[q] > q) { q = mt[q]; continue; }
                if (IsI(t, q) && t[q].Text == "as")
                {
                    int j = q + 1;
                    var tr = p.ParseTypeRef(ref j);
                    if (tr != null) { s.CastType = tr; return s; }
                }
            }
            if (IsI(t, k) && t[k].Text == "await") k++;
            s.Inner = ParseChainForward(f, k);
            return s;
        }

        // Resuelve una llamada (compatibilidad): solo metodos
        public List<MethodDecl> ResolveCall(MethodDecl m, CallSite cs, out bool inferred, out bool unresolvedRepoName)
        {
            var tg = ResolveTargets(m, cs, null, out inferred, out unresolvedRepoName);
            var r = new List<MethodDecl>();
            foreach (var x in tg) if (!r.Contains(x.M)) r.Add(x.M);
            return r;
        }

        // Resuelve una llamada a metodos del repo. thisType = tipo concreto del objeto actual (despacho virtual).
        public List<Target> ResolveTargets(MethodDecl m, CallSite cs, TypeDecl thisType, out bool inferred, out bool unresolvedRepoName)
        {
            var r = ResolveTargetsCore(m, cs, thisType, out inferred, out unresolvedRepoName);
            if (cs.PropertyAccess != null) return r.Where(x => x.M.AccessorKind == cs.PropertyAccess).ToList();
            return r.Where(x => x.M.AccessorKind == null).ToList();
        }

        List<Target> ResolveTargetsCore(MethodDecl m, CallSite cs, TypeDecl thisType, out bool inferred, out bool unresolvedRepoName)
        {
            inferred = false; unresolvedRepoName = false;
            var r = new List<Target>();
            if (cs.IsNew) return r;
            var owner = m.Owner != null ? (m.Owner.MergedInto ?? m.Owner) : null;
            bool thisRecv = cs.Receiver.Count == 1 && cs.Receiver[0].Name == "this" && !cs.Receiver[0].IsCall;
            if (cs.Receiver.Count == 0 || thisRecv)
            {
                if (cs.Receiver.Count == 0)
                {
                    foreach (var lf in LocalFunctionsInScope(m)) if (lf.Name == cs.Name) AddT(r, lf, thisType ?? owner, false);
                    if (r.Count > 0) return r;
                }
                foreach (var o in SelfAndOuters(owner))
                {
                    var l = MethodsNamed(o, cs.Name, true);
                    if (l.Count == 0) continue;
                    var res = FilterArgc(l, cs.Argc, false);
                    bool virt = res.Any(x => x.IsAbstract || x.IsVirtual || x.IsOverride);
                    if (virt && thisType != null && o == owner && IsSubtypeOf(thisType, o))
                    {
                        // despacho virtual sobre el tipo concreto conocido
                        foreach (var md in FilterArgc(MethodsNamed(thisType, cs.Name, true), cs.Argc, false)) AddT(r, md, thisType, false);
                        if (r.Count > 0) return r;
                    }
                    foreach (var md in res) AddT(r, md, thisType != null && o == owner ? thisType : o, false);
                    if (virt && (thisType == null || o != owner))
                    {
                        List<TypeDecl> impls;
                        if (ImplementorsById.TryGetValue(o.Id, out impls))
                            foreach (var it in impls) foreach (var md in FilterArgc(MethodsNamed(it, cs.Name, false), cs.Argc, false)) AddT(r, md, it, false);
                    }
                    return r;
                }
                if (owner != null)
                    foreach (var u in owner.Usings)
                    {
                        if (!u.StartsWith("static:")) continue;
                        string tn = u.Substring(7); int dot = tn.LastIndexOf('.'); if (dot >= 0) tn = tn.Substring(dot + 1);
                        foreach (var td in ResolveTypeName(tn, owner)) foreach (var md in MethodsNamed(td, cs.Name, true)) AddT(r, md, td, false);
                    }
                return FilterArgcT(r, cs.Argc, false);
            }
            var recv = ResolveChain(m, cs.Receiver, 0);
            if (recv.Types.Count > 0)
            {
                if (cs.Receiver.Count == 1 && cs.Receiver[0].Name == "base" && !cs.Receiver[0].IsCall)
                {
                    // base.Metodo(): enlace estatico a la implementacion de la clase base
                    foreach (var bt in recv.Types)
                    {
                        var l = FilterArgc(MethodsNamed(bt, cs.Name, true), cs.Argc, false);
                        foreach (var md in l) AddT(r, md, thisType ?? owner, false);
                        if (r.Count > 0) break;
                    }
                    return r;
                }
                r = FindMethods(recv.Types, cs.Name, cs.IsMethodGroup ? -1 : cs.Argc, recv.Refs, recv.OpenGeneric);
                if (r.Count > 0) return r;
                // metodo de extension sobre un tipo del repo
                List<MethodDecl> ext;
                if (ExtensionsByName.TryGetValue(cs.Name, out ext))
                {
                    var names = new HashSet<string>(recv.Types.Select(x => x.Name));
                    foreach (var td in recv.Types) foreach (var n in GetAncestorNames(td)) names.Add(n);
                    var e2 = ext.Where(x => x.Params[0].Type != null && (names.Contains(x.Params[0].Type.Name) || IsTypeParam(x, x.Params[0].Type.Name))).ToList();
                    foreach (var md in FilterArgc(e2, cs.Argc, true)) AddT(r, md, md.Owner, false);
                }
                return r;
            }
            if (cs.IsMethodGroup) return r;
            // receptor de tipo conocido pero externo (List, IDbConnection...): solo metodos de extension del repo
            List<MethodDecl> exts;
            if (ExtensionsByName.TryGetValue(cs.Name, out exts))
            {
                string extTypeName = null;
                if (recv.Refs.Count > 0) extTypeName = recv.Refs[0].Name;
                else if (recv.Known && cs.Receiver.Count == 1) extTypeName = DeclaredTypeName(m, cs.Receiver[0].Name);
                var e2 = exts.Where(x => x.Params[0].Type != null && (extTypeName == null || ExtTypeMatches(x, x.Params[0].Type.Name, extTypeName))).ToList();
                if (e2.Count > 0 && e2.Count <= 4)
                {
                    inferred = extTypeName == null;
                    foreach (var md in FilterArgc(e2, cs.Argc, true)) AddT(r, md, md.Owner, inferred);
                    return r;
                }
            }
            if (recv.Known)
            {
                // coleccion externa indexada (diccionario de estrategias): avisar si el nombre existe en el repo
                if (cs.Receiver.Any(x => x.IsIndexer) && MethodsByName.ContainsKey(cs.Name) && cs.PropertyAccess == null) unresolvedRepoName = true;
                return r;
            }
            // acceso a propiedad con receptor desconocido: no se resuelve por nombre (evita falsos positivos)
            if (cs.PropertyAccess != null) return r;
            // pista por nombre del receptor: _ventasRepository -> VentasRepository / IVentasRepository
            string hint = cs.Receiver[cs.Receiver.Count - 1].Name.TrimStart('_');
            if (hint.StartsWith("m_") || hint.StartsWith("s_")) hint = hint.Substring(2);
            if (hint.Length > 2 && hint != "(expr)")
            {
                var hinted = Types.Where(x => string.Equals(x.Name, hint, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Name, "I" + hint, StringComparison.OrdinalIgnoreCase)).ToList();
                if (hinted.Count > 0)
                {
                    r = FindMethods(hinted, cs.Name, cs.Argc, null, false);
                    if (r.Count > 0) { inferred = true; foreach (var x in r) x.Inferred = true; return r; }
                }
            }
            List<MethodDecl> byName;
            if (MethodsByName.TryGetValue(cs.Name, out byName))
            {
                var cand = FilterArgc(byName.Where(x => !x.IsExtension && !x.IsLocalFunction).ToList(), cs.Argc, false);
                int owners = cand.Select(x => x.Owner).Distinct().Count();
                if (cand.Count > 0 && owners <= 3) { inferred = true; foreach (var md in cand) AddT(r, md, md.Owner, true); return r; }
                if (cand.Count > 0) unresolvedRepoName = true;
            }
            return r;
        }

        static readonly Dictionary<string, string[]> ExtCompat = new Dictionary<string, string[]> {
            { "IDbConnection", new string[] { "OracleConnection", "SqlConnection", "DbConnection", "NpgsqlConnection", "SqliteConnection", "MySqlConnection", "IDbConnection" } },
            { "DbConnection", new string[] { "OracleConnection", "SqlConnection", "NpgsqlConnection", "SqliteConnection", "MySqlConnection", "DbConnection" } },
            { "IDbTransaction", new string[] { "OracleTransaction", "SqlTransaction", "DbTransaction", "IDbTransaction" } },
            { "DbTransaction", new string[] { "OracleTransaction", "SqlTransaction", "DbTransaction" } },
            { "IDbCommand", new string[] { "OracleCommand", "SqlCommand", "DbCommand", "IDbCommand" } } };

        bool ExtTypeMatches(MethodDecl ext, string thisTypeName, string recvTypeName)
        {
            if (thisTypeName == recvTypeName) return true;
            if (IsTypeParam(ext, thisTypeName)) return true;
            string[] compat;
            if (ExtCompat.TryGetValue(thisTypeName, out compat) && compat.Contains(recvTypeName)) return true;
            if (thisTypeName == "object") return true;
            return false;
        }

        // Tipo del elemento en "foreach (var x in coleccion)"
        TypeRef ForeachElement(MethodDecl m, int k)
        {
            var t = m.File.Toks;
            if (IsI(t, k) && t[k].Text == "this" && IsP(t, k + 1, ".")) k += 2;
            if (!IsI(t, k)) return null;
            if (!(IsP(t, k + 1, ")") || IsP(t, k + 1, "?"))) return null;
            var tr = DeclaredTypeRef(m, t[k].Text);
            if (tr == null) return null;
            if (tr.Args.Count == 1 && CollectionNames.Contains(tr.Name)) return tr.Args[0];
            if (tr.Args.Count == 2 && DictNames.Contains(tr.Name)) return null;
            if (tr.Args.Count == 0 && !CollectionNames.Contains(tr.Name)) return tr; // arreglos T[] (el sufijo [] no se conserva en TypeRef)
            return null;
        }

        TypeRef DeclaredTypeRef(MethodDecl m, string name)
        {
            for (var scope = m; scope != null; scope = scope.Parent)
            {
                LocalInfo li;
                if (scope.HasBody && Locals(scope).TryGetValue(name, out li) && li.Type != null) return li.Type;
                foreach (var p in scope.Params) if (p.Name == name && p.Type != null) return p.Type;
            }
            foreach (var o in SelfAndOuters(m.Owner))
            {
                var mv = FindMember(o, name);
                if (mv != null && mv.Type != null) return mv.Type;
                if (o.PrimaryCtor != null) foreach (var p in o.PrimaryCtor) if (p.Name == name && p.Type != null) return p.Type;
            }
            return null;
        }

        string DeclaredTypeName(MethodDecl m, string name)
        {
            var tr = DeclaredTypeRef(m, name);
            return tr != null ? U.UnwrapRef(tr).Name : null;
        }

        // Handlers MediatR/CQRS disparados desde el metodo: "new Request(...)" o una variable del tipo request
        // pasada a un metodo de despacho (Send, Publish, Dispatch, Ejecutar...).
        public List<Target> LinkedHandlers(MethodDecl m)
        {
            var r = new List<Target>();
            if (RequestHandlers.Count == 0 || !m.HasBody) return r;
            var f = m.File; var t = f.Toks; var mt = f.Match;
            var p = P(f);
            for (int k = m.BodyStart; k < m.BodyEnd && k < t.Count; k++)
            {
                if (!IsI(t, k)) continue;
                string x = t[k].Text;
                if ((x == "nameof" || x == "typeof") && IsP(t, k + 1, "(") && mt[k + 1] > k) { k = mt[k + 1]; continue; }
                if (x == "new")
                {
                    int j = k + 1;
                    if (!IsI(t, j)) continue;
                    var names = new List<string>();
                    while (IsI(t, j)) { names.Add(t[j].Text); if (IsP(t, j + 1, ".") && IsI(t, j + 2)) j += 2; else { j++; break; } }
                    if (!RequestTypeNames.Contains(names[names.Count - 1])) continue;
                    foreach (var req in ResolveQualified(names, m.Owner)) AddHandlers(r, req, m);
                    continue;
                }
                // metodo de despacho con argumentos que son variables de tipo request
                if (DispatchRx.IsMatch(x) && (IsP(t, k + 1, "(") || IsP(t, k + 1, "<")))
                {
                    int a = k + 1;
                    if (IsP(t, a, "<")) { int g = p.SkipGeneric(a); if (g < 0) continue; a = g + 1; }
                    if (!IsP(t, a, "(") || mt[a] < a) continue;
                    for (int q = a + 1; q < mt[a]; q++)
                    {
                        if (!IsI(t, q) || IsP(t, q - 1, ".")) continue;
                        if (!(IsP(t, q + 1, ",") || IsP(t, q + 1, ")"))) continue;
                        var tr = DeclaredTypeRef(m, t[q].Text);
                        if (tr == null)
                        {
                            LocalInfo li;
                            if (Locals(m).TryGetValue(t[q].Text, out li)) { var ts = TypeOfName(m, t[q].Text, 0); foreach (var td in ts.Types) AddHandlers(r, td, m); }
                            continue;
                        }
                        foreach (var req in ResolveTypeRef(U.UnwrapRef(tr), m.Owner)) AddHandlers(r, req, m);
                    }
                }
            }
            return r;
        }

        void AddHandlers(List<Target> r, TypeDecl req, MethodDecl m)
        {
            List<Target> h;
            if (!RequestHandlers.TryGetValue(req.Id, out h)) return;
            foreach (var x in h) if (x.M != m && !r.Any(y => y.M == x.M && y.Via == x.Via)) r.Add(x);
        }

        // ------------------------------------------------------------ constantes
        // Busca una constante/campo string por cadena de nombres en el contexto del metodo
        public MemberVar ResolveMemberChain(List<string> names, TypeDecl ctx)
        {
            if (names.Count == 0) return null;
            if (names.Count == 1 || (names.Count == 2 && names[0] == "this"))
            {
                string n = names[names.Count - 1];
                foreach (var o in SelfAndOuters(ctx))
                {
                    var mv = FindMember(o, n);
                    if (mv != null) return mv;
                }
                if (ctx != null)
                    foreach (var u in ctx.Usings)
                    {
                        if (!u.StartsWith("static:")) continue;
                        string tn = u.Substring(7); int dot = tn.LastIndexOf('.'); if (dot >= 0) tn = tn.Substring(dot + 1);
                        foreach (var td in ResolveTypeName(tn, ctx)) { var mv = FindMember(td, n); if (mv != null) return mv; }
                    }
                return null;
            }
            string member = names[names.Count - 1];
            var typeNames = names.Take(names.Count - 1).ToList();
            var cands = ResolveQualified(typeNames, ctx);
            if (cands.Count == 0) cands = ResolveTypeName(typeNames[typeNames.Count - 1], ctx);
            foreach (var td in cands) { var mv = FindMember(td, member); if (mv != null) return mv; }
            return null;
        }

        // Valor string de una constante (null si no es una expresion string evaluable)
        public string ConstValue(MemberVar mv)
        {
            if (mv == null || mv.InitStart < 0) return null;
            string v;
            if (constCache.TryGetValue(mv.Id, out v)) { if (v != null && TrackRefs) MarkConstRefs(mv, 0); return v; }
            if (constResolving.Contains(mv.Id)) return null;
            if (!(mv.IsConst || mv.IsStatic || mv.IsReadonly || mv.IsProperty || (mv.Type != null && mv.Type.Name == "string"))) { constCache[mv.Id] = null; return null; }
            constResolving.Add(mv.Id);
            var ev = EvalStringExpr(mv.File, mv.InitStart, mv.InitEnd, mv.Owner, true);
            constResolving.Remove(mv.Id);
            v = ev != null && ev.Complete ? ev.Value : null;
            constCache[mv.Id] = v;
            if (v != null && TrackRefs) MarkConstRefs(mv, 0);
            return v;
        }

        // marca la constante y las que usa su inicializador como referenciadas
        void MarkConstRefs(MemberVar mv, int depth)
        {
            if (mv == null || depth > 10 || !ReferencedConsts.Add(mv.Id) && depth > 0) return;
            ReferencedConsts.Add(mv.Id);
            if (mv.InitStart < 0) return;
            var t = mv.File.Toks;
            for (int k = mv.InitStart; k < mv.InitEnd && k < t.Count; k++)
            {
                if (t[k].Kind == TokKind.Str)
                {
                    foreach (var part in t[k].Lit.Parts)
                        if (part.IsHole && Regex.IsMatch(part.Text ?? "", @"^[A-Za-z_][\w.]*$"))
                        { var h = ResolveMemberChain(part.Text.Split('.').ToList(), mv.Owner); if (h != null) MarkConstRefs(h, depth + 1); }
                    continue;
                }
                if (!IsI(t, k) || IsP(t, k - 1, ".")) continue;
                var names = new List<string>();
                int j = k;
                while (IsI(t, j)) { names.Add(t[j].Text); if (IsP(t, j + 1, ".") && IsI(t, j + 2)) j += 2; else { j++; break; } }
                var r = ResolveMemberChain(names, mv.Owner);
                if (r != null && r != mv) MarkConstRefs(r, depth + 1);
                k = j - 1;
            }
        }

        public class StrEval
        {
            public StringBuilder Sb = new StringBuilder();
            public List<FragPiece> Pieces = new List<FragPiece>();
            public bool Complete = true;
            public bool HasLiteral;
            public int EndTok;
            public MemberVar OuterConst;   // constante referenciada directamente (la de mas afuera)
            public string Value { get { return Sb.ToString(); } }
        }

        // Evalua una concatenacion de literales/constantes en [s,e). requireAll: toda la expresion debe ser string.
        // localCtx: si se indica, tambien se resuelven variables locales y valores de configuracion.
        public StrEval EvalStringExpr(SourceFile f, int s, int e, TypeDecl ctx, bool requireAll)
        {
            return EvalStringExpr(f, s, e, ctx, requireAll, null, 0);
        }

        public StrEval EvalStringExpr(SourceFile f, int s, int e, TypeDecl ctx, bool requireAll, MethodDecl localCtx, int depth)
        {
            var t = f.Toks;
            var ev = new StrEval();
            if (depth > 12) return null;
            int k = s;
            bool expect = true;
            int guard = 0;
            while (k < e && guard++ < 20000)
            {
                if (expect)
                {
                    if (t[k].Kind == TokKind.Str)
                    {
                        AppendLiteral(ev, f, t[k], ctx, localCtx);
                        ev.HasLiteral = true; k++; expect = false; continue;
                    }
                    if (IsP(t, k, "(") && f.Match[k] > k && f.Match[k] < e)
                    {
                        var inner = EvalStringExpr(f, k + 1, f.Match[k], ctx, true, localCtx, depth + 1);
                        if (inner == null) { ev.Complete = false; break; }
                        Merge(ev, inner);
                        k = f.Match[k] + 1; expect = false; continue;
                    }
                    if (IsI(t, k))
                    {
                        if (t[k].Text == "nameof" && IsP(t, k + 1, "(") && f.Match[k + 1] > 0)
                        {
                            int c = f.Match[k + 1];
                            string last = null;
                            for (int q = k + 2; q < c; q++) if (IsI(t, q)) last = t[q].Text;
                            ev.Sb.Append(last ?? ""); k = c + 1; expect = false; continue;
                        }
                        var names = new List<string>();
                        int j = k;
                        while (IsI(t, j)) { names.Add(t[j].Text); if (IsP(t, j + 1, ".") && IsI(t, j + 2)) j += 2; else { j++; break; } }
                        // configuracion: _config["A:B"], _config.GetSection("A")["B"], _config.GetValue<string>("A:B")
                        int cfgEnd;
                        var ce = ConfigAccess(f, k, e, out cfgEnd);
                        if (ce != null)
                        {
                            int baseOff = ev.Sb.Length;
                            ev.Sb.Append(ce.Value);
                            ev.Pieces.Add(new FragPiece { ValueStart = baseOff, ValueLength = ce.Value.Length, File = ce.File, Line = ce.Line, CountsLines = false, ExternalKind = "config" });
                            ev.HasLiteral = true; k = cfgEnd; expect = false; continue;
                        }
                        if (IsP(t, j, "(") || IsP(t, j, "<") || IsP(t, j, "["))
                        {
                            if (requireAll) { ev.Complete = false; break; }
                            // llamada u otra expresion: marcador de posicion
                            int z = j;
                            while (z < e && (IsP(t, z, "(") || IsP(t, z, "[") || IsP(t, z, "<")))
                            {
                                if (IsP(t, z, "<")) { int g = P(f).SkipGeneric(z); if (g < 0) break; z = g + 1; continue; }
                                if (f.Match[z] < 0) break; z = f.Match[z] + 1;
                                if (IsP(t, z, ".") && IsI(t, z + 1)) z += 2;
                            }
                            ev.Sb.Append("{?}"); ev.Complete = false; k = z; expect = false; continue;
                        }
                        string special = null;
                        string joined = string.Join(".", names.ToArray());
                        if (joined == "Environment.NewLine") special = "\n";
                        else if (joined == "string.Empty" || joined == "String.Empty") special = "";
                        if (special != null) { ev.Sb.Append(special); k = j; expect = false; continue; }
                        // variable local (solo si se pidio contexto de metodo)
                        if (localCtx != null && names.Count == 1)
                        {
                            LocalInfo li;
                            if (localCtx.HasBody && Locals(localCtx).TryGetValue(names[0], out li) && li.ExprTok >= 0)
                            {
                                int le = P(localCtx.File).FindStmtEnd(li.ExprTok, localCtx.BodyEnd);
                                var sub = EvalStringExpr(localCtx.File, li.ExprTok, le, ctx, true, localCtx, depth + 1);
                                if (sub != null && sub.Complete) { Merge(ev, sub); k = j; expect = false; continue; }
                            }
                        }
                        var mv = (localCtx != null && names.Count == 1 && Locals(localCtx).ContainsKey(names[0])) ? null : ResolveMemberChain(names, ctx);
                        string cv = mv != null ? ConstValue(mv) : null;
                        if (cv != null)
                        {
                            var sub = EvalStringExpr(mv.File, mv.InitStart, mv.InitEnd, mv.Owner, true);
                            if (sub != null)
                            {
                                int baseOff = ev.Sb.Length;
                                foreach (var pc in sub.Pieces)
                                    ev.Pieces.Add(new FragPiece { ValueStart = baseOff + pc.ValueStart, ValueLength = pc.ValueLength, File = pc.File, Line = pc.Line, CountsLines = pc.CountsLines, Const = pc.Const ?? mv, ExternalKind = pc.ExternalKind });
                                ev.Sb.Append(sub.Value);
                                ev.HasLiteral = true;
                                if (ev.OuterConst == null) ev.OuterConst = mv;
                            }
                            k = j; expect = false; continue;
                        }
                        // opciones: _opts.Value.Listar / _opts.CurrentValue.Listar
                        if (localCtx != null && names.Count >= 2)
                        {
                            var recvSegs = names.Take(names.Count - 1).Select(x => new Seg { Name = x }).ToList();
                            var recv = ResolveChain(localCtx, recvSegs, 0);
                            bool amb;
                            var oe = OptionsValue(recv, names[names.Count - 1], out amb);
                            if (oe != null)
                            {
                                int baseOff = ev.Sb.Length;
                                ev.Sb.Append(oe.Value);
                                ev.Pieces.Add(new FragPiece { ValueStart = baseOff, ValueLength = oe.Value.Length, File = oe.File, Line = oe.Line, CountsLines = false, ExternalKind = "config" });
                                ev.HasLiteral = true; k = j; expect = false; continue;
                            }
                        }
                        if (requireAll) { ev.Complete = false; break; }
                        ev.Sb.Append("{?}"); ev.Complete = false; k = j; expect = false; continue;
                    }
                    ev.Complete = false; break;
                }
                else
                {
                    if (IsP(t, k, "+")) { k++; expect = true; continue; }
                    break;
                }
            }
            ev.EndTok = k;
            if (requireAll && k < e) ev.Complete = false;
            if (!ev.HasLiteral) return null;
            return ev;
        }

        // _config["A:B"] / _config.GetSection("A")["B"] / _config.GetValue<string>("A:B") / GetConnectionString no
        public ConfigEntry ConfigAccess(SourceFile f, int k, int e, out int endTok)
        {
            endTok = k;
            if (ConfigByPath.Count == 0) return null;
            var t = f.Toks; var mt = f.Match;
            int j = k;
            var path = new List<string>();
            int guard = 0;
            while (j < e && guard++ < 20)
            {
                if (IsI(t, j))
                {
                    if ((t[j].Text == "GetSection" || t[j].Text == "GetValue" || t[j].Text == "GetRequiredSection") )
                    {
                        int a = j + 1;
                        if (IsP(t, a, "<")) { int g = P(f).SkipGeneric(a); if (g < 0) return null; a = g + 1; }
                        if (!IsP(t, a, "(") || mt[a] < a || a + 1 >= t.Count || t[a + 1].Kind != TokKind.Str) return null;
                        path.Add(t[a + 1].Lit.PlainValue());
                        j = mt[a] + 1;
                        if (IsP(t, j, ".") && IsI(t, j + 1)) { j++; continue; }
                        continue;
                    }
                    j++;
                    if (IsP(t, j, ".") && IsI(t, j + 1)) { j++; continue; }
                    continue;
                }
                if (IsP(t, j, "[") && mt[j] == j + 2 && t[j + 1].Kind == TokKind.Str)
                {
                    path.Add(t[j + 1].Lit.PlainValue());
                    j = mt[j] + 1;
                    if (IsP(t, j, ".") && IsI(t, j + 1) && t[j + 1].Text == "Value") j += 2;
                    continue;
                }
                break;
            }
            if (path.Count == 0) return null;
            ConfigEntry ce;
            if (ConfigByPath.TryGetValue(string.Join(":", path.ToArray()), out ce)) { endTok = j; return ce; }
            return null;
        }

        static void Merge(StrEval ev, StrEval inner)
        {
            int baseOff = ev.Sb.Length;
            foreach (var pc in inner.Pieces) ev.Pieces.Add(new FragPiece { ValueStart = baseOff + pc.ValueStart, ValueLength = pc.ValueLength, File = pc.File, Line = pc.Line, CountsLines = pc.CountsLines, Const = pc.Const, ExternalKind = pc.ExternalKind });
            ev.Sb.Append(inner.Value);
            ev.HasLiteral |= inner.HasLiteral;
            ev.Complete &= inner.Complete;
            if (ev.OuterConst == null) ev.OuterConst = inner.OuterConst;
        }

        public void AppendLiteral(StrEval ev, SourceFile f, Token tk, TypeDecl ctx, MethodDecl localCtx)
        {
            int startOff = ev.Sb.Length;
            foreach (var part in tk.Lit.Parts)
            {
                if (!part.IsHole) { ev.Sb.Append(part.Text); continue; }
                string hv = ResolveHole(part.Text, ctx, localCtx);
                if (hv == null) { ev.Sb.Append("{?}"); ev.Complete = false; }
                else ev.Sb.Append(hv);
            }
            ev.Pieces.Add(new FragPiece { ValueStart = startOff, ValueLength = ev.Sb.Length - startOff, File = f, Line = tk.Line, CountsLines = tk.Lit.CountsLines });
        }

        public void AppendLiteral(StrEval ev, SourceFile f, Token tk, TypeDecl ctx)
        {
            AppendLiteral(ev, f, tk, ctx, null);
        }

        static readonly Regex CfgIndexRx = new Regex(@"^[\w.]+\s*\[\s*""([^""]+)""\s*\]$");
        static readonly Regex CfgSectionRx = new Regex(@"^[\w.]+\.GetSection\s*\(\s*""([^""]+)""\s*\)\s*\[\s*""([^""]+)""\s*\]$");

        public string ResolveHole(string expr, TypeDecl ctx, MethodDecl localCtx)
        {
            if (string.IsNullOrEmpty(expr)) return null;
            var mm = Regex.Match(expr, @"^nameof\s*\(\s*(?:[\w]+\s*\.\s*)*(\w+)\s*\)$");
            if (mm.Success) return mm.Groups[1].Value;
            ConfigEntry ce;
            mm = CfgIndexRx.Match(expr);
            if (mm.Success && ConfigByPath.TryGetValue(mm.Groups[1].Value, out ce)) return ce.Value;
            mm = CfgSectionRx.Match(expr);
            if (mm.Success && ConfigByPath.TryGetValue(mm.Groups[1].Value + ":" + mm.Groups[2].Value, out ce)) return ce.Value;
            if (!Regex.IsMatch(expr, @"^[A-Za-z_][\w]*(\s*\.\s*[A-Za-z_][\w]*)*$")) return null;
            var names = expr.Split('.').Select(x => x.Trim()).ToList();
            if (localCtx != null && names.Count == 1)
            {
                LocalInfo li;
                if (Locals(localCtx).TryGetValue(names[0], out li))
                {
                    if (li.ExprTok < 0) return null;
                    int le = P(localCtx.File).FindStmtEnd(li.ExprTok, localCtx.BodyEnd);
                    var sub = EvalStringExpr(localCtx.File, li.ExprTok, le, ctx, true, localCtx, 1);
                    return sub != null && sub.Complete ? sub.Value : null;
                }
            }
            var mv = ResolveMemberChain(names, ctx);
            if (mv != null) return ConstValue(mv);
            if (localCtx != null && names.Count >= 2)
            {
                var recv = ResolveChain(localCtx, names.Take(names.Count - 1).Select(x => new Seg { Name = x }).ToList(), 0);
                bool amb;
                var oe = OptionsValue(recv, names[names.Count - 1], out amb);
                if (oe != null) return oe.Value;
            }
            return null;
        }

        public string ResolveHole(string expr, TypeDecl ctx)
        {
            return ResolveHole(expr, ctx, null);
        }
    }
}

namespace SPA_NS
{
    // =====================================================================
    //  RECONOCIMIENTO DE NOMBRES DE SP Y ANALISIS DE TEXTO SQL
    // =====================================================================
    public class SpMatcher
    {
        public Regex PkgRx, ObjRx, PkgOnlyRx, DynRx;
        AnalyzerOptions opt;
        const string Id = @"[A-Za-z][\w$#]*";
        static readonly HashSet<string> PseudoCols = new HashSet<string>(new string[] { "NEXTVAL", "CURRVAL" }, StringComparer.OrdinalIgnoreCase);

        public SpMatcher(AnalyzerOptions o)
        {
            opt = o;
            string pk = string.Join("|", o.PackagePrefixes.Where(x => !string.IsNullOrEmpty(x)).Select(x => Regex.Escape(x)).ToArray());
            string ob = string.Join("|", o.ObjectPrefixes.Where(x => !string.IsNullOrEmpty(x)).Select(x => Regex.Escape(x)).ToArray());
            if (pk.Length == 0) pk = "PCK_";
            if (ob.Length == 0) ob = "SP_";
            var ro = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            PkgRx = new Regex(@"(?<![\w$#.])(?:""?(?<schema>" + Id + @")""?\s*\.\s*)?""?(?<pkg>(?:" + pk + @")[\w$#]+)""?\s*\.\s*""?(?<mem>[A-Za-z_][\w$#]*)""?(?:\s*@\s*(?<link>[A-Za-z][\w$#.]*))?", ro);
            ObjRx = new Regex(@"(?<![\w$#.])(?:""?(?<schema>" + Id + @")""?\s*\.\s*)?""?(?<mem>(?:" + ob + @")[\w$#]+)""?(?:\s*@\s*(?<link>[A-Za-z][\w$#.]*))?", ro);
            PkgOnlyRx = new Regex(@"(?<![\w$#.])(?<pkg>(?:" + pk + @")[\w$#]+)(?!\s*\.\s*[A-Za-z_""])", ro);
            // nombres armados en tiempo de ejecucion: PCK_X.{?}  PCK_X.SP_{?}  SP_{?}  {?}.SP_X(
            DynRx = new Regex(@"(?<![\w$#])(?:(?:" + pk + @")[\w$#]*\s*\.\s*[\w$#]*\{\?\}|(?:" + ob + @")[\w$#]*\{\?\}|\{\?\}\s*\.\s*(?:" + ob + @")?[\w$#]*\s*\(|(?:" + pk + @")[\w$#]*\{\?\})", ro);
        }

        public bool IsPackageName(string s)
        {
            if (s == null) return false;
            foreach (var p in opt.PackagePrefixes) if (!string.IsNullOrEmpty(p) && s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static SpName Make(string schema, string pkg, string mem, string link)
        {
            var s = new SpName();
            s.Schema = string.IsNullOrEmpty(schema) ? null : schema.ToUpperInvariant();
            s.Package = string.IsNullOrEmpty(pkg) ? null : pkg.ToUpperInvariant();
            s.Member = mem.ToUpperInvariant();
            s.DbLink = string.IsNullOrEmpty(link) ? null : link.TrimEnd('.');
            s.Key = s.Package != null ? s.Package + "." + s.Member : s.Member;
            return s;
        }

        // Hits en codigo SQL (texto ya sin comentarios ni literales).
        // wholeIsBare: el texto completo es un nombre. allowEnd: un nombre suelto al final del texto cuenta como llamada (bloques/CALL).
        public List<SpHit> FindInCode(string code, bool wholeIsBare, bool allowEnd)
        {
            var hits = new List<SpHit>();
            var spans = new List<int[]>();
            var dyn = new List<int[]>();
            foreach (Match mm in DynRx.Matches(code)) dyn.Add(new int[] { mm.Index, mm.Index + mm.Length });
            foreach (Match mm in PkgRx.Matches(code))
            {
                int after = mm.Index + mm.Length;
                spans.Add(new int[] { mm.Index, after });
                // descartar %TYPE / %ROWTYPE, secuencias y nombres incompletos
                if (after < code.Length && (code[after] == '%' || code[after] == '{')) continue;
                if (PseudoCols.Contains(mm.Groups["mem"].Value)) continue;
                if (dyn.Any(d => mm.Index < d[1] && after > d[0])) continue;
                var sp = Make(mm.Groups["schema"].Value, mm.Groups["pkg"].Value, mm.Groups["mem"].Value, mm.Groups["link"].Value);
                hits.Add(new SpHit { Sp = sp, Offset = mm.Groups["pkg"].Index });
            }
            foreach (Match mm in ObjRx.Matches(code))
            {
                bool overlap = spans.Any(s => mm.Index < s[1] && mm.Index + mm.Length > s[0]);
                if (overlap) continue;
                string schema = mm.Groups["schema"].Value;
                if (IsPackageName(schema)) continue;
                int after = mm.Index + mm.Length;
                if (after < code.Length && code[after] == '{') continue;
                if (dyn.Any(d => mm.Index < d[1] && after > d[0])) continue;
                if (PseudoCols.Contains(mm.Groups["mem"].Value)) continue;
                if (!wholeIsBare)
                {
                    int a = after;
                    while (a < code.Length && char.IsWhiteSpace(code[a])) a++;
                    bool callLike = (a < code.Length && code[a] == '(') || (allowEnd && (a >= code.Length || code[a] == ';'));
                    if (!callLike)
                    {
                        string before = code.Substring(0, mm.Index).TrimEnd();
                        if (Regex.IsMatch(before, @"\b(CALL|EXEC|EXECUTE)$", RegexOptions.IgnoreCase)) callLike = true;
                    }
                    if (!callLike) continue;
                }
                var sp = Make(schema, null, mm.Groups["mem"].Value, mm.Groups["link"].Value);
                hits.Add(new SpHit { Sp = sp, Offset = mm.Groups["mem"].Index });
            }
            hits.Sort((x, y) => x.Offset.CompareTo(y.Offset));
            return hits;
        }

        public bool HasDynamic(string code) { return DynRx.IsMatch(code); }

        // Menciones en comentarios / texto libre. Empareja "PCK_X ... SP_Y" sueltos.
        public List<SpHit> FindInText(string text)
        {
            var hits = new List<SpHit>();
            var spans = new List<int[]>();
            foreach (Match mm in PkgRx.Matches(text))
            {
                spans.Add(new int[] { mm.Index, mm.Index + mm.Length });
                if (PseudoCols.Contains(mm.Groups["mem"].Value)) continue;
                var sp = Make(mm.Groups["schema"].Value, mm.Groups["pkg"].Value, mm.Groups["mem"].Value, mm.Groups["link"].Value);
                hits.Add(new SpHit { Sp = sp, Offset = mm.Index });
            }
            var pkgOnly = new List<Match>();
            foreach (Match mm in PkgOnlyRx.Matches(text))
            {
                if (spans.Any(s => mm.Index >= s[0] && mm.Index < s[1])) continue;
                pkgOnly.Add(mm);
            }
            var distinctPk = pkgOnly.Select(x => x.Groups["pkg"].Value.ToUpperInvariant()).Distinct().ToList();
            foreach (Match mm in ObjRx.Matches(text))
            {
                if (spans.Any(s => mm.Index < s[1] && mm.Index + mm.Length > s[0])) continue;
                string schema = mm.Groups["schema"].Value;
                string pkg = null;
                if (IsPackageName(schema)) { pkg = schema; schema = null; }
                if (pkg == null)
                {
                    Match prev = null;
                    foreach (var p in pkgOnly) if (p.Index < mm.Index) prev = p;
                    if (prev != null) pkg = prev.Groups["pkg"].Value;
                    else if (distinctPk.Count == 1) pkg = distinctPk[0];
                }
                var sp = Make(schema, pkg, mm.Groups["mem"].Value, mm.Groups["link"].Value);
                hits.Add(new SpHit { Sp = sp, Offset = mm.Index });
            }
            hits.Sort((x, y) => x.Offset.CompareTo(y.Offset));
            return hits;
        }

        public bool MatchesPattern(string bare)
        {
            return PkgRx.IsMatch(bare) || ObjRx.IsMatch(bare);
        }
    }

    public class SqlScan
    {
        public string Blank;                 // mismo largo que el texto: comentarios y literales en blanco
        public List<int[]> CommentSpans = new List<int[]>();
        public string Kind;                  // empty / bare / pure / regular / other
        public string Form;                  // bare / call / block / dual / query / other

        static readonly Regex BareRx = new Regex(@"^(?:""?[A-Za-z_][\w$#]*""?\s*\.\s*){0,2}""?[A-Za-z_][\w$#]*""?(?:\s*@\s*[A-Za-z][\w$#.]*)?$", RegexOptions.CultureInvariant);
        static readonly Regex CallKwRx = new Regex(@"^(CALL|EXEC|EXECUTE)\s+", RegexOptions.IgnoreCase);
        static readonly Regex BlockRx = new Regex(@"^(?:DECLARE\b[\s\S]*?)?\bBEGIN\b(?<body>[\s\S]*)\bEND\b\s*[\w$#]*\s*$", RegexOptions.IgnoreCase);
        static readonly Regex CallStmtRx = new Regex(@"^(?:[:\w$#.""]+\s*:=\s*)?(?:""?[\w$#]+""?\s*\.\s*){0,2}""?[\w$#]+""?(?:\s*@\s*[\w$#.]+)?\s*(?:\([\s\S]*\))?$", RegexOptions.IgnoreCase);
        static readonly Regex DualRx = new Regex(@"^SELECT\s+(?<cols>[\s\S]+?)\s+FROM\s+(?:SYS\s*\.\s*)?DUAL\s*$", RegexOptions.IgnoreCase);
        static readonly Regex DualColRx = new Regex(@"^(?:(?:""?[\w$#]+""?\s*\.\s*){1,2}""?[\w$#]+""?(?:\s*@\s*[\w$#.]+)?\s*(?:\([\s\S]*\))?|""?[\w$#]+""?\s*\([\s\S]*\))(?:\s+(?:AS\s+)?""?[\w$#]+""?)?$", RegexOptions.IgnoreCase);
        static readonly Regex RegStartRx = new Regex(@"^\(?\s*(SELECT|INSERT|UPDATE|DELETE|MERGE|WITH|BEGIN|DECLARE|OPEN|TRUNCATE|LOCK)\b", RegexOptions.IgnoreCase);
        static readonly Regex RegAnyRx = new Regex(@"\bSELECT\b[\s\S]+?\bFROM\b|\bINSERT\s+INTO\b|\bUPDATE\s+[\w$#."" ]+?\s+SET\b|\bDELETE\s+FROM\b|\bMERGE\s+INTO\b|\bFROM\s+[\w$#.""]+\s+(?:[\w$#]+\s+)?(?:WHERE|JOIN|INNER|LEFT|RIGHT|ORDER|GROUP)\b", RegexOptions.IgnoreCase);

        public static SqlScan Scan(string v)
        {
            var s = new SqlScan();
            var c = v.ToCharArray();
            int n = v.Length;
            int i = 0;
            while (i < n)
            {
                char ch = v[i];
                if (ch == '-' && i + 1 < n && v[i + 1] == '-')
                {
                    int e = v.IndexOf('\n', i); if (e < 0) e = n;
                    s.CommentSpans.Add(new int[] { i, e });
                    for (int k = i; k < e; k++) if (c[k] != '\n') c[k] = ' ';
                    i = e; continue;
                }
                if (ch == '/' && i + 1 < n && v[i + 1] == '*')
                {
                    int e = v.IndexOf("*/", i + 2, StringComparison.Ordinal); e = e < 0 ? n : e + 2;
                    s.CommentSpans.Add(new int[] { i, e });
                    for (int k = i; k < e; k++) if (c[k] != '\n') c[k] = ' ';
                    i = e; continue;
                }
                if ((ch == 'q' || ch == 'Q') && i + 2 < n && v[i + 1] == '\'' && (i == 0 || !char.IsLetterOrDigit(v[i - 1])))
                {
                    char d = v[i + 2];
                    char close = d == '[' ? ']' : d == '(' ? ')' : d == '{' ? '}' : d == '<' ? '>' : d;
                    int e = v.IndexOf(close.ToString() + "'", i + 3, StringComparison.Ordinal);
                    e = e < 0 ? n : e + 2;
                    for (int k = i; k < e; k++) if (c[k] != '\n') c[k] = ' ';
                    i = e; continue;
                }
                if (ch == '\'')
                {
                    int k = i + 1;
                    while (k < n)
                    {
                        if (v[k] == '\'') { if (k + 1 < n && v[k + 1] == '\'') { k += 2; continue; } break; }
                        k++;
                    }
                    int e = Math.Min(n, k + 1);
                    for (int z = i; z < e; z++) if (c[z] != '\n') c[z] = ' ';
                    i = e; continue;
                }
                i++;
            }
            s.Blank = new string(c);
            s.Kind = Classify(s.Blank, out s.Form);
            return s;
        }

        static string Classify(string blank, out string form)
        {
            string code = blank.Trim();
            code = code.TrimEnd(';', '/', ' ', '\t', '\r', '\n').Trim();
            code = Regex.Replace(code, @"\{\?\}", "X");
            form = "other";
            if (code.Length == 0) return "empty";
            if (BareRx.IsMatch(code)) { form = "bare"; return "bare"; }
            if (CallKwRx.IsMatch(code)) { form = "call"; return "pure"; }
            form = "query";
            var bm = BlockRx.Match(code);
            if (bm.Success)
            {
                var stmts = SplitTop(bm.Groups["body"].Value, ';');
                bool allCalls = true; int n = 0;
                foreach (var st in stmts)
                {
                    string x = st.Trim();
                    if (x.Length == 0) continue;
                    n++;
                    if (!CallStmtRx.IsMatch(x) || Regex.IsMatch(x, @"^(SELECT|INSERT|UPDATE|DELETE|MERGE|OPEN|FOR|IF|LOOP|WHILE|CASE|BEGIN|EXECUTE)\b", RegexOptions.IgnoreCase)) { allCalls = false; break; }
                }
                if (allCalls && n > 0) { form = "block"; return "pure"; }
                return "regular";
            }
            var dm = DualRx.Match(code);
            if (dm.Success)
            {
                var cols = SplitTop(dm.Groups["cols"].Value, ',');
                if (cols.Count > 0 && cols.All(x => DualColRx.IsMatch(x.Trim()))) { form = "dual"; return "pure"; }
                return "regular";
            }
            if (RegStartRx.IsMatch(code)) return "regular";
            if (RegAnyRx.IsMatch(code)) return "regular";
            form = "other";
            return "other";
        }

        public static List<string> SplitTop(string s, char sep)
        {
            var r = new List<string>();
            int depth = 0, last = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == sep && depth <= 0) { r.Add(s.Substring(last, i - last)); last = i + 1; }
            }
            r.Add(s.Substring(last));
            return r;
        }
    }

    // =====================================================================
    //  ANALISIS POR METODO: fragmentos SQL, bloques de comentario, marcadores
    // =====================================================================
    public class MethodAnalysis
    {
        public MethodDecl M;
        public List<Fragment> Fragments = new List<Fragment>();
        public List<MarkerBlock> Blocks = new List<MarkerBlock>();
        public List<Marker> MethodMarkers = new List<Marker>();   // nivel metodo (fuertes)
        public List<Marker> WeakMarkers = new List<Marker>();     // menciones en logs/excepciones
        public List<Marker> ClassMarkers = new List<Marker>();
        public List<Warn> Warnings = new List<Warn>();
    }

    public class FragmentExtractor
    {
        CodeIndex ix;
        SpMatcher sm;
        int fragSeq = 0, markSeq = 0, blockSeq = 0;
        Dictionary<string, MethodAnalysis> cache = new Dictionary<string, MethodAnalysis>();
        Dictionary<string, int> wrapperCache = new Dictionary<string, int>();
        Dictionary<string, List<Marker>> classMarkerCache = new Dictionary<string, List<Marker>>();

        static readonly Regex MsgCallee = new Regex(@"^(Log\w*|Write\w*|Trace\w*|Debug\w*|Info|Information|Warn|Warning|Error|Fatal|Critical|Verbose|Print\w*|Assert\w*|Fail|AddError|AddModelError|Append\w*Message)$");
        static readonly Regex NonCmdCallee = new Regex(@"^(Ok|BadRequest|NotFound|Problem|ValidationProblem|Conflict|Created\w*|Accepted\w*|Content|Json|StatusCode|Redirect\w*|Unauthorized|Forbid|NoContent|UnprocessableEntity|Add|AddParameter|AddWithValue|Equals|Contains|StartsWith|EndsWith|IndexOf|LastIndexOf|Replace|Split|Trim\w*|ToUpper\w*|ToLower\w*|SetValue|SetString|SetInt\w*|Compare|CompareTo|Match|IsMatch|Matches|Matched|Header\w*|AddHeader|TryAdd\w*|Remove|Exists|GetValueOrDefault|TryGetValue|ContainsKey|Parse|TryParse|Cookie\w*|Claim|HasClaim|IsInRole|Redirect|Field|Get\w*Value|Select|Where|Any|All|First\w*|Single\w*|Last\w*|Count|OrderBy\w*|GroupBy)$");
        public static readonly Regex DapperExec = new Regex(@"^(Query|QueryAsync|QueryFirst|QueryFirstAsync|QueryFirstOrDefault|QueryFirstOrDefaultAsync|QuerySingle|QuerySingleAsync|QuerySingleOrDefault|QuerySingleOrDefaultAsync|QueryMultiple|QueryMultipleAsync|QueryUnbufferedAsync|Execute|ExecuteAsync|ExecuteScalar|ExecuteScalarAsync|ExecuteReader|ExecuteReaderAsync)$");
        static readonly Regex ExecCallee = new Regex(@"^(Query\w*|Execute\w*|CommandDefinition|OracleCommand|SqlCommand|DbCommand|NpgsqlCommand|OleDbCommand|OdbcCommand)$");
        static readonly Regex SqlFileRx = new Regex(@"[\w./\\-]*?([\w-]+(?:\.[\w-]+)*)\.sql\b", RegexOptions.IgnoreCase);
        static readonly Regex BuilderCallee = new Regex(@"^(Append|AppendLine|AppendFormat|Insert)$");

        public HashSet<string> EntryMethods = new HashSet<string>();   // handlers de endpoints

        public FragmentExtractor(CodeIndex index, SpMatcher matcher) { ix = index; sm = matcher; }

        static bool IsP(List<Token> t, int i, string s) { return i >= 0 && i < t.Count && t[i].Kind == TokKind.Punct && t[i].Text == s; }
        static bool IsI(List<Token> t, int i) { return i >= 0 && i < t.Count && t[i].Kind == TokKind.Ident; }

        public MethodAnalysis Analyze(MethodDecl m)
        {
            MethodAnalysis ma;
            if (cache.TryGetValue(m.Id, out ma)) return ma;
            ma = new MethodAnalysis { M = m };
            cache[m.Id] = ma;
            var f = m.File;
            var t = f.Toks;
            // marcadores de nivel metodo: comentarios previos y atributos
            AddTextMarkers(ma.MethodMarkers, m.Leading, "method-comment");
            foreach (var a in m.Attrs)
                foreach (var s in a.AllStrings)
                    foreach (var h in sm.FindInText(s))
                        ma.MethodMarkers.Add(NewMarker(h.Sp, new Location(f, a.Line), "attribute", s));
            ma.ClassMarkers = ClassMarkers(m.Owner);
            if (!m.HasBody) return ma;
            int bodyStartOff = t[Math.Min(m.BodyStart, t.Count - 1)].Start;
            int bodyEndOff = m.BodyEnd < t.Count ? t[m.BodyEnd].Start : f.Text.Length;
            if (m.BodyEnd > 0 && m.BodyEnd - 1 < t.Count) bodyEndOff = Math.Max(bodyEndOff, t[Math.Min(m.BodyEnd, t.Count) - 1].End);
            // bloques de comentario en el cuerpo
            var bodyComments = f.CommentsBetween(m.BodyStart > 0 ? t[m.BodyStart - 1].End : bodyStartOff, bodyEndOff);
            Comment prev = null;
            var curComments = new List<Comment>();
            foreach (var c in bodyComments)
            {
                if (c.IsPreproc && !Regex.IsMatch(c.Text, @"^\s*#\s*(end)?region", RegexOptions.IgnoreCase)) continue;
                bool join = prev != null && f.Text.Substring(prev.End, c.Start - prev.End).Trim().Length == 0;
                if (!join && curComments.Count > 0) { FinishBlock(ma, m, curComments); curComments = new List<Comment>(); }
                curComments.Add(c);
                prev = c;
            }
            if (curComments.Count > 0) FinishBlock(ma, m, curComments);

            // fragmentos crudos
            var raw = new List<Fragment>();
            var locals = ix.Locals(m);
            for (int k = m.BodyStart; k < m.BodyEnd && k < t.Count; k++)
            {
                var tk = t[k];
                if (tk.Kind == TokKind.Str)
                {
                    var ev = ix.EvalStringExpr(f, k, m.BodyEnd, m.Owner, false, m, 0);
                    if (ev != null)
                    {
                        var fr = RawFragment(ma, m, ev, k, Math.Max(k + 1, ev.EndTok), "literal", null);
                        if (fr != null) raw.Add(fr);
                        k = Math.Max(k, ev.EndTok - 1);
                    }
                    continue;
                }
                if (tk.Kind != TokKind.Ident || U.IsKeyword(tk.Text) && tk.Text != "this" && tk.Text != "string") continue;
                if (k > 0 && (t[k - 1].Kind == TokKind.Punct && (t[k - 1].Text == "." || t[k - 1].Text == "?." || t[k - 1].Text == "::"))) continue;
                // string.Format / string.Concat / sb.AppendFormat: se evaluan como un todo
                int callEnd;
                var special = FormatOrConcat(ma, m, k, out callEnd);
                if (special != null) { raw.Add(special); k = callEnd - 1; continue; }
                var names = new List<string>();
                int j = k;
                while (j < m.BodyEnd && t[j].Kind == TokKind.Ident) { names.Add(t[j].Text); if (IsP(t, j + 1, ".") && j + 2 < t.Count && t[j + 2].Kind == TokKind.Ident) j += 2; else { j++; break; } }
                if (names.Count == 0) continue;
                // configuracion: _config["A:B"], GetSection("A")["B"]
                int cfgEnd;
                var cfg = ix.ConfigAccess(f, k, m.BodyEnd, out cfgEnd);
                if (cfg != null)
                {
                    var fr = ExternalFragment(m, cfg.Value, cfg.File, cfg.Line, k, cfgEnd, f, tk.Line, "config", null);
                    if (fr != null) { fr.SpContext = true; ApplyContext(ma, m, fr); if (!fr.Ignored) raw.Add(fr); }
                    k = cfgEnd - 1; continue;
                }
                bool isCall = IsP(t, j, "(") || IsP(t, j, "<");
                if (isCall) { k = j - 1; continue; }
                if (names.Count == 1 && (locals.ContainsKey(names[0]) || m.Params.Any(p => p.Name == names[0]))) { continue; }
                var mv = ix.ResolveMemberChain(names, m.Owner);
                if (mv != null)
                {
                    string cv = ix.ConstValue(mv);
                    if (cv != null)
                    {
                        var ev = ix.EvalStringExpr(f, k, m.BodyEnd, m.Owner, false, m, 0);
                        if (ev != null)
                        {
                            var fr = RawFragment(ma, m, ev, k, Math.Max(j, ev.EndTok), "const", mv);
                            if (fr != null) raw.Add(fr);
                            k = Math.Max(k, ev.EndTok - 1);
                        }
                        continue;
                    }
                    // campo inicializado con carga de archivo .sql
                    if (mv.InitStart >= 0)
                    {
                        for (int q = mv.InitStart; q < mv.InitEnd && q < mv.File.Toks.Count; q++)
                        {
                            var qt = mv.File.Toks[q];
                            if (qt.Kind != TokKind.Str) continue;
                            foreach (var sf in SqlFileFragments(m, qt.Lit.PlainValue(), k, j, f, tk.Line)) raw.Add(sf);
                        }
                    }
                }
                if (names.Count >= 2)
                {
                    string key = names[names.Count - 1];
                    string bs = names[names.Count - 2];
                    List<ResxEntry> re;
                    if (mv == null && ix.ResxBases.Contains(bs) && ix.ResxByKey.TryGetValue(key, out re))
                    {
                        foreach (var r in re.Where(x => string.Equals(x.FileBase, bs, StringComparison.OrdinalIgnoreCase)))
                        {
                            var extra = new List<Marker>();
                            if (!string.IsNullOrEmpty(r.Comment))
                                foreach (var h in sm.FindInText(r.Comment)) extra.Add(NewMarker(h.Sp, new Location(r.File, r.CommentLine), "const-comment", r.Comment));
                            var fr = ExternalFragment(m, r.Value, r.File, r.Line, k, j, f, tk.Line, "resx", extra);
                            if (fr != null) { ApplyContext(ma, m, fr); if (!fr.Ignored) raw.Add(fr); }
                        }
                    }
                    else if (mv == null || mv.InitStart < 0)
                    {
                        // opciones IOptions<T>: solo si el receptor es una clase de opciones
                        var recv = ix.ResolveChain(m, names.Take(names.Count - 1).Select(x => new Seg { Name = x }).ToList(), 0);
                        bool amb;
                        var oe = ix.OptionsValue(recv, key, out amb);
                        if (oe != null && sm.MatchesPattern(oe.Value))
                        {
                            var fr = ExternalFragment(m, oe.Value, oe.File, oe.Line, k, j, f, tk.Line, "config", null);
                            if (fr != null) { fr.SpContext = true; ApplyContext(ma, m, fr); if (!fr.Ignored) raw.Add(fr); }
                        }
                        else if (amb)
                            ma.Warnings.Add(new Warn { Category = "Configuración ambigua", Message = "No se pudo determinar la sección de configuración de " + string.Join(".", names.ToArray()) + " (varias secciones tienen la clave '" + key + "')", Loc = new Location(f, tk.Line) });
                    }
                }
                k = j - 1;
            }

            // fusionar piezas de un mismo StringBuilder / variable acumulada
            var merged = MergeBuilders(m, raw);
            foreach (var fr in merged) Finalize(ma, fr);
            WarnUnresolvedCommands(ma, m);

            // asignar bloque mas cercano a cada fragmento (dentro del mismo bloque { })
            foreach (var fr in ma.Fragments)
            {
                MarkerBlock best = null;
                foreach (var b in ma.Blocks)
                {
                    bool cand = b.Start < fr.EndOffset || (b.Line == fr.EndLine && b.Start >= fr.StartOffset);
                    if (!cand) continue;
                    if (b.ScopeOpen >= 0 && !(fr.TokStart > b.ScopeOpen && fr.TokStart < b.ScopeClose)) continue;
                    if (best == null || b.Start > best.Start) best = b;
                }
                if (best != null) { fr.Block = best; best.Scoped = true; }
            }
            foreach (var b in ma.Blocks) if (!b.Scoped) foreach (var mk in b.Markers) ma.MethodMarkers.Add(mk);
            return ma;
        }

        void FinishBlock(MethodAnalysis ma, MethodDecl m, List<Comment> cs)
        {
            var b = new MarkerBlock { Start = cs[0].Start, End = cs[cs.Count - 1].End, Line = cs[0].Line, EndLine = cs[cs.Count - 1].EndLine, Id = "B" + (++blockSeq) };
            AddTextMarkers(b.Markers, cs, "body-comment");
            if (b.Markers.Count == 0) return;
            // alcance: el bloque { } que contiene al comentario
            var t = m.File.Toks; var mt = m.File.Match;
            int after = m.BodyStart;
            while (after < m.BodyEnd && t[after].Start < b.End) after++;
            for (int k = after - 1; k >= m.BodyStart; k--)
            {
                if (IsP(t, k, "}") && mt[k] >= 0 && mt[k] < k) { k = mt[k]; continue; }
                if (IsP(t, k, "{") && mt[k] > k) { b.ScopeOpen = k; b.ScopeClose = mt[k]; break; }
            }
            ma.Blocks.Add(b);
        }

        void AddTextMarkers(List<Marker> into, List<Comment> cs, string source)
        {
            if (cs == null || cs.Count == 0) return;
            var sb = new StringBuilder();
            var starts = new List<int>();
            foreach (var c in cs) { starts.Add(sb.Length); sb.Append(c.Text); sb.Append('\n'); }
            string all = sb.ToString();
            foreach (var h in sm.FindInText(all))
            {
                int idx = 0;
                for (int i = 0; i < starts.Count; i++) if (starts[i] <= h.Offset) idx = i;
                var c = cs[idx];
                int line = c.Line;
                for (int z = starts[idx]; z < h.Offset && z < all.Length; z++) if (all[z] == '\n') line++;
                var file = FileOfComment(c);
                into.Add(NewMarker(h.Sp, new Location(file, line), source, c.Text));
            }
        }

        Dictionary<Comment, SourceFile> commentFile = new Dictionary<Comment, SourceFile>();
        public void RegisterCommentFiles(IEnumerable<SourceFile> files)
        {
            foreach (var f in files) foreach (var c in f.Comments) commentFile[c] = f;
        }
        SourceFile FileOfComment(Comment c) { SourceFile f; return commentFile.TryGetValue(c, out f) ? f : null; }

        public Marker NewMarker(SpName sp, Location loc, string source, string excerpt)
        {
            return new Marker { Sp = sp, Loc = loc, Source = source, Excerpt = U.OneLine(excerpt, 160), Id = "K" + (++markSeq) };
        }

        public List<Marker> ClassMarkers(TypeDecl td)
        {
            var r = new List<Marker>();
            if (td == null) return r;
            List<Marker> c;
            if (classMarkerCache.TryGetValue(td.Id, out c)) return c;
            var all = new List<Marker>();
            AddTextMarkers(all, td.Leading, "class-comment");
            foreach (var a in td.Attrs) foreach (var s in a.AllStrings) foreach (var h in sm.FindInText(s)) all.Add(NewMarker(h.Sp, new Location(td.File, a.Line), "class-comment", s));
            if (all.Select(x => x.Sp.Key).Distinct().Count() == 1) r.Add(all[0]);
            classMarkerCache[td.Id] = r;
            return r;
        }

        // ------------------------------------------------------------ fragmentos crudos
        Fragment NewFragment(MethodDecl m, CodeIndex.StrEval ev, int tokStart, int tokEnd, string origin)
        {
            var f = m.File; var t = f.Toks;
            return new Fragment
            {
                Id = "F" + (++fragSeq), Value = ev.Value, Pieces = ev.Pieces, Method = m, File = f,
                TokStart = tokStart, TokEnd = tokEnd, Line = t[tokStart].Line, EndLine = t[Math.Min(tokEnd, t.Count) - 1].EndLine,
                StartOffset = t[tokStart].Start, EndOffset = t[Math.Min(tokEnd, t.Count) - 1].End, Origin = origin
            };
        }

        Fragment RawFragment(MethodAnalysis ma, MethodDecl m, CodeIndex.StrEval ev, int tokStart, int tokEnd, string origin, MemberVar constRef)
        {
            var fr = NewFragment(m, ev, tokStart, tokEnd, origin);
            if (constRef != null && ev.Pieces.All(p => p.Const != null || p.ExternalKind != null))
            {
                fr.OnlyConstRef = true;
                fr.ConstRef = ev.OuterConst ?? constRef;
            }
            if (constRef != null) foreach (var pc in ev.Pieces) if (pc.Const != null) AddConstMarkers(fr, pc.Const);
            if (ev.OuterConst != null && fr.ConstRef == null) fr.ConstRef = ev.OuterConst;
            ApplyContext(ma, m, fr);
            if (fr.Ignored) return null;
            if (fr.MessageContext)
            {
                foreach (var h in sm.FindInText(fr.Value))
                    ma.WeakMarkers.Add(NewMarker(h.Sp, fr.LocOfValueOffset(h.Offset), "log", fr.Value));
                return null;
            }
            return fr;
        }

        // Contexto de uso del string: log/excepcion, comando SQL, dato (comparacion, parametro, respuesta), StringBuilder
        void ApplyContext(MethodAnalysis ma, MethodDecl m, Fragment fr)
        {
            var f = m.File; var t = f.Toks; var mt = f.Match;
            int tokStart = fr.TokStart, tokEnd = fr.TokEnd;
            // comparaciones
            if (IsP(t, tokStart - 1, "==") || IsP(t, tokStart - 1, "!=") || IsP(t, tokEnd, "==") || IsP(t, tokEnd, "!=") || (IsI(t, tokStart - 1) && t[tokStart - 1].Text == "case")) { fr.Ignored = true; return; }
            // inicializador de objeto anonimo / propiedad: new { a = "..." }  new X { Prop = "..." }
            if (IsP(t, tokStart - 1, "=") && IsI(t, tokStart - 2) && (IsP(t, tokStart - 3, "{") || IsP(t, tokStart - 3, ",")))
            {
                string prop = t[tokStart - 2].Text;
                int k = tokStart - 3;
                while (k >= m.BodyStart && !(IsP(t, k, "{") && mt[k] > tokStart)) { if ((IsP(t, k, ")") || IsP(t, k, "]") || IsP(t, k, "}")) && mt[k] >= 0 && mt[k] < k) k = mt[k]; k--; }
                if (k >= m.BodyStart && IsP(t, k, "{"))
                {
                    bool objInit = (IsI(t, k - 1) && t[k - 1].Text == "new") || IsI(t, k - 1) || IsP(t, k - 1, ">") || IsP(t, k - 1, ")");
                    if (objInit && prop != "CommandText" && prop != "Sql" && prop != "Query" && prop != "Text") { fr.Ignored = true; return; }
                    if (prop == "CommandText" && BodyHasIdent(m, "StoredProcedure")) fr.SpContext = true;
                }
            }
            string callee; bool isNew; int argIndex; string namedArg; int open;
            EnclosingCall(m, tokStart, tokEnd, out callee, out isNew, out argIndex, out namedArg, out open);
            if (callee != null)
            {
                bool logRecv = false;
                List<Seg> recv = null;
                if (open > 1 && IsP(t, open - 2, ".")) { recv = ix.WalkBack(f, open - 2); logRecv = recv.Any(s => s.Name.IndexOf("log", StringComparison.OrdinalIgnoreCase) >= 0 || s.Name == "Console" || s.Name == "Debug" || s.Name == "Trace"); }
                if ((isNew && callee.EndsWith("Exception")) || (!isNew && MsgCallee.IsMatch(callee)) || logRecv) { fr.MessageContext = true; return; }
                if (isNew && callee == "StringBuilder" && argIndex == 0)
                {
                    // var sb = new StringBuilder("SELECT ...")
                    int nk = open - 2;
                    while (nk > m.BodyStart && !(IsI(t, nk) && t[nk].Text == "new")) nk--;
                    if (IsP(t, nk - 1, "=") && IsI(t, nk - 2)) { fr.GroupVar = t[nk - 2].Text; fr.GroupStart = true; }
                }
                else if (BuilderCallee.IsMatch(callee) && recv != null && recv.Count >= 1 && !recv[0].IsCall && recv.All(s => !s.IsCall || BuilderCallee.IsMatch(s.Name)))
                {
                    // sb.Append(a).Append(b): el grupo es la raiz de la cadena
                    var rootSegs = recv.TakeWhile(s => !s.IsCall).Select(s => s.Name).ToArray();
                    if (callee != "Insert" || argIndex == 1) fr.GroupVar = string.Join(".", rootSegs);
                    fr.GroupAppendLine = callee == "AppendLine";
                }
                else if (!isNew && NonCmdCallee.IsMatch(callee) && !ExecCallee.IsMatch(callee)) { fr.Ignored = true; return; }
                bool firstArg = argIndex == 0 || (namedArg != null && Regex.IsMatch(namedArg, @"^(sql|commandText|query|spName|procedure\w*|storedProcedure\w*|nombreSp|sp)$", RegexOptions.IgnoreCase));
                if (firstArg && ExecCallee.IsMatch(callee) && RangeHasIdent(t, open, mt[open], "StoredProcedure")) fr.SpContext = true;
                if (firstArg && ExecCallee.IsMatch(callee) && callee.Contains("Command") && BodyHasIdent(m, "StoredProcedure")) fr.SpContext = true;
                if (!fr.SpContext && !isNew && fr.GroupVar == null)
                {
                    // wrapper del repo: el parametro string termina como texto del comando con CommandType.StoredProcedure
                    var cs = new CallSite { Name = callee, Argc = ix.CountArgs(f, open), ArgOpen = open };
                    if (recv != null) cs.Receiver = recv;
                    bool inf, unr;
                    foreach (var tg in ix.ResolveCall(m, cs, out inf, out unr))
                    {
                        int wi = WrapperParamIndex(tg, 0);
                        if (wi >= 0 && (argIndex == wi || (namedArg != null && tg.Params[wi + (tg.IsExtension ? 1 : 0)].Name == namedArg))) { fr.SpContext = true; break; }
                    }
                }
            }
            else
            {
                // cmd.CommandText = "PCK.SP";  sql += "...";  sql = "...";
                if (tokStart >= 2 && IsP(t, tokStart - 1, "=") && IsI(t, tokStart - 2) && t[tokStart - 2].Text == "CommandText" && BodyHasIdent(m, "StoredProcedure")) fr.SpContext = true;
                if (tokStart >= 2 && (IsP(t, tokStart - 1, "+=") || (IsP(t, tokStart - 1, "=") && !IsI(t, tokStart - 3) && !IsP(t, tokStart - 3, ".")) || (IsP(t, tokStart - 1, "=") && IsI(t, tokStart - 3) && (t[tokStart - 3].Text == "var" || t[tokStart - 3].Text == "string"))) && IsI(t, tokStart - 2))
                {
                    fr.GroupVar = t[tokStart - 2].Text;
                    fr.GroupStart = IsP(t, tokStart - 1, "=");   // "x = ..." o "var x = ...": valor nuevo; "x += ...": continuacion
                }
                else if (tokStart >= 4 && IsP(t, tokStart - 1, "+") && IsI(t, tokStart - 2) && IsP(t, tokStart - 3, "=") && IsI(t, tokStart - 4) && t[tokStart - 4].Text == t[tokStart - 2].Text)
                    fr.GroupVar = t[tokStart - 2].Text;
            }
        }

        // string.Format(...), string.Concat(...), sb.AppendFormat(...): un unico fragmento con los argumentos sustituidos
        Fragment FormatOrConcat(MethodAnalysis ma, MethodDecl m, int k, out int callEnd)
        {
            callEnd = k + 1;
            var f = m.File; var t = f.Toks; var mt = f.Match;
            int nameTok = -1; bool isConcat = false; string groupVar = null;
            if ((t[k].Text == "string" || t[k].Text == "String") && IsP(t, k + 1, ".") && IsI(t, k + 2) && (t[k + 2].Text == "Format" || t[k + 2].Text == "Concat") && IsP(t, k + 3, "("))
            { nameTok = k + 2; isConcat = t[k + 2].Text == "Concat"; }
            else if (IsP(t, k + 1, ".") && IsI(t, k + 2) && t[k + 2].Text == "AppendFormat" && IsP(t, k + 3, "("))
            { nameTok = k + 2; groupVar = t[k].Text; }
            if (nameTok < 0) return null;
            int open = nameTok + 1;
            if (mt[open] < open) return null;
            var args = SplitArgs(f, open);
            if (args.Count == 0) return null;
            var vals = new List<CodeIndex.StrEval>();
            foreach (var a in args) vals.Add(ix.EvalStringExpr(f, a[0], a[1], m.Owner, true, m, 0));
            var ev = new CodeIndex.StrEval();
            if (isConcat)
            {
                foreach (var v in vals)
                {
                    if (v == null || !v.Complete) { ev.Sb.Append("{?}"); ev.Complete = false; continue; }
                    int b = ev.Sb.Length;
                    foreach (var pc in v.Pieces) ev.Pieces.Add(new FragPiece { ValueStart = b + pc.ValueStart, ValueLength = pc.ValueLength, File = pc.File, Line = pc.Line, CountsLines = pc.CountsLines, Const = pc.Const, ExternalKind = pc.ExternalKind });
                    ev.Sb.Append(v.Value);
                    ev.HasLiteral = true;
                    if (ev.OuterConst == null) ev.OuterConst = v.OuterConst;
                }
            }
            else
            {
                var fmt = vals[0];
                if (fmt == null) return null;
                string sv = fmt.Value;
                var outSb = new StringBuilder();
                var pieces = new List<FragPiece>();
                // sustituir {0},{1}... (los fragmentos del formato conservan su ubicacion aproximada)
                int last = 0;
                foreach (Match mm in Regex.Matches(sv, @"\{(\d+)(?:[^}]*)\}"))
                {
                    outSb.Append(sv, last, mm.Index - last);
                    int idx = int.Parse(mm.Groups[1].Value) + 1;
                    if (idx < vals.Count && vals[idx] != null && vals[idx].Complete) outSb.Append(vals[idx].Value);
                    else outSb.Append("{?}");
                    last = mm.Index + mm.Length;
                }
                outSb.Append(sv, last, sv.Length - last);
                ev.Sb.Append(outSb.ToString());
                foreach (var pc in fmt.Pieces) ev.Pieces.Add(new FragPiece { ValueStart = 0, ValueLength = ev.Sb.Length, File = pc.File, Line = pc.Line, CountsLines = false, Const = pc.Const, ExternalKind = pc.ExternalKind });
                ev.HasLiteral = true;
            }
            if (!ev.HasLiteral) return null;
            callEnd = mt[open] + 1;
            var fr = NewFragment(m, ev, k, callEnd, "literal");
            if (groupVar != null) fr.GroupVar = groupVar;
            else
            {
                ApplyContext(ma, m, fr);
                if (fr.Ignored || fr.MessageContext) return null;
            }
            return fr;
        }

        List<int[]> SplitArgs(SourceFile f, int open)
        {
            var t = f.Toks; var mt = f.Match;
            var r = new List<int[]>();
            int close = mt[open];
            if (close < 0) return r;
            int s = open + 1;
            for (int k = open + 1; k < close; k++)
            {
                if (t[k].Kind != TokKind.Punct) continue;
                string x = t[k].Text;
                if ((x == "(" || x == "[" || x == "{") && mt[k] > k) { k = mt[k]; continue; }
                if (x == ",") { r.Add(new int[] { s, k }); s = k + 1; }
            }
            if (close > s) r.Add(new int[] { s, close });
            return r;
        }

        // Une en orden las piezas de un mismo StringBuilder o variable acumulada (sql += ...)
        List<Fragment> MergeBuilders(MethodDecl m, List<Fragment> raw)
        {
            var result = new List<Fragment>();
            var open = new Dictionary<string, List<Fragment>>();
            var closed = new List<KeyValuePair<string, List<Fragment>>>();
            foreach (var fr in raw.OrderBy(x => x.TokStart))
            {
                if (fr.GroupVar == null) { result.Add(fr); continue; }
                List<Fragment> l;
                // una asignacion simple (sql = "...") reemplaza el valor: empieza un grupo nuevo
                if (open.TryGetValue(fr.GroupVar, out l) && fr.GroupStart && l.Count > 0)
                {
                    closed.Add(new KeyValuePair<string, List<Fragment>>(fr.GroupVar, l));
                    l = null;
                }
                if (l == null) { l = new List<Fragment>(); open[fr.GroupVar] = l; }
                l.Add(fr);
            }
            foreach (var kv in open) closed.Add(kv);
            foreach (var kv in closed)
            {
                var l = kv.Value;
                if (l.Count == 1) { result.Add(l[0]); continue; }
                var first = l[0];
                var sb = new StringBuilder();
                var pieces = new List<FragPiece>();
                bool sp = false;
                MemberVar cref = null;
                var cm = new List<Marker>();
                foreach (var fr in l)
                {
                    int b = sb.Length;
                    foreach (var pc in fr.Pieces) pieces.Add(new FragPiece { ValueStart = b + pc.ValueStart, ValueLength = pc.ValueLength, File = pc.File, Line = pc.Line, CountsLines = pc.CountsLines, Const = pc.Const, ExternalKind = pc.ExternalKind });
                    sb.Append(fr.Value);
                    if (fr.GroupAppendLine) sb.Append('\n');
                    sp |= fr.SpContext;
                    if (cref == null) cref = fr.ConstRef;
                    foreach (var x in fr.ConstMarkers) if (!cm.Any(y => y.Sp.Key == x.Sp.Key)) cm.Add(x);
                }
                var last = l[l.Count - 1];
                var mf = new Fragment
                {
                    Id = "F" + (++fragSeq), Value = sb.ToString(), Pieces = pieces, Method = m, File = m.File,
                    TokStart = first.TokStart, TokEnd = last.TokEnd, Line = first.Line, EndLine = last.EndLine,
                    StartOffset = first.StartOffset, EndOffset = last.EndOffset, Origin = "literal", SpContext = sp, ConstRef = cref, GroupVar = kv.Key
                };
                mf.ConstMarkers.AddRange(cm);
                result.Add(mf);
            }
            return result.OrderBy(x => x.TokStart).ToList();
        }

        void AddConstMarkers(Fragment fr, MemberVar mv)
        {
            var tmp = new List<Marker>();
            AddTextMarkers(tmp, mv.Leading, "const-comment");
            foreach (var x in tmp) if (!fr.ConstMarkers.Any(y => y.Sp.Key == x.Sp.Key)) fr.ConstMarkers.Add(x);
        }

        // Clasifica el fragmento y lo agrega si es relevante
        void Finalize(MethodAnalysis ma, Fragment fr)
        {
            // referencias a archivos .sql dentro del texto
            if (SqlFileRx.IsMatch(fr.Value) && fr.Value.Length < 300)
            {
                foreach (var sf in SqlFileFragments(fr.Method, fr.Value, fr.TokStart, fr.TokEnd, fr.File, fr.Line)) Finalize(ma, sf);
                return;
            }
            // clave de configuracion completa "Seccion:Clave"
            ConfigEntry ce;
            if (fr.Value.Contains(":") && !fr.Value.Contains(" ") && ix.ConfigByPath.TryGetValue(fr.Value.Trim(), out ce))
            {
                var cf = ExternalFragment(fr.Method, ce.Value, ce.File, ce.Line, fr.TokStart, fr.TokEnd, fr.File, fr.Line, "config", null);
                if (cf != null) { cf.SpContext = true; Finalize(ma, cf); }
                return;
            }
            var scan = SqlScan.Scan(fr.Value);
            fr.Kind = scan.Kind;
            fr.Form = scan.Form;
            foreach (var cs in scan.CommentSpans)
            {
                string ctext = fr.Value.Substring(cs[0], cs[1] - cs[0]);
                foreach (var h in sm.FindInText(ctext))
                    fr.InlineMarkers.Add(NewMarker(h.Sp, fr.LocOfValueOffset(cs[0] + h.Offset), "sql-comment", ctext));
            }
            bool bare = scan.Kind == "bare";
            var hits = sm.FindInCode(scan.Blank, bare, bare || scan.Kind == "pure");
            if (bare && hits.Count == 0 && fr.SpContext)
            {
                string nm = U.StripQuotes(scan.Blank.Trim().TrimEnd(';'));
                string link = null;
                int at = nm.IndexOf('@'); if (at > 0) { link = nm.Substring(at + 1).Trim(); nm = nm.Substring(0, at).Trim(); }
                var parts = nm.Split('.').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
                SpName sp = null;
                if (parts.Length == 3) sp = SpMatcher.Make(parts[0], parts[1], parts[2], link);
                else if (parts.Length == 2) sp = SpMatcher.Make(null, parts[0], parts[1], link);
                else if (parts.Length == 1) sp = SpMatcher.Make(null, null, parts[0], link);
                if (sp != null && !nm.Contains("{?}")) hits.Add(new SpHit { Sp = sp, Offset = Math.Max(0, fr.Value.IndexOf(parts[0], StringComparison.Ordinal)) });
            }
            foreach (var h in hits)
            {
                h.Loc = fr.LocOfValueOffset(h.Offset);
                fr.Calls.Add(h);
            }
            if (sm.HasDynamic(scan.Blank)) fr.HasDynamicSp = true;
            bool relevant = fr.Calls.Count > 0 || fr.Kind == "regular" || fr.InlineMarkers.Count > 0 || fr.HasDynamicSp;
            if (fr.Kind == "regular" && fr.Calls.Count == 0 && fr.Value.Trim().Length < 12) relevant = false;
            if (relevant) ma.Fragments.Add(fr);
        }

        List<Fragment> SqlFileFragments(MethodDecl m, string value, int tokStart, int tokEnd, SourceFile f, int line)
        {
            var r = new List<Fragment>();
            foreach (Match mm in SqlFileRx.Matches(value))
            {
                string full = mm.Value.Replace('\\', '/');
                string fileName = full.Contains("/") ? full.Substring(full.LastIndexOf('/') + 1) : full;
                // nombre de recurso incrustado: A.B.Nombre.sql -> Nombre.sql
                var dotParts = fileName.Split('.');
                var candidates = new List<SourceFile>();
                List<SourceFile> l;
                if (ix.SqlByName.TryGetValue(fileName, out l)) candidates.AddRange(l);
                if (candidates.Count == 0 && dotParts.Length > 2 && ix.SqlByName.TryGetValue(dotParts[dotParts.Length - 2] + ".sql", out l)) candidates.AddRange(l);
                if (candidates.Count > 1)
                {
                    string norm = full.Replace('/', '.').ToLowerInvariant();
                    var best = candidates.Where(c => norm.EndsWith(c.Rel.Replace('/', '.').ToLowerInvariant()) || c.Rel.Replace('/', '.').ToLowerInvariant().EndsWith(norm)).ToList();
                    if (best.Count > 0) candidates = best;
                }
                foreach (var sf in candidates.Distinct())
                {
                    var fr = ExternalFragment(m, sf.Text, sf, 1, tokStart, tokEnd, f, line, "sqlfile", null);
                    if (fr != null) r.Add(fr);
                }
            }
            return r;
        }

        Fragment ExternalFragment(MethodDecl m, string value, SourceFile src, int srcLine, int tokStart, int tokEnd, SourceFile f, int line, string origin, List<Marker> extraMarkers)
        {
            var t = f.Toks;
            var fr = new Fragment
            {
                Id = "F" + (++fragSeq), Value = value ?? "", Method = m, File = f, TokStart = tokStart, TokEnd = Math.Max(tokStart + 1, tokEnd),
                Line = line, EndLine = line, StartOffset = t[tokStart].Start, EndOffset = t[Math.Min(Math.Max(tokStart + 1, tokEnd), t.Count) - 1].End, Origin = origin, OnlyConstRef = true
            };
            fr.Pieces.Add(new FragPiece { ValueStart = 0, ValueLength = fr.Value.Length, File = src, Line = srcLine, CountsLines = origin != "config", ExternalKind = origin });
            if (extraMarkers != null) fr.ConstMarkers.AddRange(extraMarkers);
            return fr;
        }

        // Comandos Dapper cuyo texto no se puede resolver: se informan como advertencia
        void WarnUnresolvedCommands(MethodAnalysis ma, MethodDecl m)
        {
            var f = m.File; var t = f.Toks; var mt = f.Match;
            foreach (var cs in ix.Calls(m))
            {
                if (cs.IsNew || cs.IsMethodGroup || cs.ArgOpen < 0 || !DapperExec.IsMatch(cs.Name)) continue;
                if (cs.Receiver.Count == 0) continue;
                var args = SplitArgs(f, cs.ArgOpen);
                if (args.Count == 0) continue;
                // primer argumento (o "sql:" / "commandText:")
                int[] a0 = args[0];
                foreach (var a in args) if (IsI(t, a[0]) && IsP(t, a[0] + 1, ":") && Regex.IsMatch(t[a[0]].Text, "^(sql|commandText|command)$", RegexOptions.IgnoreCase)) { a0 = new int[] { a[0] + 2, a[1] }; break; }
                if (ma.Fragments.Any(fr => fr.TokStart < a0[1] && fr.TokEnd > a0[0])) continue;
                // el texto viene de un metodo (ArmarSql(), sb.ToString(), helper.Get()): se sigue por el grafo de llamadas
                bool hasCall = false;
                for (int q = a0[0]; q < a0[1]; q++) if (IsP(t, q, "(")) { hasCall = true; break; }
                if (hasCall) continue;
                // CommandDefinition / variable con valor conocido / parametro de un wrapper: no se avisa
                if (IsI(t, a0[0]) && t[a0[0]].Text == "new") continue;
                // StringBuilder o variable acumulada: sb.ToString() / sql
                int r0 = a0[0];
                if (IsI(t, r0) && t[r0].Text == "this" && IsP(t, r0 + 1, ".")) r0 += 2;
                if (IsI(t, r0) && ma.Fragments.Any(fr => fr.GroupVar == t[r0].Text)) continue;
                if (a0[1] - a0[0] == 1 && IsI(t, a0[0]))
                {
                    string n = t[a0[0]].Text;
                    // parametro: en un wrapper lo resuelven sus llamadores; en un endpoint viene de la peticion (dinamico)
                    if (m.Params.Any(p => p.Name == n) && !EntryMethods.Contains(m.Id)) continue;
                    LocalInfo li;
                    if (ix.Locals(m).TryGetValue(n, out li))
                    {
                        int declTok = li.ExprTok;
                        int declEnd = declTok >= 0 ? ix.P(f).FindStmtEnd(declTok, m.BodyEnd) : -1;
                        if (declTok >= 0 && ma.Fragments.Any(fr => fr.TokStart >= declTok - 3 && fr.TokStart <= declEnd)) continue;
                        bool initCall = false;
                        for (int q = Math.Max(0, declTok); declTok >= 0 && q < declEnd; q++) if (IsP(t, q, "(")) { initCall = true; break; }
                        if (initCall) continue;   // var sql = ArmarSql(): el texto sale de otro metodo
                        if (ma.Fragments.Any(fr => fr.GroupVar == n)) continue;
                    }
                    var mv = ix.ResolveMemberChain(new List<string> { n }, m.Owner);
                    if (mv != null && ix.ConstValue(mv) != null) continue;
                }
                var sb = new StringBuilder();
                for (int q = a0[0]; q < a0[1] && q < t.Count; q++) sb.Append(t[q].Kind == TokKind.Str ? "\"" + t[q].Lit.PlainValue() + "\"" : t[q].Text);
                ma.Warnings.Add(new Warn { Category = "Comando no resuelto", Message = cs.Name + "(" + U.OneLine(sb.ToString(), 80) + "): el texto del comando o el nombre del SP se arma en tiempo de ejecución", Loc = new Location(f, cs.Line) });
            }
        }

        void EnclosingCall(MethodDecl m, int tokStart, int tokEnd, out string callee, out bool isNew, out int argIndex, out string namedArg, out int open)
        {
            callee = null; isNew = false; argIndex = -1; namedArg = null; open = -1;
            var t = m.File.Toks; var mt = m.File.Match;
            int k = tokStart - 1;
            int lim = Math.Max(0, m.BodyStart - 1);
            while (k >= lim)
            {
                var tk = t[k];
                if (tk.Kind == TokKind.Punct)
                {
                    string x = tk.Text;
                    if ((x == ")" || x == "]" || x == "}") && mt[k] >= 0 && mt[k] < k) { k = mt[k] - 1; continue; }
                    if (x == "(" && mt[k] >= tokEnd - 1) { open = k; break; }
                    if (x == "{" || x == ";") return;
                    if (x == "[" && mt[k] >= tokEnd - 1) return;
                }
                k--;
            }
            if (open < 0) return;
            int ni = open - 1;
            if (IsP(t, ni, ">"))
            {
                int depth = 0; int z = ni;
                for (; z >= 0; z--) { if (IsP(t, z, ">")) depth++; else if (IsP(t, z, "<")) { depth--; if (depth == 0) break; } }
                ni = z - 1;
            }
            if (ni < 0 || t[ni].Kind != TokKind.Ident) { open = -1; return; }
            callee = t[ni].Text;
            if (U.IsKeyword(callee) && callee != "base" && callee != "this") { callee = null; open = -1; return; }
            int nn = ni - 1;
            while (nn >= 0 && (IsP(t, nn, ".") || t[nn].Kind == TokKind.Ident) && t[nn].Text != "new") nn--;
            isNew = nn >= 0 && t[nn].Kind == TokKind.Ident && t[nn].Text == "new";
            argIndex = 0;
            for (int q = open + 1; q < tokStart; q++)
            {
                if (t[q].Kind != TokKind.Punct) continue;
                string x = t[q].Text;
                if ((x == "(" || x == "[" || x == "{") && mt[q] > q) { q = mt[q]; continue; }
                if (x == ",") argIndex++;
            }
            if (tokStart >= 2 && IsP(t, tokStart - 1, ":") && t[tokStart - 2].Kind == TokKind.Ident) namedArg = t[tokStart - 2].Text;
        }

        static bool RangeHasIdent(List<Token> t, int s, int e, string id)
        {
            for (int k = s; k <= e && k < t.Count; k++) if (t[k].Kind == TokKind.Ident && t[k].Text == id) return true;
            return false;
        }

        static bool BodyHasIdent(MethodDecl m, string id)
        {
            if (!m.HasBody) return false;
            return RangeHasIdent(m.File.Toks, m.BodyStart, m.BodyEnd - 1, id);
        }

        // Indice (sin contar "this") del parametro string que termina como texto de un comando con
        // CommandType.StoredProcedure (directamente o pasando por otro wrapper). -1 si no es un wrapper.
        public int WrapperParamIndex(MethodDecl md, int depth)
        {
            int r;
            if (wrapperCache.TryGetValue(md.Id, out r)) return r;
            wrapperCache[md.Id] = -1;
            r = -1;
            if (md.HasBody && depth < 4 && BodyHasIdent(md, "StoredProcedure"))
            {
                var t = md.File.Toks; var mt = md.File.Match;
                for (int i = 0; i < md.Params.Count && r < 0; i++)
                {
                    var p = md.Params[i];
                    if (p.IsThis || p.Type == null || (p.Type.Name != "string" && p.Type.Name != "String")) continue;
                    for (int k = md.BodyStart; k < md.BodyEnd && k < t.Count; k++)
                    {
                        if (!IsI(t, k) || t[k].Text != p.Name || IsP(t, k - 1, ".")) continue;
                        // CommandText = param
                        if (IsP(t, k - 1, "=") && IsI(t, k - 2) && t[k - 2].Text == "CommandText") { r = i; break; }
                        // primer argumento (o sql:/commandText:) de Query*/Execute*/new XCommand(/new CommandDefinition(
                        bool first = IsP(t, k - 1, "(") || (IsP(t, k - 1, ":") && IsI(t, k - 2) && Regex.IsMatch(t[k - 2].Text, "^(sql|commandText)$", RegexOptions.IgnoreCase));
                        if (!first || !(IsP(t, k + 1, ",") || IsP(t, k + 1, ")"))) continue;
                        int open = IsP(t, k - 1, "(") ? k - 1 : -1;
                        if (open < 0) { int z = k - 2; while (z >= md.BodyStart && !(IsP(t, z, "(") && mt[z] > k)) z--; open = z; }
                        if (open <= 0 || !IsI(t, open - 1)) continue;
                        string callee = t[open - 1].Text;
                        if (ExecCallee.IsMatch(callee)) { r = i; break; }
                        // otro wrapper del repo
                        var cs = new CallSite { Name = callee, Argc = ix.CountArgs(md.File, open), ArgOpen = open };
                        if (open > 1 && IsP(t, open - 2, ".")) cs.Receiver = ix.WalkBack(md.File, open - 2);
                        bool inf, unr;
                        foreach (var tg in ix.ResolveCall(md, cs, out inf, out unr)) if (tg != md && WrapperParamIndex(tg, depth + 1) == 0) { r = i; break; }
                        if (r >= 0) break;
                    }
                }
            }
            if (r >= 0 && md.IsExtension) r -= 1;
            wrapperCache[md.Id] = r;
            return r;
        }
    }
}

namespace SPA_NS
{
    // =====================================================================
    //  DESCUBRIMIENTO DE ENDPOINTS
    // =====================================================================
    public class ConvRoute
    {
        public string Template, DefaultController, DefaultAction;
        public bool WebApi;
    }

    public class EndpointFinder
    {
        CodeIndex ix;
        FragmentExtractor fx;
        SpMatcher sm;
        AnalysisResult res;
        int seq = 0;
        List<ConvRoute> convRoutes = new List<ConvRoute>();
        string fastEndpointsPrefix;
        Dictionary<string, List<KeyValuePair<MethodDecl, CallSite>>> callersByName;

        static readonly string[] VerbAttrs = new string[] { "HttpGet", "HttpPost", "HttpPut", "HttpDelete", "HttpPatch", "HttpHead", "HttpOptions" };
        static readonly HashSet<string> RouteBuilderTypes = new HashSet<string>(new string[] {
            "IEndpointRouteBuilder", "RouteGroupBuilder", "WebApplication", "IApplicationBuilder", "IEndpointConventionBuilder", "RouteHandlerBuilder" });

        public EndpointFinder(CodeIndex index, FragmentExtractor f, SpMatcher matcher, AnalysisResult r) { ix = index; fx = f; sm = matcher; res = r; }

        public List<Endpoint> Find()
        {
            var eps = new List<Endpoint>();
            FindConventionalTemplates();
            FindFastEndpointsPrefix();
            eps.AddRange(Controllers());
            eps.AddRange(MinimalApis());
            eps.AddRange(FastEndpoints());
            eps.AddRange(AzureFunctions());
            foreach (var e in eps) e.Id = "E" + (++seq);
            return eps;
        }

        static bool IsP(List<Token> t, int i, string s) { return i >= 0 && i < t.Count && t[i].Kind == TokKind.Punct && t[i].Text == s; }
        static bool IsI(List<Token> t, int i) { return i >= 0 && i < t.Count && t[i].Kind == TokKind.Ident; }

        // ------------------------------------------------------------ rutas
        // Quita restricciones/valores por defecto de los parametros: {id:int} {id?} {*slug} {code:regex(^\d{{3}}$)} -> {nombre}
        public static string Normalize(string r)
        {
            if (r == null) r = "";
            r = r.Trim();
            if (r.StartsWith("~")) r = r.Substring(1);
            var sb = new StringBuilder();
            int i = 0;
            while (i < r.Length)
            {
                char c = r[i];
                if (c == '{' && i + 1 < r.Length && r[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
                if (c == '}' && i + 1 < r.Length && r[i + 1] == '}') { sb.Append('}'); i += 2; continue; }
                if (c == '{')
                {
                    int j = i + 1;
                    while (j < r.Length && r[j] == '*') j++;
                    int ns = j;
                    while (j < r.Length && (char.IsLetterOrDigit(r[j]) || r[j] == '_')) j++;
                    string name = r.Substring(ns, j - ns);
                    // fin del parametro: primera '}' no duplicada
                    while (j < r.Length)
                    {
                        if (r[j] == '}' && j + 1 < r.Length && r[j + 1] == '}') { j += 2; continue; }
                        if (r[j] == '{' && j + 1 < r.Length && r[j + 1] == '{') { j += 2; continue; }
                        if (r[j] == '}') break;
                        j++;
                    }
                    sb.Append('{').Append(name).Append('}');
                    i = j + 1;
                    continue;
                }
                sb.Append(c == '\\' ? '/' : c);
                i++;
            }
            r = sb.ToString();
            r = Regex.Replace(r, @"/{2,}", "/");
            if (!r.StartsWith("/")) r = "/" + r;
            if (r.Length > 1) r = r.TrimEnd('/');
            if (r.Length == 0) r = "/";
            return r;
        }

        // Minimal APIs / grupos: concatenacion relativa ("/api" + "/{id}" = "/api/{id}")
        public static string Join(string prefix, string tpl)
        {
            if (string.IsNullOrEmpty(tpl) || tpl == "/") return prefix ?? "";
            if (string.IsNullOrEmpty(prefix)) return tpl;
            return prefix.TrimEnd('/') + "/" + tpl.TrimStart('~').TrimStart('/');
        }

        // MVC: una plantilla que empieza con "/" o "~/" es absoluta
        public static string Combine(string prefix, string tpl)
        {
            if (tpl == null) return prefix ?? "";
            if (tpl.StartsWith("~/") || tpl.StartsWith("/")) return tpl;
            if (string.IsNullOrEmpty(prefix)) return tpl;
            if (tpl.Length == 0) return prefix;
            return prefix.TrimEnd('/') + "/" + tpl;
        }

        static AttrInfo Attr(List<AttrInfo> l, string name) { return l.FirstOrDefault(a => a.Name == name); }

        // valor string de un argumento de atributo (literal, constante o concatenacion de constantes). null si no es string.
        string ArgString(ArgRef ar, TypeDecl ctx)
        {
            if (ar == null || ar.File == null) return null;
            var t = ar.File.Toks;
            if (ar.E - ar.S == 1 && IsI(t, ar.S) && t[ar.S].Text == "null") return null;
            var ev = ix.EvalStringExpr(ar.File, ar.S, ar.E, ctx, true);
            if (ev != null && ev.Complete) return ev.Value;
            return null;
        }

        // primera cadena posicional del atributo (plantilla de ruta), evaluando constantes
        string FirstString(AttrInfo a, TypeDecl ctx)
        {
            for (int i = 0; i < a.Positional.Count; i++)
            {
                // se evalua siempre (literal, interpolado con constantes $"{R.Version}/x" o concatenacion)
                if (i < a.PosArgs.Count) { var v = ArgString(a.PosArgs[i], ctx); if (v != null) return v; }
                if (a.PositionalIsString[i]) return a.Positional[i];
            }
            ArgRef nr;
            if (a.NamedArgs.TryGetValue("template", out nr)) return ArgString(nr, ctx);
            return null;
        }

        string NamedString(AttrInfo a, string name, TypeDecl ctx)
        {
            ArgRef nr;
            if (a.NamedArgs.TryGetValue(name, out nr)) return ArgString(nr, ctx);
            return null;
        }

        void FindConventionalTemplates()
        {
            foreach (var m in ix.Methods)
            {
                if (!m.HasBody) continue;
                var t = m.File.Toks;
                for (int k = m.BodyStart; k < m.BodyEnd && k < t.Count; k++)
                {
                    if (t[k].Kind != TokKind.Ident) continue;
                    string x = t[k].Text;
                    if (x != "MapHttpRoute" && x != "MapRoute" && x != "MapControllerRoute" && x != "MapAreaControllerRoute") continue;
                    if (!IsP(t, k + 1, "(")) continue;
                    var args = SplitArgs(m.File, k + 1);
                    string tpl = null, defCtrl = null, defAct = null;
                    var strs = new List<string>();
                    for (int ai = 0; ai < args.Count; ai++)
                    {
                        int s = args[ai][0], e = args[ai][1];
                        string named = null;
                        if (IsI(t, s) && IsP(t, s + 1, ":")) { named = t[s].Text; s += 2; }
                        if (IsI(t, s) && t[s].Text == "new")
                        {
                            // defaults: new { controller = "Blog", action = "Article" }
                            for (int q = s; q < e; q++)
                            {
                                if (IsI(t, q) && IsP(t, q + 1, "=") && q + 2 < e && t[q + 2].Kind == TokKind.Str)
                                {
                                    if (t[q].Text == "controller") defCtrl = t[q + 2].Lit.PlainValue();
                                    else if (t[q].Text == "action") defAct = t[q + 2].Lit.PlainValue();
                                }
                            }
                            continue;
                        }
                        var ev = ix.EvalStringExpr(m.File, s, e, m.Owner, true);
                        if (ev == null || !ev.Complete) continue;
                        if (named == "routeTemplate" || named == "pattern" || named == "url" || named == "template") tpl = ev.Value;
                        else if (named == null) strs.Add(ev.Value);
                    }
                    if (tpl == null) tpl = strs.FirstOrDefault(s => s.Contains("{"));
                    if (tpl == null && strs.Count > 1) tpl = strs[1];
                    if (tpl == null) continue;
                    // valores por defecto inline: {controller=Home}/{action=Index}
                    var mc = Regex.Match(tpl, @"\{controller=([^}]+)\}");
                    convRoutes.Add(new ConvRoute { Template = tpl, DefaultController = defCtrl, DefaultAction = defAct, WebApi = x == "MapHttpRoute" });
                }
            }
            var general = convRoutes.FirstOrDefault(c => c.Template.Contains("{controller") && c.WebApi) ?? convRoutes.FirstOrDefault(c => c.Template.Contains("{controller"));
            res.ConventionalTemplate = general != null ? general.Template : null;
        }

        ConvRoute PickConventional(bool webApi, string ctrl, string action)
        {
            var kind = convRoutes.Where(c => c.WebApi == webApi).ToList();
            if (kind.Count == 0) kind = convRoutes;
            // ruta dedicada (sin {controller}) cuyos valores por defecto apuntan a este controller/accion
            var ded = kind.FirstOrDefault(c => !c.Template.Contains("{controller") && c.DefaultController != null
                && string.Equals(c.DefaultController, ctrl, StringComparison.OrdinalIgnoreCase)
                && (c.DefaultAction == null || string.Equals(c.DefaultAction, action, StringComparison.OrdinalIgnoreCase)));
            if (ded != null) return ded;
            return kind.FirstOrDefault(c => c.Template.Contains("{controller"));
        }

        // ------------------------------------------------------------ controllers
        static bool HasRoutingAttrs(List<AttrInfo> l)
        {
            return l.Any(a => VerbAttrs.Contains(a.Name) || a.Name == "Route" || a.Name == "AcceptVerbs");
        }

        List<Endpoint> Controllers()
        {
            var eps = new List<Endpoint>();
            foreach (var td in ix.Types)
            {
                if (td.Kind != "class" || td.IsAbstract || td.IsStatic) continue;
                if (td.TypeParams.Count > 0) continue;              // generico abierto: ASP.NET no lo registra
                if (Attr(td.Attrs, "NonController") != null) continue;
                var anc = ix.GetAncestorNames(td);
                bool isWebApi2 = anc.Contains("ApiController") && !anc.Contains("ControllerBase");
                bool isCtrl = td.Name.EndsWith("Controller") || Attr(td.Attrs, "ApiController") != null || Attr(td.Attrs, "Controller") != null
                              || anc.Contains("Controller") || anc.Contains("ControllerBase") || anc.Contains("ApiController") || anc.Contains("ODataController");
                if (!isCtrl) continue;
                if (!td.IsPublic) continue;                         // los controllers deben ser publicos
                string ctrlName = td.Name.EndsWith("Controller") && td.Name.Length > 10 ? td.Name.Substring(0, td.Name.Length - 10) : td.Name;
                var chain = new List<TypeDecl> { td };
                chain.AddRange(ix.Ancestors(td).Where(a => a.Kind == "class"));
                // rutas de clase (heredables). Web API 2: [RoutePrefix] es prefijo y [Route] de clase es plantilla por defecto
                var classTpls = new List<string>();
                string routePrefix = null;
                foreach (var c in chain)
                {
                    foreach (var a in c.Attrs.Where(a => a.Name == "Route" || (a.Name == "RoutePrefix" && !isWebApi2)))
                    {
                        string s = FirstString(a, c);
                        if (s != null) classTpls.Add(s);
                    }
                    if (classTpls.Count > 0) break;
                }
                if (isWebApi2)
                    foreach (var c in chain)
                    {
                        var rp = c.Attrs.FirstOrDefault(a => a.Name == "RoutePrefix");
                        if (rp != null) { routePrefix = FirstString(rp, c); break; }
                    }
                string area = null;
                var areaAttr = chain.SelectMany(c => c.Attrs).FirstOrDefault(a => a.Name == "Area");
                if (areaAttr != null) area = FirstString(areaAttr, td);
                // acciones: propias + heredadas de controllers base del repo
                var actions = new List<MethodDecl>();
                var seen = new HashSet<string>();
                foreach (var c in chain)
                {
                    foreach (var md in c.Methods)
                    {
                        if (md.IsCtor || md.IsStatic || !md.IsPublic || md.IsAbstract || !md.HasBody || md.IsSynthetic || md.IsLocalFunction) continue;
                        if (md.AccessorKind != null || md.ExplicitIface != null || md.Name.StartsWith("op_") || md.Name.StartsWith("~")) continue;
                        if (Attr(md.Attrs, "NonAction") != null) continue;
                        if (md.Name == "Dispose" || md.Name == "ToString" || md.Name == "Equals" || md.Name == "GetHashCode") continue;
                        // firma con tipos: dos sobrecargas con la misma cantidad de parametros son acciones distintas
                        string sig = md.Name + "(" + string.Join(",", md.Params.Select(p => p.Type != null ? p.Type.ToString() : "?").ToArray()) + ")";
                        if (!seen.Add(sig)) continue;
                        actions.Add(md);
                    }
                }
                foreach (var md in actions)
                {
                    // un override sin atributos de ruta hereda los del metodo base (virtual/abstract)
                    var attrs = md.Attrs;
                    if (!HasRoutingAttrs(attrs) && md.IsOverride)
                    {
                        foreach (var a in ix.Ancestors(md.Owner))
                        {
                            var bm = a.Methods.FirstOrDefault(x => x.Name == md.Name && x.Params.Count == md.Params.Count && HasRoutingAttrs(x.Attrs));
                            if (bm != null) { attrs = bm.Attrs; break; }
                        }
                    }
                    if (attrs.Any(a => a.Name == "NonAction")) continue;
                    var verbsTpls = new List<KeyValuePair<string, string>>();
                    var routeTpls = new List<string>();
                    string actionName = md.Name;
                    var an = Attr(attrs, "ActionName");
                    if (an != null && FirstString(an, md.Owner) != null) actionName = FirstString(an, md.Owner);
                    else if (!isWebApi2 && actionName.EndsWith("Async") && actionName.Length > 5) actionName = actionName.Substring(0, actionName.Length - 5);
                    foreach (var a in attrs)
                    {
                        if (VerbAttrs.Contains(a.Name))
                        {
                            string v = a.Name.Substring(4).ToUpperInvariant();
                            verbsTpls.Add(new KeyValuePair<string, string>(v, FirstString(a, md.Owner)));
                        }
                        else if (a.Name == "AcceptVerbs")
                        {
                            string rt = NamedString(a, "Route", md.Owner);
                            for (int i = 0; i < a.Positional.Count; i++)
                            {
                                string pv = a.PositionalIsString[i] ? a.Positional[i] : (i < a.PosArgs.Count ? ArgString(a.PosArgs[i], md.Owner) : null);
                                if (pv == null)
                                {
                                    var mm = Regex.Match(a.Positional[i], @"(Get|Post|Put|Delete|Patch|Head|Options)\b", RegexOptions.IgnoreCase);
                                    if (!mm.Success) continue;
                                    pv = mm.Value;
                                }
                                verbsTpls.Add(new KeyValuePair<string, string>(pv.ToUpperInvariant(), rt));
                            }
                        }
                        else if (a.Name == "Route")
                        {
                            string s = FirstString(a, md.Owner); if (s != null) routeTpls.Add(s);
                        }
                    }
                    bool attributeRouted = classTpls.Count > 0 || routeTpls.Count > 0 || verbsTpls.Any(x => x.Value != null);
                    if (verbsTpls.Count == 0)
                    {
                        string v = "ANY";
                        if (isWebApi2)
                        {
                            var mm = Regex.Match(md.Name, @"^(Get|Post|Put|Delete|Patch|Head|Options)");
                            v = mm.Success ? mm.Value.ToUpperInvariant() : "POST";
                        }
                        verbsTpls.Add(new KeyValuePair<string, string>(v, null));
                    }
                    var routes = new List<KeyValuePair<string, string>>();
                    if (!attributeRouted)
                    {
                        var conv = PickConventional(isWebApi2, ctrlName, actionName);
                        string tpl = conv != null ? conv.Template : (isWebApi2 ? "api/{controller}/{id}" : "api/[controller]");
                        string r = ConventionalRoute(tpl, ctrlName, actionName, md);
                        foreach (var vt in verbsTpls) routes.Add(new KeyValuePair<string, string>(vt.Key, r));
                    }
                    else
                    {
                        var prefixes = classTpls.Count > 0 ? classTpls : new List<string> { "" };
                        foreach (var vt in verbsTpls)
                        {
                            var tpls = vt.Value != null ? new List<string> { vt.Value } : (routeTpls.Count > 0 ? routeTpls : new List<string> { null });
                            foreach (var p in prefixes)
                                foreach (var tp in tpls)
                                {
                                    string full = Combine(p, tp);
                                    if (routePrefix != null && !(tp != null && (tp.StartsWith("~/") || tp.StartsWith("/")))) full = Combine(routePrefix, full);
                                    routes.Add(new KeyValuePair<string, string>(vt.Key, full));
                                }
                        }
                    }
                    var done = new HashSet<string>();
                    foreach (var vr in routes)
                    {
                        string r = vr.Value;
                        r = Regex.Replace(r, @"\[controller\]", ctrlName, RegexOptions.IgnoreCase);
                        r = Regex.Replace(r, @"\[action\]", actionName, RegexOptions.IgnoreCase);
                        if (area != null) r = Regex.Replace(r, @"\[area\]", area, RegexOptions.IgnoreCase);
                        r = Normalize(r);
                        if (!done.Add(vr.Key + " " + r)) continue;
                        var ep = new Endpoint { Verb = vr.Key, Route = r, Kind = isWebApi2 ? "WebApi2" : "Controller", Handler = md, HandlerName = td.Name + "." + md.Name, File = md.File, Line = md.Line, ViaType = td };
                        if (!attributeRouted) ep.Note = "ruta convencional";
                        eps.Add(ep);
                    }
                }
            }
            return eps;
        }

        string ConventionalRoute(string tpl, string ctrl, string action, MethodDecl md)
        {
            string r = tpl;
            r = Regex.Replace(r, @"\{controller(=[^}]*)?\}", ctrl, RegexOptions.IgnoreCase);
            r = Regex.Replace(r, @"\[controller\]", ctrl, RegexOptions.IgnoreCase);
            r = Regex.Replace(r, @"\{action(=[^}]*)?\}", action, RegexOptions.IgnoreCase);
            // parametros: se conservan solo si la accion tiene un parametro con ese nombre
            r = Regex.Replace(r, @"\{(\*{0,2})([A-Za-z_]\w*)[^}]*\}", mm =>
            {
                string pn = mm.Groups[2].Value;
                return md.Params.Any(p => string.Equals(p.Name, pn, StringComparison.OrdinalIgnoreCase)) ? "{" + pn + "}" : "";
            });
            return r;
        }

        // ------------------------------------------------------------ minimal APIs
        static readonly Dictionary<string, string> MapVerbs = new Dictionary<string, string> {
            { "MapGet", "GET" }, { "MapPost", "POST" }, { "MapPut", "PUT" }, { "MapDelete", "DELETE" }, { "MapPatch", "PATCH" }, { "MapMethods", "*" }, { "Map", "ANY" } };

        List<Endpoint> MinimalApis()
        {
            var eps = new List<Endpoint>();
            foreach (var m in ix.Methods.ToList())
            {
                if (!m.HasBody) continue;
                foreach (var cs in ix.Calls(m))
                {
                    if (cs.IsNew || cs.IsMethodGroup || !MapVerbs.ContainsKey(cs.Name) || cs.ArgOpen < 0) continue;
                    if (cs.Receiver.Count == 0) continue;
                    if (ix.InLocalFunction(m, cs.Tok)) continue;   // se procesa en la propia funcion local
                    var f = m.File; var t = f.Toks;
                    var args = SplitArgs(f, cs.ArgOpen);
                    if (args.Count < 2) continue;
                    var verbs = new List<string>();
                    int handlerArg = 1;
                    if (cs.Name == "MapMethods")
                    {
                        handlerArg = 2;
                        if (args.Count < 3) continue;
                        for (int q = args[1][0]; q < args[1][1]; q++)
                        {
                            if (t[q].Kind == TokKind.Str) verbs.Add(t[q].Lit.PlainValue().ToUpperInvariant());
                            else if (IsI(t, q) && IsP(t, q - 1, ".") && Regex.IsMatch(t[q].Text, "^(Get|Post|Put|Delete|Patch|Head|Options)$", RegexOptions.IgnoreCase)) verbs.Add(t[q].Text.ToUpperInvariant());
                        }
                        if (verbs.Count == 0) verbs.Add("ANY");
                    }
                    else verbs.Add(MapVerbs[cs.Name]);
                    // handler
                    int hs = args[handlerArg][0], he = args[handlerArg][1];
                    if (cs.Name == "Map" && !LooksLikeEndpointHandler(t, hs, he)) continue;  // app.Map("/x", b => b.Run(...)) es middleware
                    MethodDecl handler = BuildHandler(m, hs, he);
                    if (cs.Name == "Map" && handler == null) continue;
                    var tpls = TemplateValues(m, args[0][0], args[0][1], 0);
                    string hname;
                    if (handler != null && handler.IsSynthetic) hname = m.Owner.Name + "." + (m.IsSynthetic ? "<top-level>" : m.Name) + " -> lambda";
                    else if (handler != null) hname = handler.DisplayName;
                    else hname = "(handler no resuelto)";
                    // nombre de operacion .WithName("X")
                    string opName = null;
                    int close = f.Match[cs.ArgOpen];
                    int z = close + 1, guard = 0;
                    while (IsP(t, z, ".") && IsI(t, z + 1) && IsP(t, z + 2, "(") && guard++ < 30)
                    {
                        int c2 = f.Match[z + 2];
                        if (t[z + 1].Text == "WithName" && z + 3 < c2 && t[z + 3].Kind == TokKind.Str) opName = t[z + 3].Lit.PlainValue();
                        if (c2 < 0) break;
                        z = c2 + 1;
                    }
                    int chainStart;
                    ix.WalkBack(f, cs.Tok - 1, out chainStart);
                    var extra = StatementComments(m, chainStart);
                    var prefixes = ReceiverPrefixes(m, cs.Receiver, 0, null);
                    foreach (var pf in prefixes.Distinct())
                        foreach (var tpl in tpls)
                            foreach (var v in verbs)
                            {
                                var ep = new Endpoint
                                {
                                    Verb = v, Route = Normalize(Join(pf, tpl)), Kind = "MinimalApi", Handler = handler, HandlerName = hname,
                                    File = f, Line = cs.Line, OperationName = opName
                                };
                                ep.ExtraMarkers.AddRange(extra);
                                if (handler == null) res.Warnings.Add(new Warn { Category = "Endpoint", Message = "No se pudo resolver el handler de " + v + " " + ep.Route, Loc = new Location(f, cs.Line) });
                                eps.Add(ep);
                            }
                }
            }
            return eps;
        }

        static bool LooksLikeEndpointHandler(List<Token> t, int hs, int he)
        {
            // lambda con un unico parametro sin tipo (b => b.Run(...)) o que usa Run/Use: middleware
            int k = hs;
            if (IsI(t, k) && IsP(t, k + 1, "=>")) return false;
            for (int q = hs; q < he; q++) if (IsI(t, q) && (t[q].Text == "Run" || t[q].Text == "Use" || t[q].Text == "UseMiddleware") && IsP(t, q - 1, ".")) return false;
            return true;
        }

        // valores posibles de una plantilla: literal, constante, variable local o parametro (via sus llamadores)
        List<string> TemplateValues(MethodDecl m, int s, int e, int depth)
        {
            var r = new List<string>();
            var f = m.File; var t = f.Toks;
            var ev = ix.EvalStringExpr(f, s, e, m.Owner, true, m, 0);
            if (ev != null && ev.Complete) { r.Add(ev.Value); return r; }
            if (e - s == 1 && IsI(t, s) && depth < 6)
            {
                int pi = m.Params.FindIndex(p => p.Name == t[s].Text);
                if (pi >= 0)
                {
                    foreach (var kv in CallersOf(m))
                    {
                        var caller = kv.Key; var cs = kv.Value;
                        int argPos = pi - (m.IsExtension && cs.Receiver.Count > 0 ? 1 : 0);
                        if (cs.ArgOpen < 0) continue;
                        var args = SplitArgs(caller.File, cs.ArgOpen);
                        if (argPos < 0 || argPos >= args.Count) continue;
                        r.AddRange(TemplateValues(caller, args[argPos][0], args[argPos][1], depth + 1));
                    }
                    if (r.Count > 0) return r.Distinct().ToList();
                }
            }
            r.Add("{?}");
            return r;
        }

        public List<int[]> SplitArgs(SourceFile f, int open)
        {
            var t = f.Toks; var mt = f.Match;
            var r = new List<int[]>();
            int close = mt[open];
            if (close < 0) return r;
            int s = open + 1;
            for (int k = open + 1; k < close; k++)
            {
                if (t[k].Kind != TokKind.Punct) continue;
                string x = t[k].Text;
                if ((x == "(" || x == "[" || x == "{") && mt[k] > k) { k = mt[k]; continue; }
                if (x == "<" && IsI(t, k - 1)) { int g = ix.P(f).SkipGeneric(k); if (g > 0 && g < close && (IsP(t, g + 1, "(") || IsP(t, g + 1, "."))) { k = g; continue; } }
                if (x == ",") { r.Add(new int[] { s, k }); s = k + 1; }
            }
            if (close > s) r.Add(new int[] { s, close });
            return r;
        }

        int lambdaSeq = 0;
        MethodDecl BuildHandler(MethodDecl m, int hs, int he)
        {
            var f = m.File; var t = f.Toks; var mt = f.Match;
            int k = hs;
            var attrs = new List<AttrInfo>();
            while (IsP(t, k, "[") && mt[k] > k && mt[k] < he) { ix.P(f).ParseAttrSection(k, mt[k], attrs); k = mt[k] + 1; }
            while (IsI(t, k) && (t[k].Text == "static" || t[k].Text == "async")) k++;
            // lambda: buscar "=>" a profundidad 0 (admite tipo de retorno explicito: async Task<IResult> (...) =>)
            int arrow = -1;
            for (int q = k; q < he; q++)
            {
                if ((IsP(t, q, "(") || IsP(t, q, "[") || IsP(t, q, "{")) && mt[q] > q) { q = mt[q]; continue; }
                if (IsP(t, q, "=>")) { arrow = q; break; }
            }
            if (arrow > 0)
            {
                var md = new MethodDecl { Name = "lambda@" + t[hs].Line, Owner = m.Owner, File = f, Line = t[hs].Line, IsSynthetic = true, IsStatic = true, Attrs = attrs, Parent = m };
                md.Id = "L" + (++lambdaSeq);
                int pc = arrow - 1;
                if (IsP(t, pc, ")") && mt[pc] >= k) md.Params = ix.P(f).ParseParams(mt[pc], pc);
                else if (IsI(t, pc)) md.Params.Add(new ParamInfo { Name = t[pc].Text });
                int b = arrow + 1;
                if (IsP(t, b, "{") && mt[b] > b) { md.BodyStart = b + 1; md.BodyEnd = mt[b]; }
                else { md.BodyStart = b; md.BodyEnd = he; }
                md.DeclStartOffset = t[hs].Start; md.DeclEndOffset = t[he - 1].End;
                return md;
            }
            // grupo de metodos: Nombre | Tipo.Nombre | instancia.Nombre
            var names = new List<string>();
            int j = k;
            while (IsI(t, j)) { names.Add(t[j].Text); if (IsP(t, j + 1, ".") && IsI(t, j + 2)) j += 2; else { j++; break; } }
            if (names.Count == 0 || j != he) return null;
            string mname = names[names.Count - 1];
            var cands = new List<MethodDecl>();
            if (names.Count == 1)
            {
                foreach (var lf in ix.LocalFunctionsInScope(m)) if (lf.Name == mname) cands.Add(lf);
                var o = m.Owner;
                while (o != null && cands.Count == 0) { cands.AddRange(ix.MethodsNamed(o.MergedInto ?? o, mname, true)); o = o.Outer; }
                if (cands.Count == 0) { List<MethodDecl> l; if (ix.MethodsByName.TryGetValue(mname, out l) && l.Select(x => x.Owner).Distinct().Count() == 1) cands.AddRange(l); }
            }
            else
            {
                foreach (var td in ix.ResolveQualified(names.Take(names.Count - 1).ToList(), m.Owner)) cands.AddRange(ix.MethodsNamed(td, mname, true));
                if (cands.Count == 0)
                {
                    var cs = new CallSite { Name = mname, Receiver = names.Take(names.Count - 1).Select(x => new Seg { Name = x }).ToList(), Argc = -1, IsMethodGroup = true };
                    bool inf, unr; cands.AddRange(ix.ResolveCall(m, cs, out inf, out unr));
                }
            }
            return cands.FirstOrDefault(x => x.HasBody) ?? cands.FirstOrDefault();
        }

        List<Marker> StatementComments(MethodDecl m, int stmtTok)
        {
            var r = new List<Marker>();
            var f = m.File; var t = f.Toks;
            int k = Math.Max(m.BodyStart, Math.Min(stmtTok, t.Count - 1));
            int p = k - 1;
            while (p >= m.BodyStart - 1 && p >= 0 && !(IsP(t, p, ";") || IsP(t, p, "{") || IsP(t, p, "}"))) p--;
            int from = p >= 0 ? t[p].End : 0;
            int to = t[k].Start;
            var cs = f.CommentsBetween(from, to).Where(c => !c.IsPreproc || c.Text.IndexOf("region", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (p >= 0) cs = cs.Where(c => c.Line != t[p].EndLine || c.Start > t[p].End + 0 && c.Line != t[p].Line).ToList();
            foreach (var c in cs)
                foreach (var h in sm.FindInText(c.Text))
                {
                    int line = c.Line;
                    for (int z = 0; z < h.Offset && z < c.Text.Length; z++) if (c.Text[z] == '\n') line++;
                    r.Add(fx.NewMarker(h.Sp, new Location(f, line), "endpoint-comment", c.Text));
                }
            return r;
        }

        // prefijos de MapGroup para el receptor de una llamada Map*. ovr: prefijos fijados para parametros (al evaluar un metodo fabrica)
        List<string> ReceiverPrefixes(MethodDecl m, List<Seg> segs, int depth, Dictionary<string, string> ovr)
        {
            var r = new List<string>();
            if (segs == null || segs.Count == 0 || depth > 48) { r.Add(""); return r; }
            List<string> roots;
            var s0 = segs[0];
            int startIdx = 0;
            if (s0.IsCall && s0.Name == "MapGroup") roots = new List<string> { "" };
            else if (s0.IsCall)
            {
                // grupo devuelto por un metodo del repo: CreateApiGroup(app)
                roots = ReturnPrefixesOfCall(m, s0, null, depth + 1);
                startIdx = 1;
            }
            else
            {
                roots = NamePrefixes(m, s0.Name, depth + 1, ovr);
                startIdx = 1;
            }
            foreach (var root in roots)
            {
                var cur = new List<string> { root };
                for (int i = startIdx; i < segs.Count; i++)
                {
                    var s = segs[i];
                    if (!s.IsCall) continue;
                    if (s.Name == "MapGroup" && s.ArgOpen >= 0)
                    {
                        var args = SplitArgs(m.File, s.ArgOpen);
                        if (args.Count > 0)
                        {
                            var vals = TemplateValues(m, args[0][0], args[0][1], 0);
                            cur = cur.SelectMany(c => vals.Select(v => Join(c, v))).ToList();
                        }
                        continue;
                    }
                    // metodo de extension del repo que devuelve un grupo: app.MapApiV1()
                    var next = new List<string>();
                    bool resolved = false;
                    foreach (var c in cur)
                    {
                        var rp = ReturnPrefixesOfCall(m, s, c, depth + 1);
                        if (rp != null) { resolved = true; next.AddRange(rp); }
                    }
                    if (resolved) cur = next;
                }
                r.AddRange(cur);
            }
            return r;
        }

        // prefijos que devuelve un metodo del repo (fabrica de grupos). thisPrefix: prefijo del receptor (metodos de extension)
        List<string> ReturnPrefixesOfCall(MethodDecl m, Seg s, string thisPrefix, int depth)
        {
            if (depth > 40) return null;
            var targets = new List<MethodDecl>();
            foreach (var lf in ix.LocalFunctionsInScope(m)) if (lf.Name == s.Name) targets.Add(lf);
            if (targets.Count == 0)
            {
                List<MethodDecl> l;
                if (ix.MethodsByName.TryGetValue(s.Name, out l))
                    targets.AddRange(l.Where(x => x.HasBody && x.ReturnType != null && RouteBuilderTypes.Contains(U.UnwrapRef(x.ReturnType).Name)));
            }
            if (targets.Count == 0) return null;
            var r = new List<string>();
            foreach (var tg in targets)
            {
                var ovr = new Dictionary<string, string>();
                if (thisPrefix != null && tg.IsExtension && tg.Params.Count > 0) ovr[tg.Params[0].Name] = thisPrefix;
                else if (s.ArgOpen >= 0)
                {
                    // argumentos que son grupos: CreateApiGroup(app.MapGroup("/x"))
                    var args = SplitArgs(m.File, s.ArgOpen);
                    int off = tg.IsExtension && thisPrefix != null ? 1 : 0;
                    for (int i = 0; i < args.Count && i + off < tg.Params.Count; i++)
                    {
                        var segs = ix.ParseChainForward(m.File, args[i][0]);
                        if (segs.Count > 0) ovr[tg.Params[i + off].Name] = ReceiverPrefixes(m, segs, depth + 1, null).FirstOrDefault() ?? "";
                    }
                }
                var t = tg.File.Toks;
                // expresion de retorno: cuerpo "=>" o sentencias "return"
                var starts = new List<int>();
                if (tg.BodyStart > 0 && IsP(t, tg.BodyStart - 1, "=>")) starts.Add(tg.BodyStart);
                for (int k = tg.BodyStart; k < tg.BodyEnd && k < t.Count; k++) if (IsI(t, k) && t[k].Text == "return") starts.Add(k + 1);
                foreach (var st in starts)
                {
                    var segs = ix.ParseChainForward(tg.File, st);
                    if (segs.Count == 0) continue;
                    r.AddRange(ReceiverPrefixes(tg, segs, depth + 1, ovr));
                }
            }
            return r.Count > 0 ? r.Distinct().ToList() : null;
        }

        List<string> NamePrefixes(MethodDecl m, string name, int depth, Dictionary<string, string> ovr)
        {
            var r = new List<string>();
            if (ovr != null && ovr.ContainsKey(name)) { r.Add(ovr[name]); return r; }
            LocalInfo li;
            if (m.HasBody && ix.Locals(m).TryGetValue(name, out li) && li.ExprTok >= 0)
            {
                var t = m.File.Toks;
                var segs = ix.ParseChainForward(m.File, li.ExprTok);
                if (segs.Count > 0 && segs[0].Name == name) { r.Add(""); return r; }
                return ReceiverPrefixes(m, segs, depth + 1, ovr);
            }
            int pi = m.Params.FindIndex(p => p.Name == name);
            if (pi >= 0)
            {
                var prefixes = CallerPrefixes(m, pi, depth + 1);
                // Carter: CarterModule("/prefijo")
                string carter = CarterPrefix(m.Owner);
                if (carter != null) prefixes = prefixes.Select(x => Join(x, carter)).ToList();
                return prefixes;
            }
            if (m.Parent != null) return NamePrefixes(m.Parent, name, depth + 1, ovr);
            string cp = CarterPrefix(m.Owner);
            if (cp != null) { r.Add(cp); return r; }
            r.Add("");
            return r;
        }

        string CarterPrefix(TypeDecl td)
        {
            if (td == null || !ix.DerivesFrom(td, "CarterModule")) return null;
            foreach (var ar in td.BaseCtorArgs) { var v = ArgString(ar, td); if (v != null) return v; }
            if (td.BaseCtorStrings.Count > 0) return td.BaseCtorStrings[0];
            return null;
        }

        List<KeyValuePair<MethodDecl, CallSite>> CallersOf(MethodDecl target)
        {
            if (callersByName == null)
            {
                callersByName = new Dictionary<string, List<KeyValuePair<MethodDecl, CallSite>>>();
                foreach (var m in ix.Methods)
                    foreach (var cs in ix.Calls(m))
                    {
                        if (cs.IsNew || cs.IsMethodGroup) continue;
                        List<KeyValuePair<MethodDecl, CallSite>> l;
                        if (!callersByName.TryGetValue(cs.Name, out l)) { l = new List<KeyValuePair<MethodDecl, CallSite>>(); callersByName[cs.Name] = l; }
                        l.Add(new KeyValuePair<MethodDecl, CallSite>(m, cs));
                    }
            }
            var r = new List<KeyValuePair<MethodDecl, CallSite>>();
            List<KeyValuePair<MethodDecl, CallSite>> callers;
            if (target.IsSynthetic || !callersByName.TryGetValue(target.Name, out callers)) return r;
            // otros metodos con el mismo nombre que reciben grupos: solo se aceptan llamadas sin resolver si el nombre es unico
            List<MethodDecl> same;
            int sameCount = ix.MethodsByName.TryGetValue(target.Name, out same) ? same.Count(x => x.Params.Any(p => p.Type != null && RouteBuilderTypes.Contains(p.Type.Name))) : 1;
            foreach (var kv in callers)
            {
                var caller = kv.Key; var cs = kv.Value;
                if (caller == target) continue;
                if (target.IsLocalFunction && !ix.LocalFunctionsInScope(caller).Contains(target) && caller != target.Parent) continue;
                bool inf, unr;
                var tg = ix.ResolveCall(caller, cs, out inf, out unr);
                if (tg.Contains(target)) { r.Add(kv); continue; }
                if (tg.Count == 0 && sameCount <= 1 && !target.IsLocalFunction)
                {
                    int expected = target.Params.Count - (target.IsExtension && cs.Receiver.Count > 0 ? 1 : 0);
                    if (cs.Argc >= 0 && cs.Argc > expected && !target.Params.Any(p => p.IsParams)) continue;
                    r.Add(kv);
                }
            }
            return r;
        }

        List<string> CallerPrefixes(MethodDecl target, int paramIndex, int depth)
        {
            var r = new List<string>();
            bool ext = target.IsExtension;
            foreach (var kv in CallersOf(target))
            {
                var caller = kv.Key; var cs = kv.Value;
                if (ext && paramIndex == 0 && cs.Receiver.Count > 0)
                {
                    r.AddRange(ReceiverPrefixes(caller, cs.Receiver, depth + 1, null));
                    continue;
                }
                int argPos = paramIndex - (ext && cs.Receiver.Count > 0 ? 1 : 0);
                if (cs.ArgOpen < 0) continue;
                var args = SplitArgs(caller.File, cs.ArgOpen);
                if (argPos < 0 || argPos >= args.Count) continue;
                var segs = ix.ParseChainForward(caller.File, args[argPos][0]);
                r.AddRange(ReceiverPrefixes(caller, segs, depth + 1, null));
            }
            if (r.Count == 0) r.Add("");
            return r.Distinct().ToList();
        }

        // ------------------------------------------------------------ FastEndpoints
        void FindFastEndpointsPrefix()
        {
            foreach (var m in ix.Methods)
            {
                if (!m.HasBody) continue;
                var t = m.File.Toks;
                bool fe = false;
                for (int k = m.BodyStart; k < m.BodyEnd && k < t.Count; k++) if (IsI(t, k) && t[k].Text == "UseFastEndpoints") { fe = true; break; }
                if (!fe) continue;
                for (int k = m.BodyStart; k < m.BodyEnd && k < t.Count; k++)
                {
                    if (IsI(t, k) && t[k].Text == "RoutePrefix" && IsP(t, k + 1, "="))
                    {
                        int e = ix.P(m.File).FindStmtEnd(k + 2, m.BodyEnd);
                        var ev = ix.EvalStringExpr(m.File, k + 2, e, m.Owner, true, m, 0);
                        if (ev != null && ev.Complete) fastEndpointsPrefix = ev.Value;
                    }
                }
            }
        }

        List<Endpoint> FastEndpoints()
        {
            var eps = new List<Endpoint>();
            foreach (var td in ix.Types)
            {
                if (td.Kind != "class" || td.IsAbstract) continue;
                var anc = ix.GetAncestorNames(td);
                if (!anc.Any(a => a == "Endpoint" || a == "EndpointWithoutRequest" || a == "EndpointWithMapping" || a.StartsWith("Endpoint"))) continue;
                var conf = td.Methods.FirstOrDefault(x => x.Name == "Configure" && x.HasBody);
                if (conf == null) continue;
                var handler = td.Methods.FirstOrDefault(x => (x.Name == "HandleAsync" || x.Name == "ExecuteAsync") && x.HasBody);
                var t = conf.File.Toks;
                var verbs = new List<string>(); var routes = new List<string>();
                foreach (var cs in ix.Calls(conf))
                {
                    if (cs.ArgOpen < 0 || cs.Receiver.Count > 0) continue;
                    var args = SplitArgs(conf.File, cs.ArgOpen);
                    var strs = new List<string>();
                    foreach (var a in args) { var ev = ix.EvalStringExpr(conf.File, a[0], a[1], td, true); if (ev != null) strs.Add(ev.Value); }
                    if (Regex.IsMatch(cs.Name, "^(Get|Post|Put|Delete|Patch)$")) { verbs.Add(cs.Name.ToUpperInvariant()); routes.AddRange(strs); }
                    else if (cs.Name == "Routes") routes.AddRange(strs);
                    else if (cs.Name == "Verbs")
                    {
                        foreach (var s in strs) verbs.Add(s.ToUpperInvariant());
                        for (int q = cs.ArgOpen; q < conf.File.Match[cs.ArgOpen]; q++)
                            if (IsI(t, q) && Regex.IsMatch(t[q].Text, "^(GET|POST|PUT|DELETE|PATCH|HEAD|OPTIONS|Get|Post|Put|Delete|Patch|Head|Options)$")) verbs.Add(t[q].Text.ToUpperInvariant());
                    }
                }
                if (routes.Count == 0) continue;
                if (verbs.Count == 0) verbs.Add("ANY");
                var extra = new List<Marker>();
                foreach (var c in td.Leading) foreach (var h in sm.FindInText(c.Text)) extra.Add(fx.NewMarker(h.Sp, new Location(td.File, c.Line), "endpoint-comment", c.Text));
                foreach (var v in verbs.Distinct()) foreach (var rt in routes.Distinct())
                    {
                        string route = fastEndpointsPrefix != null ? Join(fastEndpointsPrefix, rt) : rt;
                        var ep = new Endpoint { Verb = v, Route = Normalize(route), Kind = "FastEndpoints", Handler = handler, HandlerName = td.Name + "." + (handler != null ? handler.Name : "?"), File = td.File, Line = td.Line, ViaType = td };
                        ep.ExtraMarkers.AddRange(extra);
                        eps.Add(ep);
                    }
            }
            return eps;
        }

        // ------------------------------------------------------------ Azure Functions
        List<Endpoint> AzureFunctions()
        {
            var eps = new List<Endpoint>();
            foreach (var md in ix.Methods)
            {
                if (!md.HasBody || md.IsSynthetic || md.IsLocalFunction) continue;
                foreach (var p in md.Params)
                {
                    var trig = p.Attrs.FirstOrDefault(a => a.Name == "HttpTrigger");
                    if (trig == null) continue;
                    var verbs = new List<string>();
                    for (int i = 0; i < trig.Positional.Count; i++)
                    {
                        string v = trig.PositionalIsString[i] ? trig.Positional[i] : (i < trig.PosArgs.Count ? ArgString(trig.PosArgs[i], md.Owner) : null);
                        if (v != null && Regex.IsMatch(v, "^[A-Za-z]+$")) verbs.Add(v.ToUpperInvariant());
                    }
                    if (verbs.Count == 0) verbs.Add("ANY");
                    string route = NamedString(trig, "Route", md.Owner);
                    if (route == null)
                    {
                        var fa = md.Attrs.FirstOrDefault(a => a.Name == "Function" || a.Name == "FunctionName");
                        route = fa != null && FirstString(fa, md.Owner) != null ? FirstString(fa, md.Owner) : md.Name;
                    }
                    foreach (var v in verbs)
                        eps.Add(new Endpoint { Verb = v, Route = Normalize(Join("api", route)), Kind = "AzureFunction", Handler = md, HandlerName = md.DisplayName, File = md.File, Line = md.Line, ViaType = md.Owner });
                }
            }
            return eps;
        }
    }
}

namespace SPA_NS
{
    // =====================================================================
    //  CLASIFICACION, RECORRIDO POR ENDPOINT Y MOTOR PRINCIPAL
    // =====================================================================
    public class FragDecision
    {
        public List<SpHit> Direct = new List<SpHit>();
        public List<SpHit> DirectInQuery = new List<SpHit>();
        public List<Marker> Migrated = new List<Marker>();
        public List<SpHit> Children = new List<SpHit>();
        public List<Marker> Used = new List<Marker>();
        public List<Marker> Unmatched = new List<Marker>();
        public string Level;
        public bool Inferred;
    }

    public class RawItem
    {
        public Fragment Fr;
        public FragDecision Dec;
        public string Trace;
        public bool InferredPath;
    }

    public class Engine
    {
        public AnalyzerOptions Opt;
        public CodeIndex Ix;
        public SpMatcher Sm;
        public FragmentExtractor Fx;
        public AnalysisResult Res = new AnalysisResult();
        public List<string> Log = new List<string>();
        Dictionary<string, Dictionary<string, bool>> groupRegularCache = new Dictionary<string, Dictionary<string, bool>>();

        public static AnalysisResult Run(AnalyzerOptions opt)
        {
            var e = new Engine();
            e.Opt = opt;
            e.Execute();
            return e.Res;
        }

        string Rel(string path)
        {
            string root = Opt.RepoRoot.TrimEnd('\\', '/');
            string p = path;
            if (p.StartsWith(root, StringComparison.OrdinalIgnoreCase)) p = p.Substring(root.Length).TrimStart('\\', '/');
            return p.Replace('\\', '/');
        }

        SourceFile LoadText(string path)
        {
            var sf = new SourceFile { Path = path, Rel = Rel(path) };
            string enc;
            string text = ReadSource(path, out enc);
            if (enc == "windows-1252") Res.EncodingNotes.Add(sf.Rel);
            // CR suelto, NEL, LS y PS tambien terminan linea en C#: se normalizan a LF (CRLF -> LF conserva el numero de linea)
            text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace((char)0x85, '\n').Replace((char)0x2028, '\n').Replace((char)0x2029, '\n');
            sf.Text = text;
            sf.LineStarts = SourceFile.ComputeLineStarts(sf.Text);
            return sf;
        }

        // UTF-8/UTF-16/UTF-32 con BOM; sin BOM: UTF-8 estricto y, si no es valido, Windows-1252 (fuentes legacy en ANSI)
        static string ReadSource(string path, out string encName)
        {
            var bytes = File.ReadAllBytes(path);
            encName = "utf-8";
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0) { encName = "utf-32"; return new UTF32Encoding(false, true).GetString(bytes, 4, bytes.Length - 4); }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { encName = "utf-16"; return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2); }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) { encName = "utf-16be"; return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2); }
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { }
            encName = "windows-1252";
            Encoding ansi;
            try { ansi = Encoding.GetEncoding(1252); } catch { ansi = Encoding.GetEncoding("iso-8859-1"); }
            return ansi.GetString(bytes);
        }

        public void Execute()
        {
            Sm = new SpMatcher(Opt);
            Ix = new CodeIndex { Opt = Opt };
            var types = new List<TypeDecl>();
            var methods = new List<MethodDecl>();
            foreach (var path in Opt.CsFiles)
            {
                try
                {
                    var sf = LoadText(path);
                    Lexer.Lex(sf);
                    var p = new Parser(sf);
                    p.Run();
                    Ix.Parsers[sf] = p;
                    Ix.Files.Add(sf);
                    types.AddRange(p.Types);
                    methods.AddRange(p.Methods);
                }
                catch (Exception ex) { Res.ParseErrors.Add(Rel(path) + ": " + ex.Message); }
            }
            foreach (var path in Opt.SqlFiles)
            {
                try { var sf = LoadText(path); sf.IsSql = true; Ix.SqlFiles.Add(sf); }
                catch (Exception ex) { Res.ParseErrors.Add(Rel(path) + ": " + ex.Message); }
            }
            foreach (var path in Opt.ResxFiles)
            {
                try { LoadResx(path); } catch (Exception ex) { Res.ParseErrors.Add(Rel(path) + ": " + ex.Message); }
            }
            foreach (var path in Opt.ConfigFiles)
            {
                try { LoadConfig(path); } catch (Exception ex) { Res.ParseErrors.Add(Rel(path) + ": " + ex.Message); }
            }
            // global using / global using static / alias globales: aplican a todos los archivos
            var gUsings = Ix.Files.SelectMany(x => x.GlobalUsings).Distinct().ToList();
            var gAliases = new Dictionary<string, string>();
            foreach (var x in Ix.Files) foreach (var kv in x.GlobalAliases) gAliases[kv.Key] = kv.Value;
            foreach (var x in Ix.Files)
            {
                foreach (var u in gUsings) if (!x.Usings.Contains(u)) x.Usings.Add(u);
                foreach (var kv in gAliases) if (!x.Aliases.ContainsKey(kv.Key)) x.Aliases[kv.Key] = kv.Value;
            }
            Ix.Build(types, methods);
            Res.FilesCs = Ix.Files.Count; Res.FilesSql = Ix.SqlFiles.Count; Res.Types = Ix.Types.Count; Res.Methods = Ix.Methods.Count;
            Fx = new FragmentExtractor(Ix, Sm);
            Fx.RegisterCommentFiles(Ix.Files);

            var finder = new EndpointFinder(Ix, Fx, Sm, Res);
            Res.Endpoints = finder.Find();
            foreach (var ep in Res.Endpoints) if (ep.Handler != null) Fx.EntryMethods.Add(ep.Handler.Id);

            // pasada global: catalogo de SP migrados en todo el repo (sin marcadores heredados)
            Ix.TrackRefs = true;
            foreach (var m in Ix.Methods)
            {
                var ma = Fx.Analyze(m);
                foreach (var fr in ma.Fragments)
                {
                    if (fr.MessageContext) continue;
                    var dec = Decide(fr, ma, null);
                    AddToCatalog(fr, dec);
                }
            }
            // recorrido por endpoint
            var visitedAll = new HashSet<string>();
            var perEp = new Dictionary<string, List<RawItem>>();
            var epMarkers = new Dictionary<string, List<Marker>>();
            var epUsed = new Dictionary<string, HashSet<string>>();
            foreach (var ep in Res.Endpoints)
            {
                var items = new List<RawItem>();
                var markers = new List<Marker>(ep.ExtraMarkers);
                var used = new HashSet<string>();
                var trace = new List<string>();
                var unresolved = new HashSet<string>();
                var warns = new List<Warn>();
                if (ep.Handler != null)
                {
                    var visited = new Dictionary<string, int>();
                    curMarkerTrace = new Dictionary<string, string>();
                    foreach (var mk in ep.ExtraMarkers) curMarkerTrace[mk.Id] = ep.HandlerName;
                    Dfs(ep, ep.Handler, ep.ViaType, ep.ExtraMarkers, "ep", 0, new List<string>(), false, visited, items, markers, used, visitedAll, trace, unresolved, warns);
                    epMarkerTrace[ep.Id] = curMarkerTrace;
                }
                foreach (var it in items) AddToCatalog(it.Fr, it.Dec);
                perEp[ep.Id] = items; epMarkers[ep.Id] = markers; epUsed[ep.Id] = used;
                Res.EndpointTraces[ep.Id] = trace;
                foreach (var u in unresolved)
                    Res.Warnings.Add(new Warn { Category = "Llamada no resuelta", Message = u, EndpointDisplay = ep.Display });
                foreach (var w in warns.GroupBy(x => x.Category + "|" + x.Message + "|" + (x.Loc != null ? x.Loc.Key : "")).Select(g => g.First()))
                    Res.Warnings.Add(new Warn { Category = w.Category, Message = w.Message, Loc = w.Loc, EndpointDisplay = ep.Display });
            }
            Ix.TrackRefs = false;
            foreach (var ep in Res.Endpoints) BuildRows(ep, perEp[ep.Id], epMarkers[ep.Id], epUsed[ep.Id]);
            BuildOrphans(visitedAll);
        }

        // ------------------------------------------------------------ decision
        Dictionary<string, bool> GroupRegular(MethodAnalysis ma)
        {
            Dictionary<string, bool> d;
            if (groupRegularCache.TryGetValue(ma.M.Id, out d)) return d;
            d = new Dictionary<string, bool>();
            foreach (var fr in ma.Fragments)
            {
                string k = fr.Block != null ? fr.Block.Id : "-";
                bool v;
                d.TryGetValue(k, out v);
                d[k] = v || fr.Kind == "regular";
            }
            groupRegularCache[ma.M.Id] = d;
            return d;
        }

        public FragDecision Decide(Fragment fr, MethodAnalysis ma, List<Marker> inherited)
        {
            var dec = new FragDecision();
            var calls = fr.Calls;
            var levels = new List<KeyValuePair<string, List<Marker>>>();
            levels.Add(new KeyValuePair<string, List<Marker>>("sql-comment", fr.InlineMarkers));
            levels.Add(new KeyValuePair<string, List<Marker>>("const-comment", fr.ConstMarkers));
            levels.Add(new KeyValuePair<string, List<Marker>>("body-comment", fr.Block != null ? fr.Block.Markers : new List<Marker>()));
            levels.Add(new KeyValuePair<string, List<Marker>>("method", ma.MethodMarkers));
            levels.Add(new KeyValuePair<string, List<Marker>>("method-weak", ma.WeakMarkers));
            levels.Add(new KeyValuePair<string, List<Marker>>("inherited", inherited ?? new List<Marker>()));
            levels.Add(new KeyValuePair<string, List<Marker>>("class", ma.ClassMarkers));
            List<Marker> migr = null;
            foreach (var lv in levels)
            {
                if (lv.Value == null || lv.Value.Count == 0) continue;
                var docs = lv.Value.Where(mk => calls.Any(c => c.Sp.SameAs(mk.Sp))).ToList();
                dec.Used.AddRange(docs);
                var rest = new List<Marker>();
                foreach (var mk in lv.Value) if (!docs.Contains(mk) && !rest.Any(r => r.Sp.Key == mk.Sp.Key)) rest.Add(mk);
                if (rest.Count > 0) { migr = rest; dec.Level = lv.Key; break; }
            }
            string kind = fr.Kind;
            if (kind == "other")
            {
                bool gr;
                if (GroupRegular(ma).TryGetValue(fr.Block != null ? fr.Block.Id : "-", out gr) && gr) kind = "regular";
            }
            if (kind == "bare" || kind == "pure")
            {
                dec.Direct.AddRange(calls);
                if (migr != null && (dec.Level == "sql-comment" || dec.Level == "const-comment" || dec.Level == "body-comment")) dec.Unmatched.AddRange(migr);
                return dec;
            }
            if (kind == "regular")
            {
                if (migr != null)
                {
                    dec.Migrated.AddRange(migr);
                    dec.Used.AddRange(migr);
                    dec.Children.AddRange(calls);
                    dec.Inferred = dec.Level == "inherited" || dec.Level == "class";
                }
                else dec.DirectInQuery.AddRange(calls);
                return dec;
            }
            dec.Direct.AddRange(calls);
            return dec;
        }

        void AddToCatalog(Fragment fr, FragDecision dec)
        {
            foreach (var mk in dec.Migrated)
            {
                List<MigratedInfo> l;
                if (!Res.MigratedCatalog.TryGetValue(mk.Sp.Key, out l)) { l = new List<MigratedInfo>(); Res.MigratedCatalog[mk.Sp.Key] = l; }
                var qloc = QueryLoc(fr);
                var info = l.FirstOrDefault(x => x.QueryLoc.Key == qloc.Key);
                if (info == null) { info = new MigratedInfo { Sp = mk.Sp, MarkerLoc = mk.Loc, QueryLoc = qloc }; l.Add(info); }
                foreach (var c in dec.Children) if (!info.Children.Any(x => x.Sp.Key == c.Sp.Key && x.Loc.Key == c.Loc.Key)) info.Children.Add(c);
            }
        }

        public List<MigratedInfo> FindCatalog(SpName sp)
        {
            var r = new List<MigratedInfo>();
            foreach (var kv in Res.MigratedCatalog) foreach (var mi in kv.Value) if (mi.Sp.SameAs(sp)) r.Add(mi);
            return r;
        }

        static Location QueryLoc(Fragment fr)
        {
            if (fr.Pieces.Count > 0) return new Location(fr.Pieces[0].File, fr.Pieces[0].Line);
            return new Location(fr.File, fr.Line);
        }

        // ------------------------------------------------------------ recorrido
        void Dfs(Endpoint ep, MethodDecl m, TypeDecl thisType, List<Marker> inherited, string inhKey, int depth, List<string> path, bool inferredPath,
                 Dictionary<string, int> visited, List<RawItem> items, List<Marker> markers, HashSet<string> used, HashSet<string> visitedAll,
                 List<string> trace, HashSet<string> unresolved, List<Warn> warns)
        {
            if (m == null) return;
            if (depth > Opt.MaxDepth)
            {
                if (m.HasBody) warns.Add(new Warn { Category = "Profundidad máxima", Message = "Se alcanzó -MaxDepth (" + Opt.MaxDepth + ") al llegar a " + m.DisplayName + ": puede haber SP más profundos sin analizar (use un -MaxDepth mayor)", Loc = new Location(m.File, m.Line) });
                return;
            }
            string key = m.Id + "|" + inhKey + "|" + (thisType != null ? thisType.Id : "-");
            int prevDepth;
            // se vuelve a visitar si ahora se llega por un camino mas corto (el anterior pudo cortarse por -MaxDepth)
            if (visited.TryGetValue(key, out prevDepth) && prevDepth <= depth) return;
            bool firstVisit = !visited.ContainsKey(key);
            visited[key] = depth;
            visitedAll.Add(m.Id);
            var ma = Fx.Analyze(m);
            var p2 = new List<string>(path); p2.Add(m.DisplayName + (inferredPath ? "*" : ""));
            string traceStr = string.Join(" -> ", p2.ToArray());
            if (firstVisit) trace.Add(new string(' ', Math.Min(depth, 30) * 2) + m.DisplayName + " (" + m.File.Rel + ":" + m.Line + ")" + (inferredPath ? " [inferido]" : ""));
            markers.AddRange(ma.MethodMarkers);
            markers.AddRange(ma.WeakMarkers);
            foreach (var b in ma.Blocks) markers.AddRange(b.Markers);
            foreach (var mk in ma.MethodMarkers.Concat(ma.WeakMarkers).Concat(ma.Blocks.SelectMany(b => b.Markers))) if (!curMarkerTrace.ContainsKey(mk.Id)) curMarkerTrace[mk.Id] = traceStr;
            warns.AddRange(ma.Warnings);
            foreach (var fr in ma.Fragments)
            {
                if (fr.MessageContext) continue;
                var dec = Decide(fr, ma, inherited);
                markers.AddRange(fr.InlineMarkers); markers.AddRange(fr.ConstMarkers);
                foreach (var mk in fr.InlineMarkers.Concat(fr.ConstMarkers)) if (!curMarkerTrace.ContainsKey(mk.Id)) curMarkerTrace[mk.Id] = traceStr;
                foreach (var u in dec.Used) used.Add(u.Id);
                items.Add(new RawItem { Fr = fr, Dec = dec, Trace = traceStr, InferredPath = inferredPath });
                if (fr.HasDynamicSp)
                    warns.Add(new Warn { Category = "SP dinámico", Message = "Nombre de SP armado en tiempo de ejecución (no se puede resolver): " + U.OneLine(fr.Value, 120), Loc = new Location(fr.File, fr.Line) });
                if (dec.Level == "inherited" && dec.Migrated.Count > 1)
                    warns.Add(new Warn { Category = "Asociación ambigua", Message = "La query recibe varios SP desde comentarios de un método llamador: " + string.Join(", ", dec.Migrated.Select(x => x.Sp.Display).ToArray()), Loc = QueryLoc(fr) });
            }
            List<Marker> nextInh = ma.MethodMarkers.Count > 0 ? ma.MethodMarkers : inherited;
            string nextKey = ma.MethodMarkers.Count > 0 ? m.Id : inhKey;
            var targets = new List<KeyValuePair<Target, List<Marker>>>();
            foreach (var cs in Ix.Calls(m))
            {
                if (cs.IsNew) continue;
                bool inf, unr;
                var tg = Ix.ResolveTargets(m, cs, thisType, out inf, out unr);
                if (unr && tg.Count == 0)
                {
                    // solo se advierte si alguno de los posibles destinos puede llegar a un SP
                    List<MethodDecl> cands;
                    if (Ix.MethodsByName.TryGetValue(cs.Name, out cands))
                    {
                        var risky = Ix.FilterArgc(cands.Where(x => !x.IsExtension && !x.IsLocalFunction).ToList(), cs.Argc, false).Where(x => CanReachSp(x, new HashSet<string>())).ToList();
                        if (risky.Count > 0)
                        {
                            string recv = string.Join(".", cs.Receiver.Select(x => x.Name).ToArray());
                            unresolved.Add((recv.Length > 0 ? recv + "." : "") + cs.Name + "() en " + m.DisplayName + " (" + m.File.Rel + ":" + cs.Line + "). Posibles destinos con SP: "
                                + string.Join(", ", risky.Take(6).Select(x => x.DisplayName).ToArray()) + (risky.Count > 6 ? " y " + (risky.Count - 6) + " más" : ""));
                        }
                    }
                }
                // documentacion del metodo de la interfaz por la que se resolvio la llamada (/// Migrado de ...)
                var ifaceMarkers = new List<Marker>();
                foreach (var x in tg)
                    if (!x.M.HasBody && x.M.Owner != null && (x.M.Owner.Kind == "interface" || x.M.IsAbstract))
                        foreach (var mk in Fx.Analyze(x.M).MethodMarkers) if (!ifaceMarkers.Any(y => y.Sp.Key == mk.Sp.Key)) ifaceMarkers.Add(mk);
                markers.AddRange(ifaceMarkers);
                foreach (var mk in ifaceMarkers) if (!curMarkerTrace.ContainsKey(mk.Id)) curMarkerTrace[mk.Id] = traceStr;
                foreach (var x in tg)
                {
                    if (x.M == m) continue;
                    x.Inferred |= inf;
                    targets.Add(new KeyValuePair<Target, List<Marker>>(x, ifaceMarkers.Count > 0 ? ifaceMarkers : null));
                }
            }
            foreach (var h in Ix.LinkedHandlers(m)) targets.Add(new KeyValuePair<Target, List<Marker>>(h, null));
            foreach (var kv in targets)
            {
                var tgt = kv.Key;
                if (!tgt.M.HasBody) continue;
                // tipo concreto del objeto en el destino (para resolver llamadas virtuales sin receptor)
                TypeDecl nextThis = null;
                if (tgt.Via != null && tgt.Via.Kind != "interface" && tgt.M.Owner != null && Ix.IsSubtypeOf(tgt.Via, tgt.M.Owner)) nextThis = tgt.Via;
                else if (tgt.M.Owner != null && tgt.M.Owner.Kind != "interface") nextThis = tgt.M.Owner;
                if (tgt.M.IsLocalFunction) nextThis = thisType;
                else if (tgt.M.IsStatic) nextThis = null;
                var inh = kv.Value ?? nextInh;
                string ik = kv.Value != null ? "if:" + string.Join(",", kv.Value.Select(x => x.Id).ToArray()) : nextKey;
                Dfs(ep, tgt.M, nextThis, inh, ik, depth + 1, p2, inferredPath || tgt.Inferred, visited, items, markers, used, visitedAll, trace, unresolved, warns);
            }
        }

        Dictionary<string, bool> reachMemo = new Dictionary<string, bool>();

        // true si desde el metodo se puede llegar a SQL o a menciones de SP
        bool CanReachSp(MethodDecl m, HashSet<string> stack)
        {
            bool r;
            if (reachMemo.TryGetValue(m.Id, out r)) return r;
            if (!m.HasBody || stack.Count > 60 || !stack.Add(m.Id)) return false;
            var ma = Fx.Analyze(m);
            r = ma.Fragments.Any(f => !f.MessageContext) || ma.MethodMarkers.Count > 0 || ma.Blocks.Count > 0;
            if (!r)
            {
                foreach (var cs in Ix.Calls(m))
                {
                    if (cs.IsNew) continue;
                    bool inf, unr;
                    foreach (var t in Ix.ResolveCall(m, cs, out inf, out unr))
                        if (t != m && CanReachSp(t, stack)) { r = true; break; }
                    if (r) break;
                }
            }
            if (!r) foreach (var h in Ix.LinkedHandlers(m)) if (CanReachSp(h.M, stack)) { r = true; break; }
            stack.Remove(m.Id);
            reachMemo[m.Id] = r;
            return r;
        }

        // ------------------------------------------------------------ filas
        Dictionary<string, string> curMarkerTrace = new Dictionary<string, string>();
        Dictionary<string, Dictionary<string, string>> epMarkerTrace = new Dictionary<string, Dictionary<string, string>>();

        void BuildRows(Endpoint ep, List<RawItem> items, List<Marker> markers, HashSet<string> used)
        {
            var rows = new List<ResultRow>();
            var keys = new HashSet<string>();
            Action<ResultRow> add = r =>
            {
                string k = r.Tipo + "|" + (r.Sp != null ? r.Sp.Key : "") + "|" + (r.ChildChain ?? "") + "|" + (r.Loc != null ? r.Loc.Key : "");
                if (keys.Add(k)) rows.Add(r);
            };
            var migrAgg = new Dictionary<string, List<KeyValuePair<RawItem, Marker>>>();
            foreach (var it in items)
            {
                var fr = it.Fr; var dec = it.Dec;
                foreach (var h in dec.Direct)
                {
                    var r = new ResultRow { Ep = ep, Sp = h.Sp, Tipo = "DIRECTO", Loc = h.Loc, Trace = it.Trace, Inferred = it.InferredPath, Origin = fr.Origin };
                    if (fr.OnlyConstRef && fr.Kind == "bare" && (fr.Origin == "const" || fr.Origin == "config" || fr.Origin == "resx"))
                    {
                        if (fr.Origin == "const" && fr.ConstRef != null)
                            r.Detail = "constante " + (fr.ConstRef.Owner != null ? fr.ConstRef.Owner.Name + "." : "") + fr.ConstRef.Name + " definida en " + fr.ConstRef.File.Rel + ":" + fr.ConstRef.Line;
                        else
                            r.Detail = (fr.Origin == "config" ? "valor de configuración en " : fr.Origin == "resx" ? "recurso en " : "constante definida en ") + h.Loc.Key;
                        r.Loc = new Location(fr.File, fr.Line);
                    }
                    else if (fr.Origin == "sqlfile") r.Detail = "archivo .sql usado en " + fr.File.Rel + ":" + fr.Line;
                    else if (fr.Kind == "bare" && (h.Loc.File != fr.File || h.Loc.Line < fr.Line || h.Loc.Line > fr.EndLine))
                    {
                        // nombre armado con constantes (Concat/Format/interpolacion): la ubicacion es el uso
                        r.Loc = new Location(fr.File, fr.Line);
                        r.Detail = fr.ConstRef != null
                            ? "constante " + (fr.ConstRef.Owner != null ? fr.ConstRef.Owner.Name + "." : "") + fr.ConstRef.Name + " definida en " + fr.ConstRef.File.Rel + ":" + fr.ConstRef.Line
                            : "nombre armado con constantes (" + h.Loc.Key + ")";
                    }
                    else if (h.Loc.File != fr.File || fr.Pieces.Any(pc => pc.Const != null)) r.Detail = "usado en " + fr.File.Rel + ":" + fr.Line;
                    r.Form = FormText(fr);
                    AddLink(r, h.Sp); add(r);
                }
                foreach (var h in dec.DirectInQuery)
                {
                    var r = new ResultRow { Ep = ep, Sp = h.Sp, Tipo = "DIRECTO_EN_QUERY", Loc = h.Loc, Trace = it.Trace, Inferred = it.InferredPath, Origin = fr.Origin, QueryLoc = QueryLoc(fr), Form = "dentro de query" };
                    if (h.Loc.File != fr.File || fr.Pieces.Any(pc => pc.Const != null)) r.Detail = "query usada en " + fr.File.Rel + ":" + fr.Line;
                    AddLink(r, h.Sp); add(r);
                }
                foreach (var mk in dec.Migrated)
                {
                    List<KeyValuePair<RawItem, Marker>> l;
                    if (!migrAgg.TryGetValue(mk.Sp.Key, out l)) { l = new List<KeyValuePair<RawItem, Marker>>(); migrAgg[mk.Sp.Key] = l; }
                    l.Add(new KeyValuePair<RawItem, Marker>(it, mk));
                }
            }
            foreach (var kv in migrAgg)
            {
                var first = kv.Value[0];
                var mk = first.Value;
                bool inferred = kv.Value.Any(x => x.Key.Dec.Inferred || x.Key.InferredPath);
                var children = new List<KeyValuePair<SpHit, RawItem>>();
                foreach (var x in kv.Value)
                    foreach (var c in x.Key.Dec.Children)
                        if (!children.Any(y => y.Key.Sp.Key == c.Sp.Key && y.Key.Loc.Key == c.Loc.Key)) children.Add(new KeyValuePair<SpHit, RawItem>(c, x.Key));
                if (children.Count == 0)
                {
                    add(new ResultRow { Ep = ep, Sp = mk.Sp, Tipo = "MIGRADO_LISTO", Loc = mk.Loc, QueryLoc = QueryLoc(first.Key.Fr), Trace = first.Key.Trace, Inferred = inferred, Detail = MarkerDetail(mk) });
                    continue;
                }
                foreach (var ch in children)
                {
                    var c = ch.Key;
                    var cat = FindCatalog(c.Sp).Where(ci => !ci.Sp.SameAs(mk.Sp)).ToList();
                    var r = new ResultRow
                    {
                        Ep = ep, Sp = mk.Sp, Child = c.Sp, ChildChain = c.Sp.Display, Level = 1, Tipo = "HIJO", Loc = c.Loc, QueryLoc = QueryLoc(ch.Value.Fr),
                        Trace = ch.Value.Trace, Inferred = inferred, ChildStatus = cat.Count > 0 ? "MIGRADO_EN_REPO" : "PENDIENTE", Detail = "migrado según " + MarkerDetail(mk)
                    };
                    AddLink(r, c.Sp); add(r);
                    var seen = new HashSet<string> { mk.Sp.Key, c.Sp.Key };
                    ExpandNested(ep, mk, c.Sp, c.Sp.Display, 2, seen, ch.Value.Trace, inferred, add);
                }
            }
            // marcadores sin evidencia de codigo
            var reported = rows.SelectMany(r => new SpName[] { r.Sp, r.Child }).Where(x => x != null).ToList();
            var soloSeen = new HashSet<string>();
            foreach (var mk in markers)
            {
                if (used.Contains(mk.Id)) continue;
                if (reported.Any(x => x.SameAs(mk.Sp))) continue;
                if (!soloSeen.Add(mk.Sp.Key)) continue;
                Dictionary<string, string> mt;
                string mtr = "";
                if (epMarkerTrace.TryGetValue(ep.Id, out mt)) mt.TryGetValue(mk.Id, out mtr);
                add(new ResultRow { Ep = ep, Sp = mk.Sp, Tipo = "SOLO_COMENTARIO", Loc = mk.Loc, Detail = MarkerDetail(mk), Trace = mtr ?? "" });
            }
            Res.Rows.AddRange(rows);
            if (rows.Count == 0)
            {
                int q = items.Count;
                bool dyn = Res.Warnings.Any(w => w.EndpointDisplay == ep.Display && (w.Category == "SP dinámico" || w.Category == "Comando no resuelto" || w.Category == "Llamada no resuelta" || w.Category == "Configuración ambigua"));
                Res.EndpointNoSpReason[ep.Id] = ep.Handler == null ? "Handler no resuelto" : dyn ? "No se pudo determinar el SP: ver advertencias (sección 7.2)" : q > 0 ? "Ejecuta " + q + " consulta(s) SQL sin SP asociado" : "No se detectó acceso a datos ni SP en el flujo";
            }
        }

        static string FormText(Fragment fr)
        {
            switch (fr.Form)
            {
                case "bare": return fr.SpContext ? "nombre del SP + CommandType.StoredProcedure" : "nombre del SP como comando";
                case "call": return "CALL / EXEC";
                case "block": return "bloque BEGIN ... END";
                case "dual": return "SELECT ... FROM DUAL";
                case "query": return "dentro de query";
                default: return "texto SQL";
            }
        }

        static void AddLink(ResultRow r, SpName sp)
        {
            if (sp == null || string.IsNullOrEmpty(sp.DbLink)) return;
            r.Detail = (r.Detail != null ? r.Detail + "; " : "") + "vía db link @" + sp.DbLink.ToUpperInvariant();
        }

        static string MarkerDetail(Marker mk)
        {
            string src = mk.Source == "sql-comment" ? "comentario SQL" : mk.Source == "const-comment" ? "comentario de la constante"
                : mk.Source == "body-comment" ? "comentario en el método" : mk.Source == "method-comment" ? "comentario/XML doc del método"
                : mk.Source == "attribute" ? "atributo del método" : mk.Source == "log" ? "mensaje de log" : mk.Source == "class-comment" ? "comentario de la clase"
                : mk.Source == "endpoint-comment" ? "comentario del endpoint" : mk.Source;
            return src + " (" + mk.Loc.Key + ")";
        }

        void ExpandNested(Endpoint ep, Marker root, SpName child, string chain, int level, HashSet<string> seen, string trace, bool inferred, Action<ResultRow> add)
        {
            if (level > Opt.MaxNestedLevels) return;
            foreach (var mi in FindCatalog(child))
            {
                foreach (var gc in mi.Children)
                {
                    if (seen.Contains(gc.Sp.Key)) continue;
                    string ch2 = chain + " -> " + gc.Sp.Display;
                    var cat = FindCatalog(gc.Sp);
                    add(new ResultRow
                    {
                        Ep = ep, Sp = root.Sp, Child = gc.Sp, ChildChain = ch2, Level = level, Tipo = "HIJO", Loc = gc.Loc, QueryLoc = mi.QueryLoc, Trace = trace, Inferred = inferred,
                        ChildStatus = cat.Count > 0 ? "MIGRADO_EN_REPO" : "PENDIENTE", Detail = child.Display + " está migrado en " + mi.MarkerLoc.Key
                    });
                    var s2 = new HashSet<string>(seen); s2.Add(gc.Sp.Key);
                    ExpandNested(ep, root, gc.Sp, ch2, level + 1, s2, trace, inferred, add);
                }
            }
        }

        // ------------------------------------------------------------ huerfanos
        void BuildOrphans(HashSet<string> visitedAll)
        {
            var reportedLocs = new HashSet<string>(Res.Rows.Where(r => r.Loc != null).Select(r => r.Loc.Key));
            foreach (var m in Ix.Methods)
            {
                if (visitedAll.Contains(m.Id)) continue;
                var ma = Fx.Analyze(m);
                foreach (var fr in ma.Fragments)
                {
                    if (fr.MessageContext) continue;
                    var dec = Decide(fr, ma, null);
                    foreach (var h in dec.Direct.Concat(dec.DirectInQuery))
                        Res.Orphans.Add(new OrphanRef { Sp = h.Sp, Loc = h.Loc, Context = m.DisplayName, Kind = "codigo", Detail = dec.DirectInQuery.Contains(h) ? "llamado dentro de query" : "llamado directamente" });
                    foreach (var mk in dec.Migrated)
                        Res.Orphans.Add(new OrphanRef { Sp = mk.Sp, Loc = mk.Loc, Context = m.DisplayName, Kind = "codigo", Detail = dec.Children.Count == 0 ? "query migrada (sin hijos)" : "query migrada con hijos: " + string.Join(", ", dec.Children.Select(c => c.Sp.Display).Distinct().ToArray()) });
                    foreach (var c in dec.Children)
                        Res.Orphans.Add(new OrphanRef { Sp = c.Sp, Loc = c.Loc, Context = m.DisplayName, Kind = "codigo", Detail = "hijo de query migrada" });
                }
                var mkAll = new List<Marker>(ma.MethodMarkers);
                foreach (var b in ma.Blocks) if (b.Scoped) mkAll.AddRange(b.Markers);
                foreach (var mk in mkAll)
                    if (!Res.Orphans.Any(o => o.Sp.SameAs(mk.Sp) && o.Context == m.DisplayName))
                        Res.Orphans.Add(new OrphanRef { Sp = mk.Sp, Loc = mk.Loc, Context = m.DisplayName, Kind = "comentario", Detail = "mención en comentario de código no alcanzable" });
            }
            // constantes con nombres de SP nunca usadas
            foreach (var td in Ix.Types)
                foreach (var mv in td.Members.Values)
                {
                    if (Ix.ReferencedConsts.Contains(mv.Id)) continue;
                    string v = Ix.ConstValue(mv);
                    if (v == null) continue;
                    var scan = SqlScan.Scan(v);
                    foreach (var h in Sm.FindInCode(scan.Blank, scan.Kind == "bare", scan.Kind == "bare" || scan.Kind == "pure"))
                        Res.Orphans.Add(new OrphanRef { Sp = h.Sp, Loc = new Location(mv.File, mv.Line), Context = td.Name + "." + mv.Name, Kind = "codigo", Detail = "constante no referenciada" });
                }
            // menciones en comentarios de SP que ya aparecen en algun endpoint (p.ej. el /// de una interfaz) no son huerfanas
            var reportedKeys = new HashSet<string>(Res.Rows.SelectMany(r => new SpName[] { r.Sp, r.Child }).Where(x => x != null).Select(x => x.Key));
            Res.Orphans = Res.Orphans.Where(o => o.Kind != "comentario" || !(reportedLocs.Contains(o.Loc.Key) || reportedKeys.Contains(o.Sp.Key))).ToList();
            Res.Orphans = Res.Orphans.Where(o => !reportedLocs.Contains(o.Loc.Key) || o.Kind == "comentario")
                .GroupBy(o => o.Sp.Key + "|" + o.Loc.Key + "|" + o.Kind).Select(g => g.First()).ToList();
        }

        // ------------------------------------------------------------ recursos
        void LoadResx(string path)
        {
            var sf = LoadText(path);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (Regex.IsMatch(name, @"\.[a-z]{2}(-[A-Za-z]{2,4})?$")) return; // recurso localizado
            Ix.ResxBases.Add(name);
            foreach (Match mm in Regex.Matches(sf.Text, @"<data\s+name=""(?<k>[^""]+)""[^>]*>\s*<value>(?<v>[\s\S]*?)</value>(?:\s*<comment>(?<c>[\s\S]*?)</comment>)?"))
            {
                string v = System.Net.WebUtility.HtmlDecode(mm.Groups["v"].Value);
                var e = new ResxEntry { FileBase = name, Key = mm.Groups["k"].Value, Value = v, File = sf, Line = sf.LineOf(mm.Groups["v"].Index) };
                if (mm.Groups["c"].Success) { e.Comment = System.Net.WebUtility.HtmlDecode(mm.Groups["c"].Value); e.CommentLine = sf.LineOf(mm.Groups["c"].Index); }
                List<ResxEntry> l;
                if (!Ix.ResxByKey.TryGetValue(e.Key, out l)) { l = new List<ResxEntry>(); Ix.ResxByKey[e.Key] = l; }
                l.Add(e);
            }
        }

        void LoadConfig(string path)
        {
            var sf = LoadText(path);
            var j = new MiniJson(sf.Text);
            j.Walk((p, v, off) =>
            {
                string leaf = p.Contains(":") ? p.Substring(p.LastIndexOf(':') + 1) : p;
                var e = new ConfigEntry { Path = p, Leaf = leaf, Value = v, File = sf, Line = sf.LineOf(off) };
                if (!Ix.ConfigByPath.ContainsKey(p)) Ix.ConfigByPath[p] = e;
                List<ConfigEntry> l;
                if (!Ix.ConfigByLeaf.TryGetValue(leaf, out l)) { l = new List<ConfigEntry>(); Ix.ConfigByLeaf[leaf] = l; }
                l.Add(e);
            });
        }
    }

    // JSON minimo (solo extrae valores string con su ruta "A:B:C")
    public class MiniJson
    {
        string s; int i;
        public MiniJson(string text) { s = text; i = 0; }
        public void Walk(Action<string, string, int> onString)
        {
            try { SkipWs(); Value("", onString); } catch { }
        }
        void SkipWs()
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c) || c == '﻿') { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*') { int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? s.Length : e + 2; continue; }
                break;
            }
        }
        void Value(string path, Action<string, string, int> cb)
        {
            SkipWs();
            if (i >= s.Length) return;
            char c = s[i];
            if (c == '{')
            {
                i++;
                while (true)
                {
                    SkipWs();
                    if (i >= s.Length) return;
                    if (s[i] == '}') { i++; return; }
                    if (s[i] == ',') { i++; continue; }
                    string k = Str();
                    SkipWs();
                    if (i < s.Length && s[i] == ':') i++;
                    Value(path.Length > 0 ? path + ":" + k : k, cb);
                }
            }
            if (c == '[')
            {
                i++; int idx = 0;
                while (true)
                {
                    SkipWs();
                    if (i >= s.Length) return;
                    if (s[i] == ']') { i++; return; }
                    if (s[i] == ',') { i++; continue; }
                    Value(path + ":" + idx, cb); idx++;
                }
            }
            if (c == '"') { int off = i; string v = Str(); cb(path, v, off); return; }
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']') i++;
        }
        string Str()
        {
            var sb = new StringBuilder();
            if (i >= s.Length || s[i] != '"') { while (i < s.Length && s[i] != ':' && s[i] != ',' && s[i] != '}') sb.Append(s[i++]); return sb.ToString().Trim(); }
            i++;
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char d = s[i + 1];
                    if (d == 'n') sb.Append('\n'); else if (d == 't') sb.Append('\t'); else if (d == 'u' && i + 5 < s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i + 2, 4), 16)); i += 4; } else sb.Append(d);
                    i += 2; continue;
                }
                sb.Append(s[i++]);
            }
            i++;
            return sb.ToString();
        }
    }
}
'@

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

function Esc([string]$s) {
    if ($null -eq $s) { return '' }
    return ($s -replace '\r?\n', ' ' -replace '\|', '\|')
}
function Code([string]$s) { if ([string]::IsNullOrEmpty($s)) { return '' } return '`' + (Esc $s) + '`' }
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
