using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using static TouchTypingStudio.NotebookPythonSyntax;

namespace TouchTypingStudio;

internal sealed class NotebookPythonKernel
{
    internal sealed record Execution(string Source, IReadOnlyDictionary<string, object> Dependencies,
        IReadOnlyDictionary<string, string> Files, string Result, string Stream, NotebookPythonException Error);
    private sealed record Stored(object Data, Dictionary<string, string> Files = null);
    private sealed record Function(string Name, string[] Parameters, Statement[] Body, Module Module);
    private sealed record Builtin(string Name);
    private sealed record CsvFile(string Name, string Body);
    private sealed record Module(string Path, string Source, Dictionary<string, Stored> Fields);
    private readonly Dictionary<string, Stored> names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Module> loaded = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string[]> Standard = new(StringComparer.Ordinal) {
        ["statistics"] = ["mean", "median"], ["math"] = ["ceil", "floor"], ["csv"] = ["DictReader", "reader"]
    };
    private static readonly HashSet<string> Builtins = ["sum", "max", "min", "len", "round", "abs", "int", "float", "str", "open", "print"];
    private const int MaxColumns = 24, MaxRows = 128, MaxItems = 128, MaxFile = 65_536;
    private static readonly CultureInfo FormatCulture = CultureInfo.InvariantCulture;
    private static readonly object Missing = new();

    private sealed class Evaluation(Func<string, string> read)
    {
        internal Func<string, string> Read = read;
        internal readonly HashSet<string> Loading = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, object> Dependencies = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, string> Files = new(StringComparer.Ordinal);
        internal readonly StringBuilder Stream = new();
        internal int Steps, Depth;
        internal void Step()
        {
            if (++Steps > 24_000) throw NotebookPythonException.Unsupported("Достигнут лимит вычислений учебной ячейки.");
        }
    }
    private sealed record Scope(Dictionary<string, Stored> Names, Module Module = null, Scope Outer = null);

    internal void Reset() { names.Clear(); loaded.Clear(); }
    internal NotebookPythonKernel Clone()
    {
        var copy = new NotebookPythonKernel();
        foreach (var entry in names) copy.names.Add(entry.Key, entry.Value);
        foreach (var entry in loaded) copy.loaded.Add(entry.Key, entry.Value);
        return copy;
    }

    internal Execution Run(string source, Func<string, string> read)
    {
        var ctx = new Evaluation(read);
        string output = null;
        NotebookPythonException error = null;
        try
        {
            var scope = new Scope(names);
            var program = NotebookPythonSyntax.Parse(source);
            for (int i = 0; i < program.Length; i++)
            {
                var action = program[i];
                var value = StatementValue(action, scope, ctx);
                if (action is Display && i == program.Length - 1 && value != null) output = Render(value);
            }
        }
        catch (NotebookPythonException ex) { error = ex; }
        return new Execution(source, ctx.Dependencies, ctx.Files, output, ctx.Stream.ToString(), error);
    }

    internal bool Fresh(Execution execution, string source, Func<string, string> read)
    {
        if (source != execution.Source) return false;
        foreach (var (name, value) in execution.Dependencies)
        {
            int budget = 1024;
            if (!Same(names.TryGetValue(name, out var current) ? current.Data : Missing, value, ref budget)) return false;
        }
        // Rendering need not know the scene filesystem. The scene calls this
        // once before taking its snapshot and when scoring a saved artifact.
        if (read != null)
            foreach (var (path, used) in execution.Files)
                if (read(path) != used) return false;
        return true;
    }

    private static bool Same(object a, object b, ref int budget)
    {
        if (--budget < 0) return false;
        if (ReferenceEquals(a, b)) return true;
        if (a is object[] al && b is object[] bl && al.Length == bl.Length)
        {
            for (int i = 0; i < al.Length; i++)
                if (!Same(al[i], bl[i], ref budget)) return false;
            return true;
        }
        if (a is Dictionary<string, object> ad && b is Dictionary<string, object> bd && ad.Count == bd.Count)
        {
            foreach (var (key, value) in ad)
                if (!bd.TryGetValue(key, out var other) || !Same(value, other, ref budget)) return false;
            return true;
        }
        return a is null or double or string or bool && Equals(a, b);
    }

    private static void AddFiles(Evaluation ctx, Dictionary<string, string> files)
    {
        if (files == null) return;
        foreach (var (path, body) in files)
            RecordFile(ctx, path, body);
    }

