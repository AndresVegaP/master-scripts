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
