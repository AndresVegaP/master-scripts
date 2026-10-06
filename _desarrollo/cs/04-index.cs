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
        public Dictionary<string, List<TypeDecl>> NestedByOuter = new Dictionary<string, List<TypeDecl>>();
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
                        List<TypeDecl> nl;
                        var nested = NestedByOuter.TryGetValue(td.Id, out nl) ? nl.Where(x => x.Name == s.Name).ToList() : new List<TypeDecl>();
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
