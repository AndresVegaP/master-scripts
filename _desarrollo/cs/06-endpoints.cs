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
                if (a.PositionalIsString[i]) return a.Positional[i];
                if (i < a.PosArgs.Count) { var v = ArgString(a.PosArgs[i], ctx); if (v != null) return v; }
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
                        if (Attr(md.Attrs, "NonAction") != null) continue;
                        if (md.Name == "Dispose" || md.Name == "ToString" || md.Name == "Equals" || md.Name == "GetHashCode") continue;
                        string sig = md.Name + "/" + md.Params.Count;
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
