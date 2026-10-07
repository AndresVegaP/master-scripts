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