    private static void RecordFile(Evaluation ctx, string path, string source)
    {
        if (ctx.Files.Count >= 24 && !ctx.Files.ContainsKey(path))
            throw NotebookPythonException.Unsupported("В ячейке допустимо до 24 учебных файлов.");
        // If a caller has retained something from an older module/input, do
        // not accidentally relabel that value using a freshly read path.
        ctx.Files.TryAdd(path, source);
    }
    private object Lookup(string name, Scope scope, Evaluation ctx)
    {
        ctx.Step();
        for (var current = scope; current != null; current = current.Outer)
            if (current.Names.TryGetValue(name, out var local))
            {
                if (ReferenceEquals(current.Names, names)) ctx.Dependencies[name] = local.Data;
                AddFiles(ctx, local.Files);
                return local.Data;
            }
        if (scope.Module?.Fields.TryGetValue(name, out var member) == true)
        { AddFiles(ctx, member.Files); return member.Data; }
        if (scope.Module == null && names.TryGetValue(name, out var global))
        { ctx.Dependencies[name] = global.Data; AddFiles(ctx, global.Files); return global.Data; }
        if (Builtins.Contains(name)) return new Builtin(name);
        if (scope.Module == null) ctx.Dependencies.TryAdd(name, Missing);
        throw new NotebookPythonException("NameError", "Имя «" + name + "» не определено; выражение не выполняется. Выполните ячейку с данными или импортом.");
    }

    private void Assign(Scope scope, string name, object value, Evaluation ctx)
    {
        if (scope.Names.Count >= 96 && !scope.Names.ContainsKey(name))
            throw NotebookPythonException.Unsupported("В этом сеансе слишком много переменных. Перезапустите ядро.");
        var global = ReferenceEquals(scope.Names, names);
        scope.Names[name] = new Stored(value, new Dictionary<string, string>(ctx.Files, StringComparer.Ordinal));
        if (global) ctx.Dependencies[name] = value;
    }

    private object StatementValue(Statement action, Scope scope, Evaluation ctx)
    {
        ctx.Step();
        try
        {
            switch (action)
            {
                case Display d: return Evaluate(d.Value, scope, ctx);
                case Return r: return Evaluate(r.Value, scope, ctx);
                case Let a: Assign(scope, a.Target, Evaluate(a.Value, scope, ctx), ctx); break;
                case Define f: Assign(scope, f.Function, new Function(f.Function, f.Parameters, f.Body, scope.Module), ctx); break;
                case Import m:
                    var module = ImportModule(m.Module, ctx);
                    foreach (var (member, alias) in m.Names)
                        Assign(scope, alias, member == null ? module : ModuleMember(module, member, ctx), ctx);
                    break;
            }
            return null;
        }
        catch (NotebookPythonException ex) { throw ex.At((scope.Module?.Path ?? "Cell") + ", line " + action.Line); }
    }

    private Module ImportModule(string name, Evaluation ctx)
    {
        if (Standard.TryGetValue(name, out var members))
            return new Module(null, null, members.ToDictionary(m => m, m => new Stored(new Builtin(name + "." + m))));
        string path = name.Replace('.', '/') + ".py";
        if (loaded.TryGetValue(path, out var cached))
        { RecordFile(ctx, path, cached.Source); return cached; }
        if (loaded.Count >= 16 || ctx.Loading.Count >= 8)
            throw NotebookPythonException.Unsupported("Слишком много вложенных учебных импортов.");
        if (!ctx.Loading.Add(path)) throw new NotebookPythonException("ImportError", "Циклический импорт «" + path + "».");
        try
        {
            string source = ReadVirtual(ctx, path, ".py", allowMissing: true);
            if (source == null) throw new NotebookPythonException("ModuleNotFoundError",
                "«" + name + "» отсутствует в учебном проекте. Встроены csv, statistics и math.");
            var module = new Module(path, source, new Dictionary<string, Stored>(StringComparer.Ordinal));
            foreach (var action in NotebookPythonSyntax.Parse(source)) StatementValue(action, new Scope(module.Fields, module), ctx);
            loaded.Add(path, module);
            return module;
        }
        catch (NotebookPythonException ex) { throw ex.At(path); }
        finally { ctx.Loading.Remove(path); }
    }

    private static object ModuleMember(Module module, string member, Evaluation ctx)
    {
        if (module.Path != null) RecordFile(ctx, module.Path, module.Source);
        if (!module.Fields.TryGetValue(member, out var value))
            throw new NotebookPythonException("ImportError", "Не найдено имя «" + member + "» в запрошенном модуле.");
        AddFiles(ctx, value.Files);
        return value.Data;
    }

