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
