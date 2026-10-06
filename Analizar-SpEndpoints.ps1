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
$excludedDirNames = @('bin', 'obj', '.git', '.vs', '.vscode', '.idea', 'node_modules', 'packages', 'TestResults', 'artifacts',
    '.github', '.gitlab', '.azuredevops', 'docs', 'doc', 'documentation', 'documentacion', "documentaci$([char]0x00F3)n", '.claude', '.config')

function Test-Excluded([string]$relPath) {
    foreach ($p in $ExcludePath) {
        if ([string]::IsNullOrWhiteSpace($p)) { continue }
        $pp = $p.Replace('\', '/')
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

$stack = New-Object System.Collections.Generic.Stack[string]
$visitedDirs = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$stack.Push($RepoPath)
while ($stack.Count -gt 0) {
    $dir = $stack.Pop()
    # OneDrive marca las carpetas como reparse points: no se saltan, pero se evita recorrer dos veces
    # la misma carpeta y los ciclos de enlaces con un limite de profundidad
    if (-not $visitedDirs.Add($dir)) { continue }
    if (($dir.Length - $RepoPath.Length) -gt 2000 -or ($dir.Split('\').Count - $RepoPath.Split('\').Count) -gt 60) { continue }
    try {
        foreach ($d in [System.IO.Directory]::EnumerateDirectories($dir)) {
            $name = [System.IO.Path]::GetFileName($d)
            $rel = Get-RelPath $d
            if ($excludedDirNames -contains $name -or (Test-Excluded $rel)) { $skippedDirs.Add($rel); continue }
            $stack.Push($d)
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
                '.sql' { $sqlFiles.Add($f) }
                '.resx' { $resxFiles.Add($f) }
                '.json' { if ($fn -like 'appsettings*.json') { $configFiles.Add($f) } }
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
            $dir = [System.IO.Path]::GetDirectoryName($f)
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

    public class AttrInfo
    {
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
        public bool IsAbstract, IsStatic, IsPartial;
        public TypeDecl Outer;
        public List<MethodDecl> Methods = new List<MethodDecl>();
        public Dictionary<string, MemberVar> Members = new Dictionary<string, MemberVar>(StringComparer.Ordinal);
        public List<ParamInfo> PrimaryCtor;
        public List<Comment> Leading = new List<Comment>();
        public List<SourceFile> Files = new List<SourceFile>();
        public List<string> Usings = new List<string>();
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
        public bool IsStatic, IsPublic, IsAbstract, IsVirtual, IsOverride, IsCtor, IsExtension, IsSynthetic;
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

        public Location LocOfValueOffset(int off)
        {
            FragPiece best = null;
            foreach (var p in Pieces) { if (off >= p.ValueStart && off <= p.ValueStart + p.ValueLength) { best = p; break; } }
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
            if ((t.Name == "Task" || t.Name == "ValueTask" || t.Name == "Nullable" || t.Name == "Lazy") && t.Args.Count == 1) return UnwrapRef(t.Args[0]);
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
            foreach (var t in toks) { t.Line = f.LineOf(t.Start); t.EndLine = f.LineOf(Math.Max(t.Start, t.End - 1)); }
            ComputeMatch(f);
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
        public static bool IsIdPart(char c) { return char.IsLetterOrDigit(c) || c == '_'; }

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
                            RecordUsing(k, semi);
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

        void RecordUsing(int k, int semi)
        {
            bool isStatic = false;
            if (IsId(k, "static")) { isStatic = true; k++; }
            var sb = new StringBuilder();
            for (int j = k; j < semi; j++)
            {
                if (IsP(j, "=")) return; // alias
                if (IsP(j, "<")) break;
                sb.Append(t[j].Text);
            }
            string u = sb.ToString().Replace("global::", "");
            if (u.Length > 0) f.Usings.Add(isStatic ? "static:" + u : u);
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
                Attrs = new List<AttrInfo>(attrs), IsAbstract = mods.Contains("abstract"), IsStatic = mods.Contains("static"), IsPartial = mods.Contains("partial")
            };
            td.Id = "T" + (++seq);
            td.FullName = (outer != null ? outer.FullName + "." : (ns.Length > 0 ? ns + "." : "")) + td.Name;
            td.Leading = CommentsBefore(declStart, k);
            td.Usings = f.Usings;
            td.Files.Add(f);
            k++;
            if (IsP(k, "<")) { int g = SkipGeneric(k); if (g > 0) k = g + 1; }
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
            if (IsP(k, "~")) return SkipUnknown(i, e);
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
                        if (t[k].Text == "base") for (int q = k + 2; q < M(k + 1); q++) if (t[q].Kind == TokKind.Str) td.BaseCtorStrings.Add(t[q].Lit.PlainValue());
                        k = M(k + 1) + 1;
                    }
                }
                ParseBody(ref k, md);
                Register(md, td);
                return k;
            }
            if (x.Kind == TokKind.Ident && (x.Text == "event" || x.Text == "implicit" || x.Text == "explicit" || x.Text == "delegate"))
                return SkipUnknown(i, e);
            if (x.Kind != TokKind.Ident && !IsP(k, "(")) return SkipUnknown(i, e);

            TypeRef type = ParseTypeRef(ref k);
            if (type == null) return SkipUnknown(i, e);
            if (IsId(k, "operator")) return SkipUnknown(i, e);
            if (IsId(k, "this") && IsP(k + 1, "[")) return SkipUnknown(i, e);
            if (!IsIdent(k)) return SkipUnknown(i, e);
            string name = t[k].Text;
            int nameTok = k;
            k++;
            int guard = 0;
            while (guard++ < 20)
            {
                if (IsP(k, "<"))
                {
                    int g = SkipGeneric(k);
                    if (g > 0 && IsP(g + 1, ".") && IsIdent(g + 2)) { name = t[g + 2].Text; nameTok = g + 2; k = g + 3; continue; }
                    break;
                }
                if (IsP(k, ".") && IsIdent(k + 1)) { name = t[k + 1].Text; nameTok = k + 1; k += 2; continue; }
                break;
            }
            if (IsP(k, "<")) { int g = SkipGeneric(k); if (g > 0) k = g + 1; }
            if (IsP(k, "("))
            {
                int c = M(k);
                if (c < 0) return SkipUnknown(i, e);
                var md = NewMethod(name, td, nameTok, declStart, attrs, mods);
                md.ReturnType = type;
                md.Params = ParseParams(k, c);
                md.IsExtension = md.Params.Count > 0 && md.Params[0].IsThis;
                k = c + 1;
                while (k < t.Count && !IsP(k, "{") && !IsP(k, "=>") && !IsP(k, ";"))
                {
                    if (IsP(k, "(") && M(k) > k) k = M(k) + 1; else k++;
                }
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
                var a = new AttrInfo { Line = t[k].Line, StartOffset = t[k].Start };
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
                        if (key != null) a.Named[key] = val;
                        else { a.Positional.Add(val); a.PositionalIsString.Add(isStr); }
                        as0 = ae + 1;
                    }
                    k = c + 1;
                }
                list.Add(a);
                if (IsP(k, ",")) k++;
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
    }

    public class LocalInfo
    {
        public TypeRef Type;
        public int ExprTok = -1;
        public int ForeachTok = -1;
        public bool Resolving;
        public List<TypeDecl> Cache;
        public bool Known;    // tipo conocido (aunque sea externo)
    }

    public class TypeSet
    {
        public List<TypeDecl> Types = new List<TypeDecl>();
        public bool Known;     // se conoce el tipo (aunque no este en el repo)
        public bool Static;    // acceso estatico por nombre de tipo
        public static TypeSet Unknown() { return new TypeSet(); }
    }

    public class ResxEntry { public string FileBase, Key, Value; public SourceFile File; public int Line; }
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
        public Dictionary<string, List<TypeDecl>> Implementors = new Dictionary<string, List<TypeDecl>>(StringComparer.Ordinal);
        public Dictionary<string, List<TypeDecl>> AncestorCache = new Dictionary<string, List<TypeDecl>>();
        public Dictionary<string, HashSet<string>> AncestorNames = new Dictionary<string, HashSet<string>>();
        public Dictionary<string, List<MethodDecl>> RequestHandlers = new Dictionary<string, List<MethodDecl>>(StringComparer.Ordinal);
        public Dictionary<string, List<SourceFile>> SqlByName = new Dictionary<string, List<SourceFile>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<ResxEntry>> ResxByKey = new Dictionary<string, List<ResxEntry>>(StringComparer.Ordinal);
        public HashSet<string> ResxBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ConfigEntry> ConfigByPath = new Dictionary<string, ConfigEntry>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<ConfigEntry>> ConfigByLeaf = new Dictionary<string, List<ConfigEntry>>(StringComparer.Ordinal);
        public Dictionary<SourceFile, Parser> Parsers = new Dictionary<SourceFile, Parser>();
        Dictionary<string, Dictionary<string, LocalInfo>> localsCache = new Dictionary<string, Dictionary<string, LocalInfo>>();
        Dictionary<string, List<CallSite>> callsCache = new Dictionary<string, List<CallSite>>();
        Dictionary<string, string> constCache = new Dictionary<string, string>();
        HashSet<string> constResolving = new HashSet<string>();

        static readonly HashSet<string> HandlerMethodNames = new HashSet<string>(new string[] {
            "Handle","HandleAsync","Consume","ConsumeAsync","Execute","ExecuteAsync","Process","ProcessAsync","Run","RunAsync","Invoke","InvokeAsync" });

        public Parser P(SourceFile f)
        {
            Parser p;
            if (!Parsers.TryGetValue(f, out p)) { p = new Parser(f); Parsers[f] = p; }
            return p;
        }

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
                    prim.BaseCtorStrings.AddRange(td.BaseCtorStrings);
                    prim.IsAbstract |= td.IsAbstract; prim.IsStatic |= td.IsStatic;
                    foreach (var u in td.Usings) if (!prim.Usings.Contains(u)) prim.Usings = new List<string>(prim.Usings.Concat(new string[] { u }));
                }
                else if (!byFull.ContainsKey(td.FullName)) { byFull[td.FullName] = td; Types.Add(td); }
                else { Types.Add(td); }
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
            // ancestros e implementadores
            foreach (var td in Types)
            {
                var names = GetAncestorNames(td);
                foreach (var n in names)
                {
                    List<TypeDecl> l;
                    if (!Implementors.TryGetValue(n, out l)) { l = new List<TypeDecl>(); Implementors[n] = l; }
                    if (!l.Contains(td)) l.Add(td);
                }
            }
            // handlers tipo MediatR / ICommandHandler<T> / IConsumer<T>
            foreach (var td in Types)
            {
                foreach (var b in AllBaseRefs(td))
                {
                    if (b.Args.Count == 0) continue;
                    if (!(b.Name.EndsWith("Handler") || b.Name.EndsWith("Consumer") || b.Name == "IHandleMessages" || b.Name.EndsWith("Handler`"))) continue;
                    string req = b.Args[0].Name;
                    if (string.IsNullOrEmpty(req) || req.Length < 2) continue;
                    if (TypesByName.ContainsKey(req) == false) continue;
                    var hm = new List<MethodDecl>();
                    foreach (var a in new TypeDecl[] { td }.Concat(Ancestors(td)))
                        foreach (var md in a.Methods) if (HandlerMethodNames.Contains(md.Name) && md.HasBody && !hm.Contains(md)) hm.Add(md);
                    if (hm.Count == 0) continue;
                    List<MethodDecl> l;
                    if (!RequestHandlers.TryGetValue(req, out l)) { l = new List<MethodDecl>(); RequestHandlers[req] = l; }
                    foreach (var x in hm) if (!l.Contains(x)) l.Add(x);
                }
            }
            foreach (var sf in SqlFiles)
            {
                string name = System.IO.Path.GetFileName(sf.Path);
                List<SourceFile> l;
                if (!SqlByName.TryGetValue(name, out l)) { l = new List<SourceFile>(); SqlByName[name] = l; }
                l.Add(sf);
            }
        }

        // TypeRefs de todas las bases (directas e indirectas)
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

        // ------------------------------------------------------------ tipos
        public List<TypeDecl> ResolveTypeName(string name, TypeDecl ctx)
        {
            List<TypeDecl> c;
            if (name == null || !TypesByName.TryGetValue(name, out c)) return new List<TypeDecl>();
            if (c.Count == 1 || ctx == null) return c;
            var pref = new List<TypeDecl>();
            foreach (var x in c)
            {
                if (x == ctx) { pref.Add(x); continue; }
                string ns = x.Namespace ?? "";
                string cns = ctx.Namespace ?? "";
                bool ok = ns == cns || (cns.StartsWith(ns + ".") && ns.Length > 0) || ctx.Usings.Contains(ns)
                          || x.FullName.StartsWith(ctx.FullName + ".") || (x.Outer != null && ctx.FullName.StartsWith(x.Outer.FullName));
                if (ok) pref.Add(x);
            }
            return pref.Count > 0 ? pref : c;
        }

        public List<TypeDecl> ResolveTypeRef(TypeRef tr, TypeDecl ctx)
        {
            if (tr == null) return new List<TypeDecl>();
            var c = ResolveTypeName(tr.Name, ctx);
            if (c.Count > 1 && tr.Qualified != null && tr.Qualified.Contains("."))
            {
                var q = c.Where(x => x.FullName.EndsWith(tr.Qualified)).ToList();
                if (q.Count > 0) return q;
            }
            return c;
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

        IEnumerable<TypeDecl> SelfAndOuters(TypeDecl td)
        {
            var x = td;
            while (x != null) { yield return x.MergedInto ?? x; x = x.Outer; }
        }

        public List<MethodDecl> MethodsNamed(TypeDecl td, string name, bool withAncestors)
        {
            var r = new List<MethodDecl>();
            foreach (var md in td.Methods) if (md.Name == name) r.Add(md);
            if (withAncestors) foreach (var a in Ancestors(td)) foreach (var md in a.Methods) if (md.Name == name && !r.Contains(md)) r.Add(md);
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

        public List<MethodDecl> FindMethods(List<TypeDecl> types, string name, int argc)
        {
            var r = new List<MethodDecl>();
            foreach (var td in types)
            {
                var direct = MethodsNamed(td, name, true);
                foreach (var md in direct) if (!r.Contains(md)) r.Add(md);
                bool poly = td.Kind == "interface" || td.IsAbstract || direct.Any(x => x.IsAbstract || x.IsVirtual || !x.HasBody);
                if (poly)
                {
                    List<TypeDecl> impls;
                    if (Implementors.TryGetValue(td.Name, out impls))
                        foreach (var it in impls)
                        {
                            if (it == td) continue;
                            foreach (var md in MethodsNamed(it, name, true)) if (!r.Contains(md)) r.Add(md);
                        }
                }
            }
            r = FilterArgc(r, argc, false);
            return r;
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
                    if (!d.ContainsKey(t[k + 1].Text)) d[t[k + 1].Text] = new LocalInfo { ExprTok = k + 3 };
                    continue;
                }
                if (U.IsKeyword(x) && x != "string") continue;
                if (k > 0 && (IsP(t, k - 1, ".") || IsP(t, k - 1, "?."))) continue;
                bool candidate = TypesByName.ContainsKey(x) || IsP(t, k + 1, "<") || (k + 1 < t.Count && t[k + 1].Kind == TokKind.Ident && char.IsUpper(x[0]));
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

        static bool IsP(List<Token> t, int i, string s) { return i >= 0 && i < t.Count && t[i].Kind == TokKind.Punct && t[i].Text == s; }
        static bool IsI(List<Token> t, int i) { return i >= 0 && i < t.Count && t[i].Kind == TokKind.Ident; }

        // tipo de un nombre simple dentro del contexto de un metodo
        public TypeSet TypeOfName(MethodDecl m, string name, int depth)
        {
            var ts = new TypeSet();
            if (depth > 8) return ts;
            var owner = m.Owner;
            if (name == "this") { ts.Known = true; if (owner != null) ts.Types.Add(owner); return ts; }
            if (name == "base") { ts.Known = true; if (owner != null) foreach (var b in owner.Bases) ts.Types.AddRange(ResolveTypeRef(b, owner)); return ts; }
            LocalInfo li;
            if (m.HasBody && Locals(m).TryGetValue(name, out li))
            {
                if (li.Cache != null) { ts.Types.AddRange(li.Cache); ts.Known = li.Known || li.Cache.Count > 0; return ts; }
                if (li.Resolving) return ts;
                li.Resolving = true;
                List<TypeDecl> res;
                if (li.Type != null) { res = ResolveTypeRef(U.UnwrapRef(li.Type), owner); li.Known = true; }
                else if (li.ForeachTok >= 0)
                {
                    var elem = ForeachElement(m, li.ForeachTok);
                    res = elem != null ? ResolveTypeRef(elem, owner) : new List<TypeDecl>();
                    li.Known = elem != null;
                }
                else
                {
                    bool known;
                    res = InferExprType(m, li.ExprTok, depth + 1, out known);
                    li.Known = known;
                }
                li.Cache = res; li.Resolving = false;
                ts.Types.AddRange(res); ts.Known = li.Known || res.Count > 0;
                return ts;
            }
            foreach (var p in m.Params)
                if (p.Name == name) { ts.Known = p.Type != null; if (p.Type != null) ts.Types.AddRange(ResolveTypeRef(U.UnwrapRef(p.Type), owner)); return ts; }
            foreach (var o in SelfAndOuters(owner))
            {
                var mv = FindMember(o, name);
                if (mv != null) { ts.Known = mv.Type != null; if (mv.Type != null) ts.Types.AddRange(ResolveTypeRef(U.UnwrapRef(mv.Type), mv.Owner)); return ts; }
                if (o.PrimaryCtor != null)
                    foreach (var p in o.PrimaryCtor)
                        if (p.Name == name) { ts.Known = p.Type != null; if (p.Type != null) ts.Types.AddRange(ResolveTypeRef(U.UnwrapRef(p.Type), o)); return ts; }
                // parametro de constructor asignado a campo con otro nombre no se resuelve aqui
            }
            var tn = ResolveTypeName(name, owner);
            if (tn.Count > 0) { ts.Types.AddRange(tn); ts.Known = true; ts.Static = true; return ts; }
            return ts;
        }

        public List<TypeDecl> InferExprType(MethodDecl m, int k, int depth, out bool known)
        {
            known = false;
            var t = m.File.Toks;
            var r = new List<TypeDecl>();
            if (k < 0 || k >= t.Count || depth > 8) return r;
            if (IsI(t, k) && t[k].Text == "await") k++;
            if (IsI(t, k) && t[k].Text == "new")
            {
                int j = k + 1;
                if (IsP(t, j, "(")) return r;
                var tr = P(m.File).ParseTypeRef(ref j);
                if (tr != null) { known = true; r.AddRange(ResolveTypeRef(tr, m.Owner)); }
                return r;
            }
            if (IsP(t, k, "("))
            {
                int c = m.File.Match[k];
                if (c > k + 1 && IsI(t, k + 1) && (IsI(t, c + 1) || IsP(t, c + 1, "(")))
                {
                    int j = k + 1;
                    var tr = P(m.File).ParseTypeRef(ref j);
                    if (tr != null && j == c) { known = true; r.AddRange(ResolveTypeRef(tr, m.Owner)); return r; }
                }
                return r;
            }
            if (!IsI(t, k)) return r;
            var segs = ParseChainForward(m.File, k);
            if (segs.Count == 0) return r;
            var last = segs[segs.Count - 1];
            if (last.IsCall && last.GenericArgs.Count > 0 && (last.Name.StartsWith("Get") || last.Name.StartsWith("Resolve") || last.Name.StartsWith("Create")))
            {
                known = true;
                r.AddRange(ResolveTypeRef(last.GenericArgs[0], m.Owner));
                return r;
            }
            var ts = ResolveChain(m, segs, depth + 1);
            known = ts.Known;
            return ts.Types;
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
                while (j < close && !IsP(f.Toks, j, ",")) j++;
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

        public TypeSet ResolveChain(MethodDecl m, List<Seg> segs, int depth)
        {
            var cur = new TypeSet();
            if (segs.Count == 0 || depth > 10) return cur;
            int start = 1;
            var s0 = segs[0];
            if (s0.IsCall)
            {
                var targets = new List<MethodDecl>();
                foreach (var o in SelfAndOuters(m.Owner)) { targets.AddRange(MethodsNamed(o, s0.Name, true)); if (targets.Count > 0) break; }
                cur = ReturnTypes(targets);
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
                        var tn = ResolveTypeName(segs[i].Name, m.Owner);
                        if (tn.Count > 0) { cur = new TypeSet { Types = tn, Known = true, Static = true }; start = i + 1; break; }
                    }
                }
            }
            for (int i = start; i < segs.Count; i++)
            {
                var s = segs[i];
                var next = new TypeSet();
                if (cur.Types.Count == 0) { next.Known = false; return next; }
                if (s.IsCall)
                {
                    var targets = FindMethods(cur.Types, s.Name, s.Argc);
                    next = ReturnTypes(targets);
                }
                else
                {
                    foreach (var td in cur.Types)
                    {
                        var mv = FindMember(td, s.Name);
                        if (mv != null) { next.Known = mv.Type != null; if (mv.Type != null) next.Types.AddRange(ResolveTypeRef(U.UnwrapRef(mv.Type), mv.Owner)); continue; }
                        var nested = Types.Where(x => x.Outer != null && (x.Outer.MergedInto ?? x.Outer) == td && x.Name == s.Name).ToList();
                        if (nested.Count > 0) { next.Types.AddRange(nested); next.Known = true; next.Static = true; }
                    }
                }
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
                foreach (var x in ResolveTypeRef(U.UnwrapRef(md.ReturnType), md.Owner)) if (!ts.Types.Contains(x)) ts.Types.Add(x);
            }
            return ts;
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
                    if (tr != null) r.Add(new CallSite { IsNew = true, NewType = tr, Name = tr.Name, Tok = k, Line = tk.Line });
                    continue;
                }
                if (U.IsKeyword(tk.Text) && tk.Text != "this" && tk.Text != "base") continue;
                if (k > 0 && IsI(t, k - 1) && t[k - 1].Text == "new") continue;
                int a = k + 1;
                if (IsP(t, a, "<")) { int g = p.SkipGeneric(a); if (g > 0 && IsP(t, g + 1, "(")) a = g + 1; }
                bool afterDot = IsP(t, k - 1, ".") || IsP(t, k - 1, "?.");
                if (IsP(t, a, "(") && mt[a] > a)
                {
                    // descartar declaraciones de funciones locales: "Tipo Nombre(" con tipo previo
                    if (!afterDot && k > m.BodyStart && IsI(t, k - 1) && !U.IsKeyword(t[k - 1].Text) && IsDeclContext(t, k - 1)) continue;
                    var cs = new CallSite { Name = tk.Text, Tok = k, Line = tk.Line, ArgOpen = a, Argc = CountArgs(f, a) };
                    if (afterDot) cs.Receiver = WalkBack(f, k - 1);
                    if (tk.Text == "this" || tk.Text == "base") continue;
                    r.Add(cs);
                    continue;
                }
                // grupo de metodos pasado como delegado: (X) , X ,  Tipo.X
                if ((IsP(t, k + 1, ",") || IsP(t, k + 1, ")")) && !U.IsKeyword(tk.Text)
                    && (afterDot || (!Locals(m).ContainsKey(tk.Text) && !m.Params.Any(pp => pp.Name == tk.Text))))
                {
                    int chainStart = k;
                    var recv = new List<Seg>();
                    if (afterDot) { recv = WalkBack(f, k - 1); chainStart = k - 1 - 2 * recv.Count; }
                    if (IsP(t, chainStart - 1, "(") || IsP(t, chainStart - 1, ","))
                        r.Add(new CallSite { Name = tk.Text, Tok = k, Line = tk.Line, IsMethodGroup = true, Receiver = recv, Argc = -1 });
                }
            }
            return r;
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
            var t = f.Toks; var mt = f.Match;
            var segs = new List<Seg>();
            int p = dotTok;
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
                    if (IsI(t, ni)) { segs.Insert(0, new Seg { Name = t[ni].Text, IsCall = true, Argc = CountArgs(f, open), ArgOpen = open }); p = ni - 1; continue; }
                    break;
                }
                if (IsP(t, q, "]") && mt[q] >= 0)
                {
                    int open = mt[q];
                    if (IsI(t, open - 1)) { segs.Insert(0, new Seg { Name = t[open - 1].Text, IsIndexer = true }); p = open - 2; continue; }
                    break;
                }
                if (IsI(t, q)) { segs.Insert(0, new Seg { Name = t[q].Text }); p = q - 1; continue; }
                break;
            }
            return segs;
        }

        // Resuelve una llamada a metodos del repo. inferred = resolucion heuristica.
        public List<MethodDecl> ResolveCall(MethodDecl m, CallSite cs, out bool inferred, out bool unresolvedRepoName)
        {
            inferred = false; unresolvedRepoName = false;
            var r = new List<MethodDecl>();
            if (cs.IsNew) return r;
            if (cs.Receiver.Count == 0)
            {
                foreach (var o in SelfAndOuters(m.Owner))
                {
                    var l = MethodsNamed(o, cs.Name, true);
                    if (l.Count > 0)
                    {
                        // metodos virtuales llamados sin receptor: incluir overrides en derivados
                        var res = FilterArgc(l, cs.Argc, false);
                        if (res.Any(x => x.IsAbstract || x.IsVirtual))
                        {
                            List<TypeDecl> impls;
                            if (Implementors.TryGetValue(o.Name, out impls)) foreach (var it in impls) foreach (var md in it.Methods) if (md.Name == cs.Name && !res.Contains(md)) res.Add(md);
                        }
                        return res;
                    }
                }
                foreach (var u in (m.Owner != null ? m.Owner.Usings : new List<string>()))
                {
                    if (!u.StartsWith("static:")) continue;
                    string tn = u.Substring(7); int dot = tn.LastIndexOf('.'); if (dot >= 0) tn = tn.Substring(dot + 1);
                    foreach (var td in ResolveTypeName(tn, m.Owner)) r.AddRange(MethodsNamed(td, cs.Name, true));
                }
                if (r.Count > 0) return FilterArgc(r, cs.Argc, false);
                if (cs.IsMethodGroup) return r;
                return r;
            }
            var recv = ResolveChain(m, cs.Receiver, 0);
            if (recv.Types.Count > 0)
            {
                r = FindMethods(recv.Types, cs.Name, cs.IsMethodGroup ? -1 : cs.Argc);
                if (r.Count > 0) return r;
                // metodo de extension sobre un tipo del repo
                List<MethodDecl> ext;
                if (ExtensionsByName.TryGetValue(cs.Name, out ext))
                {
                    var names = new HashSet<string>(recv.Types.Select(x => x.Name));
                    foreach (var td in recv.Types) foreach (var n in GetAncestorNames(td)) names.Add(n);
                    var e2 = ext.Where(x => x.Params[0].Type != null && (names.Contains(x.Params[0].Type.Name) || x.Params[0].Type.Name.Length <= 2)).ToList();
                    if (e2.Count > 0) return FilterArgc(e2, cs.Argc, true);
                }
                return r;
            }
            if (cs.IsMethodGroup) return r;
            // receptor de tipo conocido pero externo (List, IDbConnection...): solo metodos de extension del repo
            List<MethodDecl> exts;
            if (ExtensionsByName.TryGetValue(cs.Name, out exts))
            {
                string extTypeName = null;
                if (recv.Known && cs.Receiver.Count == 1)
                {
                    // tipo declarado (externo) del receptor
                    extTypeName = DeclaredTypeName(m, cs.Receiver[0].Name);
                }
                var e2 = exts.Where(x => x.Params[0].Type != null && (extTypeName == null || x.Params[0].Type.Name == extTypeName || x.Params[0].Type.Name.Length <= 2)).ToList();
                if (e2.Count > 0 && e2.Count <= 4) { inferred = extTypeName == null; return FilterArgc(e2, cs.Argc, true); }
            }
            if (recv.Known) return r;
            // pista por nombre del receptor: _ventasRepository -> VentasRepository / IVentasRepository
            string hint = cs.Receiver[cs.Receiver.Count - 1].Name.TrimStart('_');
            if (hint.StartsWith("m_") || hint.StartsWith("s_")) hint = hint.Substring(2);
            if (hint.Length > 2)
            {
                var hinted = Types.Where(x => string.Equals(x.Name, hint, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Name, "I" + hint, StringComparison.OrdinalIgnoreCase)).ToList();
                if (hinted.Count > 0)
                {
                    r = FindMethods(hinted, cs.Name, cs.Argc);
                    if (r.Count > 0) { inferred = true; return r; }
                }
            }
            List<MethodDecl> byName;
            if (MethodsByName.TryGetValue(cs.Name, out byName))
            {
                var cand = FilterArgc(byName.Where(x => !x.IsExtension).ToList(), cs.Argc, false);
                int owners = cand.Select(x => x.Owner).Distinct().Count();
                if (cand.Count > 0 && owners <= 3) { inferred = true; return cand; }
                if (cand.Count > 0) unresolvedRepoName = true;
            }
            return r;
        }

        static readonly HashSet<string> CollectionNames = new HashSet<string>(new string[] {
            "IEnumerable", "IList", "List", "ICollection", "IReadOnlyList", "IReadOnlyCollection", "HashSet", "ISet", "IAsyncEnumerable", "Collection", "ObservableCollection", "IQueryable" });

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
            if (tr.Args.Count == 0 && !CollectionNames.Contains(tr.Name)) return tr; // arreglos T[] (el sufijo [] no se conserva en TypeRef)
            return null;
        }

        TypeRef DeclaredTypeRef(MethodDecl m, string name)
        {
            LocalInfo li;
            if (m.HasBody && Locals(m).TryGetValue(name, out li) && li.Type != null) return li.Type;
            foreach (var p in m.Params) if (p.Name == name && p.Type != null) return p.Type;
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
            LocalInfo li;
            if (m.HasBody && Locals(m).TryGetValue(name, out li) && li.Type != null) return li.Type.Name;
            foreach (var p in m.Params) if (p.Name == name && p.Type != null) return p.Type.Name;
            if (m.Owner != null)
            {
                var mv = FindMember(m.Owner, name);
                if (mv != null && mv.Type != null) return mv.Type.Name;
                if (m.Owner.PrimaryCtor != null) foreach (var p in m.Owner.PrimaryCtor) if (p.Name == name && p.Type != null) return p.Type.Name;
            }
            return null;
        }

        // Handlers MediatR/CQRS disparados desde el metodo
        public List<MethodDecl> LinkedHandlers(MethodDecl m)
        {
            var r = new List<MethodDecl>();
            if (RequestHandlers.Count == 0) return r;
            var t = m.File.Toks;
            if (m.HasBody)
                for (int k = m.BodyStart; k < m.BodyEnd && k < t.Count; k++)
                {
                    if (t[k].Kind != TokKind.Ident) continue;
                    List<MethodDecl> h;
                    if (RequestHandlers.TryGetValue(t[k].Text, out h)) foreach (var x in h) if (!r.Contains(x) && x != m) r.Add(x);
                }
            foreach (var p in m.Params)
            {
                if (p.Type == null) continue;
                List<MethodDecl> h;
                if (RequestHandlers.TryGetValue(p.Type.Name, out h) && !h.Contains(m)) foreach (var x in h) if (!r.Contains(x)) r.Add(x);
            }
            return r;
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
            string typeName = names[names.Count - 2];
            var cands = ResolveTypeName(typeName, ctx);
            if (cands.Count > 1 && names.Count > 2)
            {
                string qual = string.Join(".", names.Take(names.Count - 1).ToArray());
                var q = cands.Where(x => x.FullName.EndsWith(qual)).ToList();
                if (q.Count > 0) cands = q;
            }
            foreach (var td in cands) { var mv = FindMember(td, member); if (mv != null) return mv; }
            return null;
        }

        // Valor string de una constante (null si no es una expresion string evaluable)
        public string ConstValue(MemberVar mv)
        {
            if (mv == null || mv.InitStart < 0) return null;
            string v;
            if (constCache.TryGetValue(mv.Id, out v)) return v;
            if (constResolving.Contains(mv.Id)) return null;
            if (!(mv.IsConst || mv.IsStatic || mv.IsReadonly || mv.IsProperty || (mv.Type != null && mv.Type.Name == "string"))) { constCache[mv.Id] = null; return null; }
            constResolving.Add(mv.Id);
            var ev = EvalStringExpr(mv.File, mv.InitStart, mv.InitEnd, mv.Owner, true);
            constResolving.Remove(mv.Id);
            v = ev != null && ev.Complete ? ev.Value : null;
            constCache[mv.Id] = v;
            return v;
        }

        public class StrEval
        {
            public StringBuilder Sb = new StringBuilder();
            public List<FragPiece> Pieces = new List<FragPiece>();
            public bool Complete = true;
            public bool HasLiteral;
            public int EndTok;
            public string Value { get { return Sb.ToString(); } }
        }

        // Evalua una concatenacion de literales/constantes en [s,e). requireAll: toda la expresion debe ser string.
        public StrEval EvalStringExpr(SourceFile f, int s, int e, TypeDecl ctx, bool requireAll)
        {
            var t = f.Toks;
            var ev = new StrEval();
            int k = s;
            bool expect = true;
            int guard = 0;
            while (k < e && guard++ < 500)
            {
                if (expect)
                {
                    if (t[k].Kind == TokKind.Str)
                    {
                        AppendLiteral(ev, f, t[k], ctx);
                        ev.HasLiteral = true; k++; expect = false; continue;
                    }
                    if (IsP(t, k, "(") && f.Match[k] > k && f.Match[k] < e)
                    {
                        var inner = EvalStringExpr(f, k + 1, f.Match[k], ctx, true);
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
                        if (IsP(t, j, "(") || IsP(t, j, "<") || IsP(t, j, "["))
                        {
                            if (requireAll) { ev.Complete = false; break; }
                            // llamada u otra expresion: marcador de posicion
                            int z = j;
                            while (z < e && (IsP(t, z, "(") || IsP(t, z, "[") || IsP(t, z, "<")) )
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
                        var mv = ResolveMemberChain(names, ctx);
                        string cv = mv != null ? ConstValue(mv) : null;
                        if (cv != null)
                        {
                            var sub = EvalStringExpr(mv.File, mv.InitStart, mv.InitEnd, mv.Owner, true);
                            if (sub != null)
                            {
                                int baseOff = ev.Sb.Length;
                                foreach (var pc in sub.Pieces)
                                    ev.Pieces.Add(new FragPiece { ValueStart = baseOff + pc.ValueStart, ValueLength = pc.ValueLength, File = pc.File, Line = pc.Line, CountsLines = pc.CountsLines, Const = pc.Const ?? mv });
                                ev.Sb.Append(sub.Value);
                                ev.HasLiteral = true;
                            }
                            k = j; expect = false; continue;
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

        static void Merge(StrEval ev, StrEval inner)
        {
            int baseOff = ev.Sb.Length;
            foreach (var pc in inner.Pieces) ev.Pieces.Add(new FragPiece { ValueStart = baseOff + pc.ValueStart, ValueLength = pc.ValueLength, File = pc.File, Line = pc.Line, CountsLines = pc.CountsLines, Const = pc.Const });
            ev.Sb.Append(inner.Value);
            ev.HasLiteral |= inner.HasLiteral;
            ev.Complete &= inner.Complete;
        }

        public void AppendLiteral(StrEval ev, SourceFile f, Token tk, TypeDecl ctx)
        {
            int startOff = ev.Sb.Length;
            foreach (var part in tk.Lit.Parts)
            {
                if (!part.IsHole) { ev.Sb.Append(part.Text); continue; }
                string hv = ResolveHole(part.Text, ctx);
                if (hv == null) { ev.Sb.Append("{?}"); ev.Complete = false; }
                else ev.Sb.Append(hv);
            }
            ev.Pieces.Add(new FragPiece { ValueStart = startOff, ValueLength = ev.Sb.Length - startOff, File = f, Line = tk.Line, CountsLines = tk.Lit.CountsLines });
        }

        public string ResolveHole(string expr, TypeDecl ctx)
        {
            if (string.IsNullOrEmpty(expr)) return null;
            var mm = Regex.Match(expr, @"^nameof\s*\(\s*(?:[\w]+\s*\.\s*)*(\w+)\s*\)$");
            if (mm.Success) return mm.Groups[1].Value;
            if (!Regex.IsMatch(expr, @"^[A-Za-z_][\w]*(\s*\.\s*[A-Za-z_][\w]*)*$")) return null;
            var names = expr.Split('.').Select(x => x.Trim()).ToList();
            var mv = ResolveMemberChain(names, ctx);
            return mv != null ? ConstValue(mv) : null;
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
        public Regex PkgRx, ObjRx, PkgOnlyRx;
        AnalyzerOptions opt;
        const string Id = @"[A-Za-z][\w$#]*";

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

        // Hits en codigo SQL (texto ya sin comentarios ni literales). requireCall aplica a nombres sueltos.
        public List<SpHit> FindInCode(string code, bool wholeIsBare)
        {
            var hits = new List<SpHit>();
            var spans = new List<int[]>();
            foreach (Match mm in PkgRx.Matches(code))
            {
                // descartar %TYPE / %ROWTYPE
                int after = mm.Index + mm.Length;
                if (after < code.Length && code[after] == '%') continue;
                var sp = Make(mm.Groups["schema"].Value, mm.Groups["pkg"].Value, mm.Groups["mem"].Value, mm.Groups["link"].Value);
                hits.Add(new SpHit { Sp = sp, Offset = mm.Groups["pkg"].Index });
                spans.Add(new int[] { mm.Index, mm.Index + mm.Length });
            }
            foreach (Match mm in ObjRx.Matches(code))
            {
                bool overlap = spans.Any(s => mm.Index < s[1] && mm.Index + mm.Length > s[0]);
                if (overlap) continue;
                string schema = mm.Groups["schema"].Value;
                if (IsPackageName(schema)) continue;
                if (!wholeIsBare)
                {
                    int a = mm.Index + mm.Length;
                    while (a < code.Length && char.IsWhiteSpace(code[a])) a++;
                    bool callLike = a >= code.Length || code[a] == '(' || code[a] == ';';
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

        // Menciones en comentarios / texto libre. Empareja "PCK_X ... SP_Y" sueltos.
        public List<SpHit> FindInText(string text)
        {
            var hits = new List<SpHit>();
            var spans = new List<int[]>();
            foreach (Match mm in PkgRx.Matches(text))
            {
                var sp = Make(mm.Groups["schema"].Value, mm.Groups["pkg"].Value, mm.Groups["mem"].Value, mm.Groups["link"].Value);
                hits.Add(new SpHit { Sp = sp, Offset = mm.Index });
                spans.Add(new int[] { mm.Index, mm.Index + mm.Length });
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
        static readonly Regex DualColRx = new Regex(@"^(?:""?[\w$#]+""?\s*\.\s*){0,2}""?[\w$#]+""?(?:\s*@\s*[\w$#.]+)?\s*\([\s\S]*\)(?:\s+(?:AS\s+)?""?[\w$#]+""?)?$", RegexOptions.IgnoreCase);
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
        public List<Marker> MethodMarkers = new List<Marker>();
        public List<Marker> ClassMarkers = new List<Marker>();
        public List<Warn> Warnings = new List<Warn>();
    }

    public class FragmentExtractor
    {
        CodeIndex ix;
        SpMatcher sm;
        int fragSeq = 0, markSeq = 0, blockSeq = 0;
        Dictionary<string, MethodAnalysis> cache = new Dictionary<string, MethodAnalysis>();
        Dictionary<string, bool> wrapperCache = new Dictionary<string, bool>();
        Dictionary<string, List<Marker>> classMarkerCache = new Dictionary<string, List<Marker>>();
        public Dictionary<string, Fragment> ConstFragments = new Dictionary<string, Fragment>();
        public HashSet<string> ReferencedConsts = new HashSet<string>();

        static readonly Regex MsgCallee = new Regex(@"^(Log\w*|Write\w*|Trace\w*|Debug\w*|Info|Information|Warn|Warning|Error|Fatal|Critical|Verbose|Print\w*|Assert\w*|Fail|Problem|ValidationProblem|AddError|AddModelError|Append\w*Message)$");
        static readonly Regex ExecCallee = new Regex(@"^(Query\w*|Execute\w*|CommandDefinition|OracleCommand|SqlCommand|DbCommand|NpgsqlCommand|OleDbCommand|OdbcCommand|ExecSp\w*|Exec\w*)$");
        static readonly Regex SqlFileRx = new Regex(@"[\w./\\-]*?([\w-]+(?:\.[\w-]+)*)\.sql\b", RegexOptions.IgnoreCase);

        public FragmentExtractor(CodeIndex index, SpMatcher matcher) { ix = index; sm = matcher; }

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
                if (!join && curComments.Count > 0) { FinishBlock(ma, f, curComments); curComments = new List<Comment>(); }
                curComments.Add(c);
                prev = c;
            }
            if (curComments.Count > 0) FinishBlock(ma, f, curComments);

            // fragmentos
            var locals = ix.Locals(m);
            for (int k = m.BodyStart; k < m.BodyEnd && k < t.Count; k++)
            {
                var tk = t[k];
                if (tk.Kind == TokKind.Str)
                {
                    var ev = ix.EvalStringExpr(f, k, m.BodyEnd, m.Owner, false);
                    if (ev != null)
                    {
                        AddFragment(ma, m, ev, k, Math.Max(k + 1, ev.EndTok), "literal", null);
                        k = Math.Max(k, ev.EndTok - 1);
                    }
                    continue;
                }
                if (tk.Kind != TokKind.Ident || U.IsKeyword(tk.Text) && tk.Text != "this") continue;
                if (k > 0 && (t[k - 1].Kind == TokKind.Punct && (t[k - 1].Text == "." || t[k - 1].Text == "?." || t[k - 1].Text == "::"))) continue;
                var names = new List<string>();
                int j = k;
                while (j < m.BodyEnd && t[j].Kind == TokKind.Ident) { names.Add(t[j].Text); if (IsP(t, j + 1, ".") && j + 2 < t.Count && t[j + 2].Kind == TokKind.Ident) j += 2; else { j++; break; } }
                if (names.Count == 0) continue;
                bool isCall = IsP(t, j, "(") || IsP(t, j, "<");
                if (isCall) { k = j - 1; continue; }
                if (names.Count == 1 && (locals.ContainsKey(names[0]) || m.Params.Any(p => p.Name == names[0]))) { continue; }
                var mv = ix.ResolveMemberChain(names, m.Owner);
                if (mv != null)
                {
                    string cv = ix.ConstValue(mv);
                    if (cv != null)
                    {
                        ReferencedConsts.Add(mv.Id);
                        var ev = ix.EvalStringExpr(f, k, m.BodyEnd, m.Owner, false);
                        if (ev != null)
                        {
                            var fr = AddFragment(ma, m, ev, k, Math.Max(j, ev.EndTok), "const", mv);
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
                            AddSqlFileFragments(ma, m, qt.Lit.PlainValue(), k, j, f, tk.Line);
                        }
                    }
                    // opciones/configuracion: _opt.Value.SpListar
                    if (names.Count >= 2) TryConfigLeaf(ma, m, names[names.Count - 1], k, j, f, tk.Line);
                    k = j - 1; continue;
                }
                if (names.Count >= 2)
                {
                    string key = names[names.Count - 1];
                    string bs = names[names.Count - 2];
                    List<ResxEntry> re;
                    if (ix.ResxBases.Contains(bs) && ix.ResxByKey.TryGetValue(key, out re))
                    {
                        foreach (var r in re.Where(x => string.Equals(x.FileBase, bs, StringComparison.OrdinalIgnoreCase)))
                            AddExternalFragment(ma, m, r.Value, r.File, r.Line, k, j, f, tk.Line, "resx");
                    }
                    else TryConfigLeaf(ma, m, key, k, j, f, tk.Line);
                }
                k = j - 1;
            }
            // asignar bloque mas cercano a cada fragmento
            foreach (var fr in ma.Fragments)
            {
                MarkerBlock best = null;
                foreach (var b in ma.Blocks)
                {
                    bool cand = b.Start < fr.StartOffset || (b.Start < fr.EndOffset) || (b.Line == fr.EndLine && b.Start >= fr.StartOffset);
                    if (!cand) continue;
                    if (best == null || b.Start > best.Start) best = b;
                }
                if (best != null) { fr.Block = best; best.Scoped = true; }
            }
            foreach (var b in ma.Blocks) if (!b.Scoped) foreach (var mk in b.Markers) ma.MethodMarkers.Add(mk);
            return ma;
        }

        void FinishBlock(MethodAnalysis ma, SourceFile f, List<Comment> cs)
        {
            var b = new MarkerBlock { Start = cs[0].Start, End = cs[cs.Count - 1].End, Line = cs[0].Line, EndLine = cs[cs.Count - 1].EndLine, Id = "B" + (++blockSeq) };
            AddTextMarkers(b.Markers, cs, "body-comment");
            if (b.Markers.Count > 0) ma.Blocks.Add(b);
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

        static bool IsP(List<Token> t, int i, string s) { return i >= 0 && i < t.Count && t[i].Kind == TokKind.Punct && t[i].Text == s; }

        Fragment AddFragment(MethodAnalysis ma, MethodDecl m, CodeIndex.StrEval ev, int tokStart, int tokEnd, string origin, MemberVar constRef)
        {
            var f = m.File; var t = f.Toks;
            var fr = new Fragment
            {
                Id = "F" + (++fragSeq), Value = ev.Value, Pieces = ev.Pieces, Method = m, File = f,
                TokStart = tokStart, TokEnd = tokEnd, Line = t[tokStart].Line, EndLine = t[Math.Min(tokEnd, t.Count) - 1].EndLine,
                StartOffset = t[tokStart].Start, EndOffset = t[Math.Min(tokEnd, t.Count) - 1].End, Origin = origin
            };
            if (constRef != null && tokEnd - tokStart <= 2 * 6 && ev.Pieces.All(p => p.Const != null)) { fr.OnlyConstRef = true; fr.ConstRef = constRef; }
            // contexto de la invocacion que contiene el fragmento
            string callee; bool isNew; int argIndex; string namedArg; int open;
            EnclosingCall(m, tokStart, tokEnd, out callee, out isNew, out argIndex, out namedArg, out open);
            if (callee != null)
            {
                bool logRecv = false;
                if (open > 1 && IsP(t, open - 2, ".")) { var recv = ix.WalkBack(f, open - 2); logRecv = recv.Any(s => s.Name.IndexOf("log", StringComparison.OrdinalIgnoreCase) >= 0 || s.Name == "Console" || s.Name == "Debug" || s.Name == "Trace"); }
                if ((isNew && callee.EndsWith("Exception")) || (!isNew && MsgCallee.IsMatch(callee) && (logRecv || !callee.StartsWith("Append"))) || logRecv) fr.MessageContext = true;
                if (!fr.MessageContext)
                {
                    bool firstArg = argIndex == 0 || (namedArg != null && Regex.IsMatch(namedArg, @"^(sql|commandText|query|spName|procedure\w*|storedProcedure\w*|nombreSp|sp)$", RegexOptions.IgnoreCase));
                    if (firstArg && ExecCallee.IsMatch(callee) && RangeHasIdent(t, open, f.Match[open], "StoredProcedure")) fr.SpContext = true;
                    if (firstArg && ExecCallee.IsMatch(callee) && (callee.Contains("Command")) && BodyHasIdent(m, "StoredProcedure")) fr.SpContext = true;
                    if (!fr.SpContext && !isNew)
                    {
                        // wrapper del repo: metodo con parametro string y CommandType.StoredProcedure
                        var cs = new CallSite { Name = callee, Argc = ix.CountArgs(f, open), ArgOpen = open };
                        if (open > 1 && IsP(t, open - 2, ".")) cs.Receiver = ix.WalkBack(f, open - 2);
                        bool inf, unr;
                        var targets = ix.ResolveCall(m, cs, out inf, out unr);
                        foreach (var tg in targets)
                        {
                            int sIdx = FirstStringParam(tg);
                            if (sIdx >= 0 && (argIndex == sIdx || (namedArg != null && tg.Params[sIdx].Name == namedArg)) && IsSpWrapper(tg)) { fr.SpContext = true; break; }
                        }
                    }
                }
            }
            else
            {
                // cmd.CommandText = "PCK.SP";
                if (tokStart >= 2 && IsP(t, tokStart - 1, "=") && t[tokStart - 2].Kind == TokKind.Ident && t[tokStart - 2].Text == "CommandText" && BodyHasIdent(m, "StoredProcedure")) fr.SpContext = true;
                // propiedad de objeto: new X { CommandText = "..." }
            }
            if (fr.MessageContext)
            {
                foreach (var h in sm.FindInText(fr.Value))
                    ma.MethodMarkers.Add(NewMarker(h.Sp, fr.LocOfValueOffset(h.Offset), "log", fr.Value));
                return fr;
            }
            // referencias a archivos .sql
            if (SqlFileRx.IsMatch(fr.Value)) AddSqlFileFragments(ma, m, fr.Value, tokStart, tokEnd, f, fr.Line);
            // referencias a claves de configuracion "Seccion:Clave"
            ConfigEntry ce;
            if (fr.Value.Contains(":") && ix.ConfigByPath.TryGetValue(fr.Value.Trim(), out ce))
            {
                AddExternalFragment(ma, m, ce.Value, ce.File, ce.Line, tokStart, tokEnd, f, fr.Line, "config");
                return fr;
            }
            if (constRef != null)
                foreach (var pc in ev.Pieces) if (pc.Const != null) AddConstMarkers(fr, pc.Const);
            Finalize(ma, fr);
            return fr;
        }

        void AddConstMarkers(Fragment fr, MemberVar mv)
        {
            if (fr.ConstMarkers.Any(x => x.Excerpt == "__" + mv.Id)) return;
            var tmp = new List<Marker>();
            AddTextMarkers(tmp, mv.Leading, "const-comment");
            foreach (var x in tmp) if (!fr.ConstMarkers.Any(y => y.Sp.Key == x.Sp.Key)) fr.ConstMarkers.Add(x);
        }

        // Clasifica el fragmento y lo agrega si es relevante
        void Finalize(MethodAnalysis ma, Fragment fr)
        {
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
            var hits = sm.FindInCode(scan.Blank, bare);
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
                if (sp != null) hits.Add(new SpHit { Sp = sp, Offset = fr.Value.IndexOf(parts[0], StringComparison.Ordinal) < 0 ? 0 : fr.Value.IndexOf(parts[0], StringComparison.Ordinal) });
            }
            if (bare && !fr.SpContext && hits.Count > 0)
            {
                // nombre suelto que calza con el patron: es llamada directa igualmente
            }
            foreach (var h in hits)
            {
                h.Loc = fr.LocOfValueOffset(h.Offset);
                fr.Calls.Add(h);
            }
            if (Regex.IsMatch(scan.Blank, @"(?:" + string.Join("|", ix.Opt.PackagePrefixes.Select(x => Regex.Escape(x)).ToArray()) + @")[\w$#]*\s*\.\s*\{\?\}|\{\?\}\s*\.\s*(?:" + string.Join("|", ix.Opt.ObjectPrefixes.Select(x => Regex.Escape(x)).ToArray()) + @")?[\w$#]*\s*\(", RegexOptions.IgnoreCase))
                fr.HasDynamicSp = true;
            bool relevant = fr.Calls.Count > 0 || fr.Kind == "regular" || fr.InlineMarkers.Count > 0 || fr.HasDynamicSp;
            if (fr.Kind == "regular" && fr.Calls.Count == 0 && fr.Value.Trim().Length < 12) relevant = false;
            if (relevant) ma.Fragments.Add(fr);
        }

        void TryConfigLeaf(MethodAnalysis ma, MethodDecl m, string leaf, int tokStart, int tokEnd, SourceFile f, int line)
        {
            List<ConfigEntry> ce;
            if (!ix.ConfigByLeaf.TryGetValue(leaf, out ce)) return;
            foreach (var c in ce)
                if (sm.MatchesPattern(c.Value)) { AddExternalFragment(ma, m, c.Value, c.File, c.Line, tokStart, tokEnd, f, line, "config"); break; }
        }

        void AddSqlFileFragments(MethodAnalysis ma, MethodDecl m, string value, int tokStart, int tokEnd, SourceFile f, int line)
        {
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
                    AddExternalFragment(ma, m, sf.Text, sf, 1, tokStart, tokEnd, f, line, "sqlfile");
            }
        }

        void AddExternalFragment(MethodAnalysis ma, MethodDecl m, string value, SourceFile src, int srcLine, int tokStart, int tokEnd, SourceFile f, int line, string origin)
        {
            var t = f.Toks;
            var fr = new Fragment
            {
                Id = "F" + (++fragSeq), Value = value ?? "", Method = m, File = f, TokStart = tokStart, TokEnd = Math.Max(tokStart + 1, tokEnd),
                Line = line, EndLine = line, StartOffset = t[tokStart].Start, EndOffset = t[Math.Min(Math.Max(tokStart + 1, tokEnd), t.Count) - 1].End, Origin = origin, OnlyConstRef = true
            };
            fr.Pieces.Add(new FragPiece { ValueStart = 0, ValueLength = fr.Value.Length, File = src, Line = srcLine, CountsLines = true, ExternalKind = origin });
            if (origin == "config") fr.SpContext = true;
            Finalize(ma, fr);
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
            // indice de argumento
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

        static int FirstStringParam(MethodDecl md)
        {
            for (int i = 0; i < md.Params.Count; i++)
            {
                if (md.Params[i].IsThis) continue;
                var ty = md.Params[i].Type;
                if (ty != null && (ty.Name == "string" || ty.Name == "String")) return md.IsExtension ? i - 1 : i;
            }
            return -1;
        }

        public bool IsSpWrapper(MethodDecl md)
        {
            bool r;
            if (wrapperCache.TryGetValue(md.Id, out r)) return r;
            r = FirstStringParam(md) >= 0 && BodyHasIdent(md, "StoredProcedure");
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
    public class EndpointFinder
    {
        CodeIndex ix;
        FragmentExtractor fx;
        SpMatcher sm;
        AnalysisResult res;
        int seq = 0;
        string webApiTemplate, mvcTemplate;
        Dictionary<string, List<KeyValuePair<MethodDecl, CallSite>>> callersByName;

        static readonly string[] VerbAttrs = new string[] { "HttpGet", "HttpPost", "HttpPut", "HttpDelete", "HttpPatch", "HttpHead", "HttpOptions" };
        static readonly HashSet<string> RouteBuilderTypes = new HashSet<string>(new string[] {
            "IEndpointRouteBuilder", "RouteGroupBuilder", "WebApplication", "IApplicationBuilder", "IEndpointConventionBuilder", "RouteHandlerBuilder" });

        public EndpointFinder(CodeIndex index, FragmentExtractor f, SpMatcher matcher, AnalysisResult r) { ix = index; fx = f; sm = matcher; res = r; }

        public List<Endpoint> Find()
        {
            var eps = new List<Endpoint>();
            FindConventionalTemplates();
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
        public static string Normalize(string r)
        {
            if (r == null) r = "";
            r = r.Replace('\\', '/').Trim();
            if (r.StartsWith("~")) r = r.Substring(1);
            r = Regex.Replace(r, @"\{\*{0,2}([A-Za-z_][\w]*)[^}]*\}", "{$1}");
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
        static List<AttrInfo> Attrs(List<AttrInfo> l, string name) { return l.Where(a => a.Name == name).ToList(); }

        static string FirstString(AttrInfo a)
        {
            for (int i = 0; i < a.Positional.Count; i++) if (a.PositionalIsString[i]) return a.Positional[i];
            string v;
            if (a.Named.TryGetValue("template", out v)) return v.Trim('"');
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
                    if (x != "MapHttpRoute" && x != "MapRoute" && x != "MapControllerRoute") continue;
                    if (!IsP(t, k + 1, "(")) continue;
                    int c = m.File.Match[k + 1];
                    string tpl = null;
                    var strs = new List<string>();
                    for (int q = k + 2; q < c; q++)
                    {
                        if (t[q].Kind == TokKind.Str) strs.Add(t[q].Lit.PlainValue());
                        if (IsI(t, q) && (t[q].Text == "routeTemplate" || t[q].Text == "pattern" || t[q].Text == "url") && IsP(t, q + 1, ":") && q + 2 < c && t[q + 2].Kind == TokKind.Str) tpl = t[q + 2].Lit.PlainValue();
                    }
                    if (tpl == null) tpl = strs.FirstOrDefault(s => s.Contains("{controller"));
                    if (tpl == null && strs.Count > 1) tpl = strs[1];
                    if (tpl == null) continue;
                    if (x == "MapHttpRoute") { if (webApiTemplate == null || (tpl.Contains("api") && !webApiTemplate.Contains("api"))) webApiTemplate = tpl; }
                    else { if (mvcTemplate == null) mvcTemplate = tpl; }
                }
            }
            res.ConventionalTemplate = webApiTemplate ?? mvcTemplate;
        }

        // ------------------------------------------------------------ controllers
        List<Endpoint> Controllers()
        {
            var eps = new List<Endpoint>();
            foreach (var td in ix.Types)
            {
                if (td.Kind != "class" || td.IsAbstract || td.IsStatic) continue;
                if (Attr(td.Attrs, "NonController") != null) continue;
                var anc = ix.GetAncestorNames(td);
                bool isWebApi2 = anc.Contains("ApiController") && !anc.Contains("ControllerBase");
                bool isCtrl = td.Name.EndsWith("Controller") || Attr(td.Attrs, "ApiController") != null || Attr(td.Attrs, "Controller") != null
                              || anc.Contains("Controller") || anc.Contains("ControllerBase") || anc.Contains("ApiController") || anc.Contains("ODataController");
                if (!isCtrl) continue;
                if (td.Name.EndsWith("Controller") && anc.Count == 0 && Attr(td.Attrs, "ApiController") == null && Attr(td.Attrs, "Route") == null && Attr(td.Attrs, "RoutePrefix") == null)
                {
                    // clase "XController" sin base ni atributos: puede ser un POCO controller, se acepta igual
                }
                string ctrlName = td.Name.EndsWith("Controller") && td.Name.Length > 10 ? td.Name.Substring(0, td.Name.Length - 10) : td.Name;
                // rutas de clase (heredables)
                var classTpls = new List<string>();
                var chain = new List<TypeDecl> { td };
                chain.AddRange(ix.Ancestors(td).Where(a => a.Kind == "class"));
                foreach (var c in chain)
                {
                    foreach (var a in c.Attrs.Where(a => a.Name == "Route" || a.Name == "RoutePrefix"))
                    {
                        string s = FirstString(a);
                        if (s != null) classTpls.Add(s);
                    }
                    if (classTpls.Count > 0) break;
                }
                string area = null;
                var areaAttr = chain.SelectMany(c => c.Attrs).FirstOrDefault(a => a.Name == "Area");
                if (areaAttr != null) area = FirstString(areaAttr);
                // acciones: propias + heredadas de controllers base del repo
                var actions = new List<MethodDecl>();
                var seen = new HashSet<string>();
                foreach (var c in chain)
                {
                    foreach (var md in c.Methods)
                    {
                        if (md.IsCtor || md.IsStatic || !md.IsPublic || md.IsAbstract || !md.HasBody || md.IsSynthetic) continue;
                        if (Attr(md.Attrs, "NonAction") != null) continue;
                        if (md.Name == "Dispose" || md.Name == "ToString" || md.Name == "Equals" || md.Name == "GetHashCode") continue;
                        string sig = md.Name + "/" + md.Params.Count;
                        if (!seen.Add(sig)) continue;
                        actions.Add(md);
                    }
                }
                foreach (var md in actions)
                {
                    var verbsTpls = new List<KeyValuePair<string, string>>();
                    var routeTpls = new List<string>();
                    string actionName = md.Name;
                    var an = Attr(md.Attrs, "ActionName");
                    if (an != null && FirstString(an) != null) actionName = FirstString(an);
                    else if (!isWebApi2 && actionName.EndsWith("Async") && actionName.Length > 5) actionName = actionName.Substring(0, actionName.Length - 5);
                    foreach (var a in md.Attrs)
                    {
                        if (VerbAttrs.Contains(a.Name))
                        {
                            string v = a.Name.Substring(4).ToUpperInvariant();
                            verbsTpls.Add(new KeyValuePair<string, string>(v, FirstString(a)));
                        }
                        else if (a.Name == "AcceptVerbs")
                        {
                            string rt; a.Named.TryGetValue("Route", out rt); if (rt != null) rt = rt.Trim('"');
                            for (int i = 0; i < a.Positional.Count; i++)
                            {
                                string pv = a.Positional[i];
                                if (!a.PositionalIsString[i]) { var mm = Regex.Match(pv, @"(Get|Post|Put|Delete|Patch|Head|Options)\b", RegexOptions.IgnoreCase); if (!mm.Success) continue; pv = mm.Value; }
                                verbsTpls.Add(new KeyValuePair<string, string>(pv.ToUpperInvariant(), rt));
                            }
                        }
                        else if (a.Name == "Route")
                        {
                            string s = FirstString(a); if (s != null) routeTpls.Add(s);
                        }
                    }
                    bool attributeRouted = classTpls.Count > 0 || routeTpls.Count > 0 || verbsTpls.Any(x => x.Value != null);
                    if (verbsTpls.Count == 0)
                    {
                        string v = "ANY";
                        if (isWebApi2 || !attributeRouted && webApiTemplate != null)
                        {
                            var mm = Regex.Match(md.Name, @"^(Get|Post|Put|Delete|Patch|Head|Options)");
                            v = mm.Success ? mm.Value.ToUpperInvariant() : "POST";
                        }
                        verbsTpls.Add(new KeyValuePair<string, string>(v, null));
                    }
                    var routes = new List<KeyValuePair<string, string>>();
                    if (!attributeRouted)
                    {
                        string conv = (isWebApi2 ? webApiTemplate : (mvcTemplate ?? webApiTemplate)) ?? (isWebApi2 ? "api/{controller}/{id}" : "api/[controller]");
                        string r = ConventionalRoute(conv, ctrlName, actionName, md);
                        foreach (var vt in verbsTpls) routes.Add(new KeyValuePair<string, string>(vt.Key, r));
                    }
                    else
                    {
                        var prefixes = classTpls.Count > 0 ? classTpls : new List<string> { "" };
                        foreach (var vt in verbsTpls)
                        {
                            var tpls = vt.Value != null ? new List<string> { vt.Value } : (routeTpls.Count > 0 ? routeTpls : new List<string> { null });
                            foreach (var p in prefixes) foreach (var tp in tpls) routes.Add(new KeyValuePair<string, string>(vt.Key, Combine(p, tp)));
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
                        var ep = new Endpoint { Verb = vr.Key, Route = r, Kind = isWebApi2 ? "WebApi2" : "Controller", Handler = md, HandlerName = td.Name + "." + md.Name, File = md.File, Line = md.Line };
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
            { "MapGet", "GET" }, { "MapPost", "POST" }, { "MapPut", "PUT" }, { "MapDelete", "DELETE" }, { "MapPatch", "PATCH" }, { "MapMethods", "*" } };

        List<Endpoint> MinimalApis()
        {
            var eps = new List<Endpoint>();
            foreach (var m in ix.Methods.ToList())
            {
                if (!m.HasBody) continue;
                foreach (var cs in ix.Calls(m))
                {
                    if (cs.IsNew || cs.IsMethodGroup || !MapVerbs.ContainsKey(cs.Name) || cs.ArgOpen < 0) continue;
                    var f = m.File; var t = f.Toks;
                    var args = SplitArgs(f, cs.ArgOpen);
                    if (args.Count < 2) continue;
                    string tpl = "{?}";
                    var ev = ix.EvalStringExpr(f, args[0][0], args[0][1], m.Owner, true);
                    if (ev != null) tpl = ev.Value;
                    else if (args[0][1] - args[0][0] == 1 && t[args[0][0]].Kind == TokKind.Str) tpl = t[args[0][0]].Lit.PlainValue();
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
                    MethodDecl handler = BuildHandler(m, hs, he);
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
                    // marcadores: comentarios antes de la sentencia
                    var extra = StatementComments(m, cs.Tok - 2 * cs.Receiver.Count);
                    var prefixes = ReceiverPrefixes(m, cs.Receiver, 0);
                    foreach (var pf in prefixes.Distinct())
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
            // lambda?
            int arrow = -1;
            for (int q = k; q < he; q++)
            {
                if (IsP(t, q, "=>")) { arrow = q; break; }
                if ((IsP(t, q, "(") || IsP(t, q, "[") || IsP(t, q, "{")) && mt[q] > q) { if (q != k) break; q = mt[q]; continue; }
                if (q > k + 1 && !IsP(t, q, "=>")) break;
            }
            if (arrow > 0)
            {
                var md = new MethodDecl { Name = "lambda@" + t[hs].Line, Owner = m.Owner, File = f, Line = t[hs].Line, IsSynthetic = true, IsStatic = true, Attrs = attrs };
                md.Id = "L" + (++lambdaSeq);
                if (IsP(t, k, "(") && mt[k] > k) md.Params = ix.P(f).ParseParams(k, mt[k]);
                else if (IsI(t, k)) md.Params.Add(new ParamInfo { Name = t[k].Text });
                int b = arrow + 1;
                if (IsP(t, b, "{") && mt[b] > b) { md.BodyStart = b + 1; md.BodyEnd = mt[b]; }
                else { md.BodyStart = b; md.BodyEnd = he; }
                md.DeclStartOffset = t[hs].Start; md.DeclEndOffset = t[he - 1].End;
                return md;
            }
            // grupo de metodos: Nombre | Tipo.Nombre
            var names = new List<string>();
            int j = k;
            while (IsI(t, j)) { names.Add(t[j].Text); if (IsP(t, j + 1, ".") && IsI(t, j + 2)) j += 2; else { j++; break; } }
            if (names.Count == 0 || j != he) return null;
            string mname = names[names.Count - 1];
            var cands = new List<MethodDecl>();
            if (names.Count == 1)
            {
                var o = m.Owner;
                while (o != null && cands.Count == 0) { cands.AddRange(ix.MethodsNamed(o.MergedInto ?? o, mname, true)); o = o.Outer; }
                if (cands.Count == 0) { List<MethodDecl> l; if (ix.MethodsByName.TryGetValue(mname, out l) && l.Select(x => x.Owner).Distinct().Count() == 1) cands.AddRange(l); }
            }
            else
            {
                foreach (var td in ix.ResolveTypeName(names[names.Count - 2], m.Owner)) cands.AddRange(ix.MethodsNamed(td, mname, true));
                if (cands.Count == 0)
                {
                    // instancia: handler.Metodo
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

        // prefijos de MapGroup para el receptor de una llamada Map*
        List<string> ReceiverPrefixes(MethodDecl m, List<Seg> segs, int depth)
        {
            var r = new List<string>();
            if (segs == null || segs.Count == 0 || depth > 48) { r.Add(""); return r; }
            List<string> roots;
            var s0 = segs[0];
            if (s0.IsCall && s0.Name == "MapGroup") roots = new List<string> { "" };
            else if (s0.IsCall) roots = new List<string> { "" };
            else roots = NamePrefixes(m, s0.Name, depth + 1);
            foreach (var root in roots)
            {
                string pf = root;
                for (int i = 0; i < segs.Count; i++)
                {
                    var s = segs[i];
                    if (s.IsCall && s.Name == "MapGroup" && s.ArgOpen >= 0)
                    {
                        var args = SplitArgs(m.File, s.ArgOpen);
                        if (args.Count > 0)
                        {
                            var ev = ix.EvalStringExpr(m.File, args[0][0], args[0][1], m.Owner, true);
                            pf = Join(pf, ev != null ? ev.Value : "{?}");
                        }
                    }
                }
                r.Add(pf);
            }
            return r;
        }

        List<string> NamePrefixes(MethodDecl m, string name, int depth)
        {
            var r = new List<string>();
            LocalInfo li;
            if (m.HasBody && ix.Locals(m).TryGetValue(name, out li) && li.ExprTok >= 0)
            {
                var segs = ix.ParseChainForward(m.File, li.ExprTok);
                if (segs.Count > 0 && segs[0].Name == name) { r.Add(""); return r; }
                return ReceiverPrefixes(m, segs, depth + 1);
            }
            int pi = m.Params.FindIndex(p => p.Name == name);
            if (pi >= 0)
            {
                var prefixes = CallerPrefixes(m, pi, depth + 1);
                // Carter: CarterModule("/prefijo")
                if (m.Owner != null && ix.DerivesFrom(m.Owner, "CarterModule") && m.Owner.BaseCtorStrings.Count > 0)
                    prefixes = prefixes.Select(x => Join(x, m.Owner.BaseCtorStrings[0])).ToList();
                return prefixes;
            }
            if (m.Owner != null && ix.DerivesFrom(m.Owner, "CarterModule") && m.Owner.BaseCtorStrings.Count > 0) { r.Add(m.Owner.BaseCtorStrings[0]); return r; }
            r.Add("");
            return r;
        }

        List<string> CallerPrefixes(MethodDecl target, int paramIndex, int depth)
        {
            var r = new List<string>();
            if (callersByName == null)
            {
                callersByName = new Dictionary<string, List<KeyValuePair<MethodDecl, CallSite>>>();
                foreach (var m in ix.Methods)
                    foreach (var cs in ix.Calls(m))
                    {
                        if (cs.IsNew) continue;
                        List<KeyValuePair<MethodDecl, CallSite>> l;
                        if (!callersByName.TryGetValue(cs.Name, out l)) { l = new List<KeyValuePair<MethodDecl, CallSite>>(); callersByName[cs.Name] = l; }
                        l.Add(new KeyValuePair<MethodDecl, CallSite>(m, cs));
                    }
            }
            List<KeyValuePair<MethodDecl, CallSite>> callers;
            if (target.IsSynthetic || !callersByName.TryGetValue(target.Name, out callers)) { r.Add(""); return r; }
            bool ext = target.IsExtension;
            foreach (var kv in callers)
            {
                var caller = kv.Key; var cs = kv.Value;
                if (caller == target) continue;
                if (cs.IsMethodGroup) continue;
                bool fits = true;
                int argc = cs.Argc;
                int expected = target.Params.Count - (ext && cs.Receiver.Count > 0 ? 1 : 0);
                if (argc >= 0 && argc > expected && !target.Params.Any(p => p.IsParams)) fits = false;
                if (!fits) continue;
                if (ext && paramIndex == 0 && cs.Receiver.Count > 0)
                {
                    r.AddRange(ReceiverPrefixes(caller, cs.Receiver, depth + 1));
                    continue;
                }
                int argPos = paramIndex - (ext && cs.Receiver.Count > 0 ? 1 : 0);
                if (cs.ArgOpen < 0) continue;
                var args = SplitArgs(caller.File, cs.ArgOpen);
                if (argPos < 0 || argPos >= args.Count) continue;
                var segs = ix.ParseChainForward(caller.File, args[argPos][0]);
                r.AddRange(ReceiverPrefixes(caller, segs, depth + 1));
            }
            if (r.Count == 0) r.Add("");
            return r.Distinct().ToList();
        }

        // ------------------------------------------------------------ FastEndpoints
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
                        for (int q = cs.ArgOpen; q < conf.File.Match[cs.ArgOpen]; q++)
                            if (IsI(t, q) && Regex.IsMatch(t[q].Text, "^(GET|POST|PUT|DELETE|PATCH|Get|Post|Put|Delete|Patch)$")) verbs.Add(t[q].Text.ToUpperInvariant());
                }
                if (routes.Count == 0) continue;
                if (verbs.Count == 0) verbs.Add("ANY");
                var extra = new List<Marker>();
                foreach (var c in td.Leading) foreach (var h in sm.FindInText(c.Text)) extra.Add(fx.NewMarker(h.Sp, new Location(td.File, c.Line), "endpoint-comment", c.Text));
                foreach (var v in verbs.Distinct()) foreach (var rt in routes.Distinct())
                    {
                        var ep = new Endpoint { Verb = v, Route = Normalize(rt), Kind = "FastEndpoints", Handler = handler, HandlerName = td.Name + "." + (handler != null ? handler.Name : "?"), File = td.File, Line = td.Line };
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
                if (!md.HasBody) continue;
                foreach (var p in md.Params)
                {
                    var trig = p.Attrs.FirstOrDefault(a => a.Name == "HttpTrigger");
                    if (trig == null) continue;
                    var verbs = new List<string>();
                    for (int i = 0; i < trig.Positional.Count; i++) if (trig.PositionalIsString[i]) verbs.Add(trig.Positional[i].ToUpperInvariant());
                    if (verbs.Count == 0) verbs.Add("ANY");
                    string route; trig.Named.TryGetValue("Route", out route);
                    route = route != null ? route.Trim('"') : null;
                    if (route == null)
                    {
                        var fa = md.Attrs.FirstOrDefault(a => a.Name == "Function" || a.Name == "FunctionName");
                        route = fa != null && FirstString(fa) != null ? FirstString(fa) : md.Name;
                    }
                    foreach (var v in verbs)
                        eps.Add(new Endpoint { Verb = v, Route = Normalize(Join("api", route)), Kind = "AzureFunction", Handler = md, HandlerName = md.DisplayName, File = md.File, Line = md.Line });
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
            sf.Text = File.ReadAllText(path);
            sf.LineStarts = SourceFile.ComputeLineStarts(sf.Text);
            return sf;
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
            Ix.Build(types, methods);
            Res.FilesCs = Ix.Files.Count; Res.FilesSql = Ix.SqlFiles.Count; Res.Types = Ix.Types.Count; Res.Methods = Ix.Methods.Count;
            Fx = new FragmentExtractor(Ix, Sm);
            Fx.RegisterCommentFiles(Ix.Files);

            var finder = new EndpointFinder(Ix, Fx, Sm, Res);
            Res.Endpoints = finder.Find();

            // pasada global: catalogo de SP migrados en todo el repo (sin marcadores heredados)
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
                if (ep.Handler != null)
                {
                    var visited = new HashSet<string>();
                    Dfs(ep, ep.Handler, ep.ExtraMarkers, "ep", 0, new List<string>(), false, visited, items, markers, used, visitedAll, trace, unresolved);
                }
                foreach (var it in items) AddToCatalog(it.Fr, it.Dec);
                perEp[ep.Id] = items; epMarkers[ep.Id] = markers; epUsed[ep.Id] = used;
                Res.EndpointTraces[ep.Id] = trace;
                foreach (var u in unresolved)
                    Res.Warnings.Add(new Warn { Category = "Llamada no resuelta", Message = u, EndpointDisplay = ep.Display });
            }
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
        void Dfs(Endpoint ep, MethodDecl m, List<Marker> inherited, string inhKey, int depth, List<string> path, bool inferredPath,
                 HashSet<string> visited, List<RawItem> items, List<Marker> markers, HashSet<string> used, HashSet<string> visitedAll,
                 List<string> trace, HashSet<string> unresolved)
        {
            if (m == null || depth > Opt.MaxDepth) return;
            string key = m.Id + "|" + inhKey;
            if (!visited.Add(key)) return;
            visitedAll.Add(m.Id);
            var ma = Fx.Analyze(m);
            var p2 = new List<string>(path); p2.Add(m.DisplayName + (inferredPath ? "*" : ""));
            string traceStr = string.Join(" -> ", p2.ToArray());
            trace.Add(new string(' ', depth * 2) + m.DisplayName + " (" + m.File.Rel + ":" + m.Line + ")" + (inferredPath ? " [inferido]" : ""));
            markers.AddRange(ma.MethodMarkers);
            foreach (var b in ma.Blocks) markers.AddRange(b.Markers);
            foreach (var fr in ma.Fragments)
            {
                if (fr.MessageContext) continue;
                var dec = Decide(fr, ma, inherited);
                markers.AddRange(fr.InlineMarkers); markers.AddRange(fr.ConstMarkers);
                foreach (var u in dec.Used) used.Add(u.Id);
                items.Add(new RawItem { Fr = fr, Dec = dec, Trace = traceStr, InferredPath = inferredPath });
                if (fr.HasDynamicSp)
                    Res.Warnings.Add(new Warn { Category = "SP dinámico", Message = "Nombre de SP armado en tiempo de ejecución (no se puede resolver): " + U.OneLine(fr.Value, 120), Loc = new Location(fr.File, fr.Line), EndpointDisplay = ep.Display });
                if (dec.Level == "inherited" && dec.Migrated.Count > 1)
                    Res.Warnings.Add(new Warn { Category = "Asociación ambigua", Message = "La query recibe varios SP desde comentarios de un método llamador: " + string.Join(", ", dec.Migrated.Select(x => x.Sp.Display).ToArray()), Loc = QueryLoc(fr), EndpointDisplay = ep.Display });
            }
            List<Marker> nextInh = ma.MethodMarkers.Count > 0 ? ma.MethodMarkers : inherited;
            string nextKey = ma.MethodMarkers.Count > 0 ? m.Id : inhKey;
            var targets = new List<KeyValuePair<MethodDecl, bool>>();
            foreach (var cs in Ix.Calls(m))
            {
                if (cs.IsNew) continue;
                bool inf, unr;
                var tg = Ix.ResolveCall(m, cs, out inf, out unr);
                if (unr && tg.Count == 0)
                {
                    // solo se advierte si alguno de los posibles destinos puede llegar a un SP
                    List<MethodDecl> cands;
                    if (Ix.MethodsByName.TryGetValue(cs.Name, out cands))
                    {
                        var risky = Ix.FilterArgc(cands.Where(x => !x.IsExtension).ToList(), cs.Argc, false).Where(x => CanReachSp(x, new HashSet<string>())).ToList();
                        if (risky.Count > 0)
                        {
                            string recv = string.Join(".", cs.Receiver.Select(x => x.Name).ToArray());
                            unresolved.Add((recv.Length > 0 ? recv + "." : "") + cs.Name + "() en " + m.DisplayName + " (" + m.File.Rel + ":" + cs.Line + "). Posibles destinos con SP: "
                                + string.Join(", ", risky.Take(6).Select(x => x.DisplayName).ToArray()) + (risky.Count > 6 ? " y " + (risky.Count - 6) + " más" : ""));
                        }
                    }
                }
                foreach (var t in tg) if (t != m) targets.Add(new KeyValuePair<MethodDecl, bool>(t, inf));
            }
            foreach (var h in Ix.LinkedHandlers(m)) targets.Add(new KeyValuePair<MethodDecl, bool>(h, false));
            foreach (var kv in targets)
            {
                if (!kv.Key.HasBody) continue;
                Dfs(ep, kv.Key, nextInh, nextKey, depth + 1, p2, inferredPath || kv.Value, visited, items, markers, used, visitedAll, trace, unresolved);
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
            if (!r) foreach (var h in Ix.LinkedHandlers(m)) if (CanReachSp(h, stack)) { r = true; break; }
            stack.Remove(m.Id);
            reachMemo[m.Id] = r;
            return r;
        }

        // ------------------------------------------------------------ filas
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
                        r.Detail = (fr.Origin == "config" ? "valor de configuración en " : fr.Origin == "resx" ? "recurso en " : "constante definida en ") + h.Loc.Key;
                        r.Loc = new Location(fr.File, fr.Line);
                    }
                    else if (fr.Origin == "sqlfile") r.Detail = "archivo .sql usado en " + fr.File.Rel + ":" + fr.Line;
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
                add(new ResultRow { Ep = ep, Sp = mk.Sp, Tipo = "SOLO_COMENTARIO", Loc = mk.Loc, Detail = MarkerDetail(mk), Trace = "" });
            }
            Res.Rows.AddRange(rows);
            if (rows.Count == 0)
            {
                int q = items.Count;
                Res.EndpointNoSpReason[ep.Id] = ep.Handler == null ? "Handler no resuelto" : q > 0 ? "Ejecuta " + q + " consulta(s) SQL sin SP asociado" : "No se detectó acceso a datos ni SP en el flujo";
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
                    if (Fx.ReferencedConsts.Contains(mv.Id)) continue;
                    string v = Ix.ConstValue(mv);
                    if (v == null) continue;
                    var scan = SqlScan.Scan(v);
                    foreach (var h in Sm.FindInCode(scan.Blank, scan.Kind == "bare"))
                        Res.Orphans.Add(new OrphanRef { Sp = h.Sp, Loc = new Location(mv.File, mv.Line), Context = td.Name + "." + mv.Name, Kind = "codigo", Detail = "constante no referenciada" });
                }
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
            foreach (Match mm in Regex.Matches(sf.Text, @"<data\s+name=""(?<k>[^""]+)""[^>]*>\s*<value>(?<v>[\s\S]*?)</value>"))
            {
                string v = System.Net.WebUtility.HtmlDecode(mm.Groups["v"].Value);
                var e = new ResxEntry { FileBase = name, Key = mm.Groups["k"].Value, Value = v, File = sf, Line = sf.LineOf(mm.Groups["v"].Index) };
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
W '  3. comentario previo en el mismo método;'
W '  4. XML doc, comentarios o atributos del método;'
W '  5. comentario de un método llamador (&dagger;);'
W '  6. comentario de la clase, solo si nombra un único SP.'
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