    private object Evaluate(Expr expression, Scope scope, Evaluation ctx)
    {
        ctx.Step();
        return expression switch
        {
            Literal value => value.Value,
            Name n => Lookup(n.Value, scope, ctx),
            Member m => Evaluate(m.Value, scope, ctx) is Module mod
                ? ModuleMember(mod, m.Field, ctx) : throw Type("Атрибуты доступны только у учебных модулей."),
            NotebookPythonSyntax.Index item => Subscript(Evaluate(item.Value, scope, ctx), Evaluate(item.Position, scope, ctx)),
            Unary one => UnaryValue(one.Operator, Evaluate(one.Inner, scope, ctx)),
            Binary two => BinaryValue(two.Operator, Evaluate(two.Left, scope, ctx), Evaluate(two.Right, scope, ctx)),
            NotebookPythonSyntax.List seq when seq.Items.Length <= MaxItems => seq.Items.Select(item => Evaluate(item, scope, ctx)).ToArray(),
            Comprehension loop => Iterate(loop, scope, ctx),
            Call call => Invoke(Evaluate(call.Function, scope, ctx), call.Arguments.Select(x => Evaluate(x, scope, ctx)).ToArray(), ctx),
            _ => throw NotebookPythonException.Unsupported("Эта операция пока не поддержана учебным ядром.")
        };
    }

    private object[] Iterate(Comprehension loop, Scope scope, Evaluation ctx)
    {
        var vector = AsList(Evaluate(loop.Items, scope, ctx));
        var result = new List<object>();
        var local = new Dictionary<string, Stored>(StringComparer.Ordinal);
        // In a comprehension only the loop name shadows outer scope. Reads
        // of any other notebook name still contribute dependency evidence.
        var nested = new Scope(local, scope.Module, scope);
        foreach (var item in vector)
        {
            local[loop.Variable] = new Stored(item);
            if (loop.Filter == null || Truth(Evaluate(loop.Filter, nested, ctx)))
                result.Add(Evaluate(loop.Item, nested, ctx));
        }
        return result.ToArray();
    }

