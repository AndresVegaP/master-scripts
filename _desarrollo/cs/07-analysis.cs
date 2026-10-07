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
