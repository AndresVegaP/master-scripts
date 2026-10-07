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
