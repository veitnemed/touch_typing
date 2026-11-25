using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TouchTypingStudio;

// Parser for a bounded, deliberately small Python-compatible teaching subset.
// It produces our own inert syntax nodes; none of the source is passed to a
// host language runtime, shell, compiler or external kernel.
internal static class NotebookPythonSyntax
{
    internal abstract record Expr;
    internal sealed record Literal(object Value) : Expr;
    internal sealed record Name(string Value) : Expr;
    internal sealed record Member(Expr Value, string Field) : Expr;
    internal sealed record Index(Expr Value, Expr Position) : Expr;
    internal sealed record Unary(string Operator, Expr Inner) : Expr;
    internal sealed record Binary(string Operator, Expr Left, Expr Right) : Expr;
    internal sealed record List(Expr[] Items) : Expr;
    internal sealed record Comprehension(Expr Item, string Variable, Expr Items, Expr Filter) : Expr;
    internal sealed record Call(Expr Function, Expr[] Arguments) : Expr;
    internal abstract record Statement(int Line);
    internal sealed record Let(int Line, string Target, Expr Value) : Statement(Line);
    internal sealed record Display(int Line, Expr Value) : Statement(Line);
    internal sealed record Return(int Line, Expr Value) : Statement(Line);
    internal sealed record Define(int Line, string Function, string[] Parameters, Statement[] Body) : Statement(Line);
    internal sealed record Import(int Line, string Module, (string Member, string Alias)[] Names) : Statement(Line);

