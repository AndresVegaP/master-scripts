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
