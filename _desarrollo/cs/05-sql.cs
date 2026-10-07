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