    private object Invoke(object candidate, object[] args, Evaluation ctx)
    {
        if (++ctx.Depth > 12) throw NotebookPythonException.Unsupported("Слишком глубокий вызов функции.");
        try
        {
            if (candidate is Builtin method) return CallBuiltin(method.Name, args, ctx);
            if (candidate is not Function function) throw Type("Это значение нельзя вызвать как функцию.");
            if (args.Length != function.Parameters.Length) throw Type("Функция ожидает аргументов: " + function.Parameters.Length);
            if (function.Module?.Path is string path) RecordFile(ctx, path, function.Module.Source);
            var local = new Dictionary<string, Stored>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++) local.Add(function.Parameters[i], new Stored(args[i]));
            foreach (var statement in function.Body)
            {
                var value = StatementValue(statement, new Scope(local, function.Module), ctx);
                if (statement is Return) return value;
            }
            return null;
        }
        finally { ctx.Depth--; }
    }

    private static object CallBuiltin(string name, object[] args, Evaluation ctx)
    {
        if (name == "print")
        {
            var line = string.Join(" ", args.Select(value => Render(value, unquoted: true)));
            if (ctx.Stream.Length + line.Length + 1 > 8192)
                throw NotebookPythonException.Unsupported("Вывод ячейки превышает 8 КБ.");
            ctx.Stream.Append(line).Append('\n');
            return null;
        }
        if (name == "round")
        {
            if (args.Length is < 1 or > 2) throw Type("round принимает число и необязательную точность.");
            int places = args.Length == 2 ? Integral(args[1]) : 0;
            if (places is < 0 or > 12) throw Type("Точность round в учебном ядре: от 0 до 12.");
            return Math.Round(Number(args[0]), places);
        }
        if (name == "open")
        {
            if (args.Length is < 1 or > 2 || args[0] is not string path || args.Length == 2 && args[1] is not ("r" or "rt"))
                throw Type("open здесь читает только учебный CSV: open('data/report.csv').");
            return new CsvFile(path, ReadVirtual(ctx, path, ".csv"));
        }
        if (args.Length != 1) throw Type(name + " ожидает один аргумент.");
        object value = args[0];
        return name switch
        {
            "len" => (double)(value switch {
                object[] a => a.Length, string str => str.Length, Dictionary<string, object> map => map.Count, _ => throw Type("len требует список, строку или строку CSV.")
            }),
            "int" => Math.Truncate(FromNumber(value)), "float" => FromNumber(value),
            "str" => Render(value, unquoted: true),
            "abs" => Math.Abs(Number(value)), "math.ceil" => Math.Ceiling(Number(value)), "math.floor" => Math.Floor(Number(value)),
            "csv.DictReader" or "csv.reader" => value is CsvFile csv ? ParseCsv(csv, name.EndsWith("DictReader", StringComparison.Ordinal), ctx)
                : throw Type("Передайте учебный CSV: DictReader(open('data/report.csv'))."),
            "sum" or "max" or "min" or "statistics.mean" or "statistics.median" => Aggregate(name, AsList(value)),
            _ => throw NotebookPythonException.Unsupported("Эта функция недоступна в учебном ядре.")
        };
    }

    private static double Aggregate(string name, object[] items)
    {
        if (name == "sum" && items.Length == 0) return 0;
        if (items.Length == 0) throw new NotebookPythonException(name.StartsWith("statistics.", StringComparison.Ordinal) ? "StatisticsError" : "ValueError",
            "Нет чисел для расчёта.");
        var numbers = items.Select(Number).ToArray();
        if (name == "statistics.median")
        {
            Array.Sort(numbers);
            return numbers.Length % 2 != 0 ? numbers[numbers.Length / 2] :
                Finite((numbers[numbers.Length / 2 - 1] + numbers[numbers.Length / 2]) / 2);
        }
        return Finite(name switch {
            "min" => numbers.Min(), "max" => numbers.Max(), "statistics.mean" => numbers.Average(), _ => numbers.Sum()
        });
    }

    private static object[] ParseCsv(CsvFile file, bool dictionaries, Evaluation ctx)
    {
        var records = new List<string[]>();
        var row = new List<string>(); var value = new StringBuilder();
        bool quoted = false, closedQuote = false;
        void Field()
        {
            if (row.Count >= MaxColumns) throw new NotebookPythonException("ValueError", "Слишком много колонок CSV.");
            row.Add(value.ToString()); value.Clear(); closedQuote = false;
        }
        void Row()
        {
            if (records.Count >= MaxRows + 1) throw NotebookPythonException.Unsupported("Допустимо до 128 строк данных CSV.");
            Field(); if (row.Count != 1 || row[0].Length != 0) records.Add(row.ToArray());
            row.Clear();
        }
        string body = file.Body.TrimStart('\ufeff');
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (quoted)
            {
                if (c != '"') value.Append(c);
                else if (i + 1 < body.Length && body[i + 1] == '"') { i++; value.Append('"'); }
                else { quoted = false; closedQuote = true; }
            }
            else if (c == ',') Field();
            else if (c is '\n' or '\r')
            { if (c == '\r' && i + 1 < body.Length && body[i + 1] == '\n') i++; Row(); }
            else if (c == '"' && value.Length == 0 && !closedQuote) quoted = true;
            else if (closedQuote || c == '"') throw new NotebookPythonException("ValueError", "Проверьте кавычки в CSV.");
            else value.Append(c);
            if (value.Length > 2048) throw NotebookPythonException.Unsupported("Поле CSV превышает 2048 знаков.");
        }
        if (quoted) throw new NotebookPythonException("ValueError", "В CSV не закрыта кавычка.");
        if (row.Count > 0 || value.Length > 0 || closedQuote) Row();
        if (!dictionaries) return records.Select(r => (object)r.Cast<object>().ToArray()).ToArray();
        if (records.Count == 0) return [];
        string[] header = records[0];
        if (header.Any(string.IsNullOrEmpty) || header.Distinct().Count() != header.Length)
            throw new NotebookPythonException("ValueError", "Нужны уникальные непустые заголовки CSV.");
        return records.Skip(1).Select(r =>
        {
            ctx.Step();
            if (r.Length != header.Length) throw new NotebookPythonException("ValueError", "Число полей CSV не совпадает с заголовком.");
            return (object)header.Zip(r).ToDictionary(x => x.First, x => (object)x.Second, StringComparer.Ordinal);
        }).ToArray();
    }

    private static string ReadVirtual(Evaluation ctx, string path, string extension, bool allowMissing = false)
    {
        if (path.Length is < 1 or > 180 || !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ||
            path.Split('/').Any(p => p.Length == 0 || p is "." or ".." ||
                !p.All(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '.' or '-' or '_')))
            throw new NotebookPythonException("PermissionError", "Доступны только относительные пути в текущем учебном проекте.");
        string body = ctx.Read?.Invoke(path);
        if (body?.Length > MaxFile) throw NotebookPythonException.Unsupported("Учебный файл превышает 64 КБ.");
        RecordFile(ctx, path, body);
        if (body == null && !allowMissing) throw new NotebookPythonException("FileNotFoundError", "Файл проекта «" + path + "» не найден.");
        return body;
    }

    private static object Subscript(object value, object offset)
    {
        if (value is Dictionary<string, object> map && offset is string key)
            return map.TryGetValue(key, out var member) ? member :
                throw new NotebookPythonException("KeyError", "В CSV нет колонки «" + key + "».");
        if (value is not (object[] or string)) throw Type("Индекс доступен для списка, строки или записи CSV.");
        int i = Integral(offset), length = value is object[] array ? array.Length : ((string)value).Length;
        if (i < 0) i += length;
        if (i < 0 || i >= length) throw new NotebookPythonException("IndexError", "Индекс вне диапазона.");
        return value is object[] arr ? arr[i] : ((string)value)[i].ToString();
    }

    private static object UnaryValue(string op, object value) => op switch {
        "not" => !Truth(value), "+" => Number(value), _ => -Number(value)
    };
    private static object BinaryValue(string op, object a, object b)
    {
        if (op is "==" or "!=")
        {
            int budget = 1024;
            return Same(a, b, ref budget) == (op == "==");
        }
        if (op == "+" && a is string first && b is string second)
        {
            if (first.Length + second.Length > 4096) throw NotebookPythonException.Unsupported("Слишком длинная строка.");
            return first + second;
        }
        double x = Number(a), y = Number(b);
        if (op is "/" or "//" or "%" && y == 0) throw new NotebookPythonException("ZeroDivisionError", "Деление на ноль.");
        return op switch
        {
            ">" => x > y, "<" => x < y, ">=" => x >= y, "<=" => x <= y,
            _ => Finite(op switch {
                "+" => x + y, "-" => x - y, "*" => x * y, "/" => x / y, "//" => Math.Floor(x / y),
                "%" => x - Math.Floor(x / y) * y, _ => Math.Pow(x, y)
            })
        };
    }

    private static string Render(object obj, bool unquoted = false)
    {
        var output = new StringBuilder();
        void Add(object value, int depth, bool plain)
        {
            if (output.Length > 3800 || depth > 6) { if (output.Length <= 3800) output.Append('…'); return; }
            if (value is object[] arr)
            {
                output.Append('[');
                for (int i = 0; i < arr.Length && output.Length <= 3800; i++)
                {
                    if (i != 0) output.Append(", ");
                    Add(arr[i], depth + 1, false);
                }
                output.Append(']');
            }
            else if (value is Dictionary<string, object> row)
            {
                output.Append('{');
                bool first = true;
                foreach (var (key, field) in row)
                {
                    if (output.Length > 3800) break;
                    if (!first) output.Append(", ");
                    first = false;
                    Add(key, depth + 1, false); output.Append(": "); Add(field, depth + 1, false);
                }
                output.Append('}');
            }
            else output.Append(value switch {
                double n => n.ToString("G12", FormatCulture), string s => plain ? s : "'" + s.Replace("'", "\\'").Replace("\n", "\\n") + "'",
                bool b => b ? "True" : "False", null => "None", Function f => "<function " + f.Name + ">",
                Builtin fn => "<built-in " + fn.Name + ">", Module m => "<module " + (m.Path ?? "stdlib") + ">",
                CsvFile file => "<CSV " + file.Name + ">", _ => ""
            });
        }
        Add(obj, 0, unquoted);
        return output.Length > 4096 ? output.ToString(0, 4095) + "…" : output.ToString();
    }
    private static object[] AsList(object value) => value as object[] ?? throw Type("Нужен список.");
    private static double Number(object value) => value is double d ? d : value is bool b ? (b ? 1 : 0) : throw Type("Нужно число.");
    private static double FromNumber(object value) => value is string str
        ? double.TryParse(str, NumberStyles.Float, FormatCulture, out double parsed) && double.IsFinite(parsed) ? parsed :
            throw new NotebookPythonException("ValueError", "«" + str + "» не является числом.") : Number(value);
    private static int Integral(object value) => Number(value) is double d && double.IsInteger(d) && d is >= int.MinValue and <= int.MaxValue
        ? (int)d : throw Type("Нужно целое число в учебном диапазоне.");
    private static double Finite(double value) => double.IsFinite(value) ? value :
        throw new NotebookPythonException("OverflowError", "Результат вышел за диапазон учебного ядра.");
    private static bool Truth(object value) => value switch {
        null => false, bool b => b, double n => n != 0, string s => s.Length != 0, object[] a => a.Length != 0,
        Dictionary<string, object> row => row.Count != 0, _ => true
    };
    private static NotebookPythonException Type(string text) => new("TypeError", text);
}
