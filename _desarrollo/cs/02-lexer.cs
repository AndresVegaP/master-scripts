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