    internal static Statement[] Parse(string code)
    {
        if (code.Length > 24_000) throw NotebookPythonException.Unsupported("Файл превышает лимит учебного ядра (24 КБ).");
        var lines = code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length > 160) throw NotebookPythonException.Unsupported("В учебном исходнике допускается до 160 строк.");
        var result = new List<Statement>();
        for (int i = 0; i < lines.Length; i++)
        {
            var source = lines[i];
            var parser = new Parser(source, i + 1);
            if (parser.End) continue;
            if (source.Length > 0 && char.IsWhiteSpace(source[0])) throw parser.Syntax("Неожиданный отступ.");
            if (!parser.Take("def"))
                result.Add(parser.Statement(inFunction: false));
            else
            {
                int definitionLine = i + 1;
                var function = parser.Identifier();
                parser.Require("(");
                var parameters = new List<string>();
                if (!parser.Take(")"))
                {
                    parameters.Add(parser.Identifier());
                    while (parser.Take(",") && !parser.Is(")")) parameters.Add(parser.Identifier());
                    parser.Require(")");
                }
                parser.Require(":"); parser.EndLine();
                if (parameters.Count > 8 || parameters.Distinct().Count() != parameters.Count) throw parser.Syntax("Некорректные параметры функции.");
                var body = new List<Statement>();
                while (i + 1 < lines.Length)
                {
                    string line = lines[i + 1];
                    var nested = new Parser(line, i + 2);
                    if (nested.End) { i++; continue; }
                    int indent = line.Length - line.TrimStart(' ').Length;
                    if (indent == 0) break;
                    if (indent != 4) throw nested.Syntax("В учебной функции используйте отступ в четыре пробела.");
                    i++;
                    body.Add(nested.Statement(inFunction: true));
                }
                if (body.Count == 0) throw parser.Syntax("Добавьте хотя бы одну строку в тело функции.");
                result.Add(new Define(definitionLine, function, parameters.ToArray(), body.ToArray()));
            }
        }
        return result.ToArray();
    }

    private enum Kind { End, Word, Number, Text, Symbol }
    private sealed record Token(Kind Kind, string Text);

    private sealed class Parser(string source, int line)
    {
        private readonly Token[] tokens = Lex(source, line);
        private int pos, depth, nodes;
        private Token Current => tokens[pos];
        private Token Next => tokens[Math.Min(pos + 1, tokens.Length - 1)];
        internal bool End => Current.Kind == Kind.End;
        internal bool Is(string word) => (Current.Kind is Kind.Symbol or Kind.Word) && Current.Text == word;
        internal bool Take(string word)
        {
            if (!Is(word)) return false;
            pos++; return true;
        }
        internal void Require(string word)
        {
            if (!Take(word)) throw Syntax("Ожидается «" + word + "».");
        }
        internal void EndLine()
        {
            if (!End) throw Syntax("Неподдерживаемая часть выражения: «" + Current.Text + "».");
        }
        internal NotebookPythonException Syntax(string detail) => new NotebookPythonException("SyntaxError", detail).At("line " + line);
        internal string Identifier()
        {
            if (Current.Kind != Kind.Word || Current.Text is "def" or "return" or "from" or "import" or "for" or "in" or "if" or "as" or
                "class" or "True" or "False" or "None" || Current.Text.StartsWith("__", StringComparison.Ordinal))
                throw Syntax("Ожидается учебное имя переменной, функции или модуля.");
            return tokens[pos++].Text;
        }
        internal Statement Statement(bool inFunction)
        {
            Statement s;
            if (Take("return"))
            {
                if (!inFunction) throw Syntax("Оператор return используется только внутри функции.");
                s = new Return(line, Expression());
            }
            else if (Take("import") || Is("from"))
            {
                bool from = Take("from");
                string module = Identifier();
                while (Take(".")) module += "." + Identifier();
                if (from)
                {
                    Require("import");
                    var names = new List<(string, string)>();
                    do
                    {
                        var name = Identifier();
                        names.Add((name, Take("as") ? Identifier() : name));
                    }
                    while (Take(","));
                    s = new Import(line, module, names.ToArray());
                }
                else
                {
                    bool alias = Take("as");
                    if (!alias && module.Contains('.')) throw Syntax("Для учебного вложенного модуля укажите псевдоним через as.");
                    s = new Import(line, module, [(null, alias ? Identifier() : module)]);
                }
            }
            else if (Current.Kind == Kind.Word && Next.Text == "=")
            {
                var id = Identifier(); Require("=");
                s = new Let(line, id, Expression());
            }
            else s = new Display(line, Expression());
            EndLine();
            return s;
        }

        private static int Precedence(string key) => key switch {
            "==" or "!=" or "<" or ">" or "<=" or ">=" => 1,
            "+" or "-" => 2,
            "*" or "/" or "//" or "%" => 3,
            "**" => 6, _ => -1
        };
        private void Allocate()
        {
            if (++nodes > 192) throw Syntax("Слишком сложное выражение для учебного ядра.");
        }
        private Expr Expression(int tier = 0)
        {
            Allocate();
            if (++depth > 22) throw Syntax("Слишком много вложенных выражений.");
            var left = Prefix();
            while (Current.Kind == Kind.Symbol)
            {
                string op = Current.Text;
                int p = Precedence(op);
                if (p < tier) break;
                pos++; Allocate();
                left = new Binary(op, left, Expression(p + (op == "**" ? 0 : 1)));
            }
            depth--;
            return left;
        }
        private Expr Prefix()
        {
            if (Take("+")) return new Unary("+", Expression(5));
            if (Take("-")) return new Unary("-", Expression(5));
            if (Take("not")) return new Unary("not", Expression(1));
            Expr atom;
            if (Take("(")) { atom = Expression(); Require(")"); }
            else if (Take("["))
            {
                if (Take("]")) atom = new List([]);
                else
                {
                    Expr first = Expression();
                    if (Take("for"))
                    {
                        var variable = Identifier(); Require("in");
                        var items = Expression();
                        Expr filter = Take("if") ? Expression() : null;
                        Require("]");
                        atom = new Comprehension(first, variable, items, filter);
                    }
                    else
                    {
                        var items = new List<Expr> { first };
                        while (Take(",") && !Is("]")) items.Add(Expression());
                        Require("]");
                        atom = new List(items.ToArray());
                    }
                }
            }
            else if (Take("None")) atom = new Literal(null);
            else if (Take("True")) atom = new Literal(true);
            else if (Take("False")) atom = new Literal(false);
            else if (Current.Kind == Kind.Number)
            {
                if (!double.TryParse(Current.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n))
                    throw Syntax("Число вне учебного диапазона.");
                pos++; atom = new Literal(n);
            }
            else if (Current.Kind == Kind.Text)
            {
                atom = new Literal(Current.Text); pos++;
            }
            else atom = new Name(Identifier());
            int calls = 0;
            while (!End)
            {
                if (Take(".")) atom = new Member(atom, Identifier());
                else if (Take("[")) { atom = new Index(atom, Expression()); Require("]"); }
                else if (Take("("))
                {
                    var args = new List<Expr>();
                    if (!Take(")"))
                    {
                        args.Add(Expression());
                        while (Take(",") && !Is(")")) args.Add(Expression());
                        Require(")");
                    }
                    atom = new Call(atom, args.ToArray());
                }
                else break;
                if (++calls > 16) throw Syntax("Слишком длинная цепочка обращений.");
                Allocate();
            }
            return atom;
        }
    }

    private static Token[] Lex(string text, int line)
    {
        var tokens = new List<Token>();
        NotebookPythonException Error(string message) => new NotebookPythonException("SyntaxError", message).At("line " + line);
        for (int i = 0; i < text.Length;)
        {
            char ch = text[i++];
            if (char.IsWhiteSpace(ch)) continue;
            if (ch == '#') break;
            if (tokens.Count >= 384) throw Error("Слишком длинная строка.");
            if (char.IsAsciiLetter(ch) || ch == '_')
            {
                int start = i - 1;
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_')) i++;
                if (i - start > 48) throw Error("Имя длиннее 48 знаков.");
                tokens.Add(new(Kind.Word, text[start..i]));
            }
            else if (char.IsAsciiDigit(ch))
            {
                int start = i - 1;
                while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                if (i + 1 < text.Length && text[i] == '.' && char.IsAsciiDigit(text[i + 1]))
                {
                    ++i; while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                }
                if (i - start > 18) throw Error("Слишком длинное число.");
                tokens.Add(new(Kind.Number, text[start..i]));
            }
            else if (ch is '\'' or '"')
            {
                var value = new StringBuilder();
                while (i < text.Length && text[i] != ch)
                {
                    char next = text[i++];
                    if (next == '\\')
                    {
                        if (i == text.Length) throw Error("Незавершённая escape-последовательность.");
                        next = text[i++] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '\'' => '\'', '"' => '"', '\\' => '\\',
                            _ => throw Error("Эта escape-последовательность не поддерживается в учебном ядре.") };
                    }
                    if (value.Length >= 2048) throw Error("Строка превышает 2048 знаков.");
                    value.Append(next);
                }
                if (i == text.Length) throw Error("Строка не закрыта кавычкой.");
                i++;
                tokens.Add(new(Kind.Text, value.ToString()));
            }
            else if ("=,.[]():+-*/%<>!".Contains(ch))
            {
                string op = ch.ToString();
                if (i < text.Length && (ch == '/' && text[i] == '/' || ch == '*' && text[i] == '*' ||
                        (ch is '<' or '>' or '=' or '!') && text[i] == '='))
                    op += text[i++];
                tokens.Add(new(Kind.Symbol, op));
            }
            else throw Error("Символ «" + ch + "» не поддерживается.");
        }
        tokens.Add(new(Kind.End, ""));
        return tokens.ToArray();
    }
}

internal sealed class NotebookPythonException(string name, string message) : Exception(message)
{
    internal string Name { get; } = name;
    internal readonly List<string> Trace = [];
    internal NotebookPythonException At(string site)
    {
        if (Trace.Count < 12 && (Trace.Count == 0 || Trace[^1] != site)) Trace.Add(site);
        return this;
    }
    internal static NotebookPythonException Unsupported(string message) => new("Учебное ядро", message);
}
