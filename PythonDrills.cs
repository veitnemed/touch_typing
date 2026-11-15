using System;
using System.Collections.Generic;
using System.Globalization;

namespace TouchTypingStudio
{
    internal sealed class PythonDrillLevel
    {
        private readonly Func<Random, string> create;

        public readonly string Id, Title, Description;
        public readonly bool HasRepairs;

        public PythonDrillLevel(string id, string title, string description, Func<Random, string> create, bool hasRepairs = false)
        {
            Id = id;
            Title = title;
            Description = description;
            this.create = create;
            HasRepairs = hasRepairs;
        }

        public string Create(Random random) { return create(random); }
    }

    internal static class PythonDrills
    {
        // Repeated entries give frequent Python words more practice in each round.
        private static readonly string[] ShortWords =
        {
            "print", "len", "dict", "list", "int", "str", "range", "def", "for", "if", "in", "set",
            "sum", "min", "max", "type", "key", "value", "item", "name", "print", "len", "list",
            "dict", "range", "for", "value", "item"
        };

        private static readonly string[] Keywords =
        {
            "for item in range", "def get value", "return result", "if not value", "from path import path",
            "if count else", "for key in dict", "while index", "try except", "import math", "assert count",
            "return list", "in range", "if value", "for item in list", "return value"
        };

        private static readonly string[] Methods =
        {
            "items append value", "values extend items", "data get key", "data update values", "keys values items",
            "sorted values", "zip names values", "enumerate items", "list range count", "text split lines",
            "text strip lower", "path read bytes", "value replace text", "index count", "items append item"
        };

        private static readonly string[] MixedWords =
        {
            "def get item return value", "for index item in enumerate", "if key in data", "return dict items",
            "print len values", "from collections import defaultdict", "for name value in zip",
            "if not items return list", "return sorted values", "for item in range", "try read except",
            "def read text return result", "items append value", "data get key return value"
        };

        private static readonly string[] Names = { "count", "index", "total", "value", "size", "limit", "result" };
        private static readonly string[] Labels = { "item", "note", "name", "task", "score", "code", "entry" };

        public static readonly PythonDrillLevel[] WordLevels =
        {
            WordLevel("basics", "Короткие слова", "print, len, dict, list и другие частые слова Python.", ShortWords, 175),
            WordLevel("keywords", "Ключевые слова", "Слова из присваиваний, условий и циклов. Например, for item in range.", Keywords, 175),
            WordLevel("methods", "Методы и имена", "Частые движения из вызовов append, get, sorted, enumerate и других.", Methods, 185),
            WordLevel("mixed", "Слова из конструкций", "Перемешанные группы слов из функций, циклов, импорта и работы со словарями.", MixedWords, 200)
        };

        public static readonly PythonDrillLevel[] CodeLevels =
        {
            CodeLevel("calls", "Присваивания и вызовы", "Круглые скобки, цифры, кавычки, равно и простые вызовы.", 9, false,
                r => "print(" + Digit(r) + ")",
                r => Pick(r, Names) + " = " + Digit(r),
                r => "total = " + Digit(r) + " + " + Digit(r),
                r => "count = int('" + Digit(r) + "')",
                r => "label = '" + Pick(r, Labels) + Digit(r) + "'",
                r => "print(len('" + Pick(r, Labels) + "'))",
                r => "value = max(" + Digit(r) + ", " + Digit(r) + ")"),
            CodeLevel("collections", "Списки, словари и индексы", "Квадратные и фигурные скобки, запятые, индексы и пары ключ–значение.", 6, false,
                r => "items = [" + NumberPair(r) + "]\nprint(items[" + Index(r) + "])",
                r => "data = {'count': " + Digit(r) + ", 'size': " + Digit(r) + "}\nprint(data['" + Pick(r, new[] { "count", "size" }) + "'])",
                r => "scores = [" + NumberPair(r) + "]\nscores.append(" + Digit(r) + ")",
                r => "values = (" + NumberPair(r) + ")\nprint(sum(values))",
                r => "items = ['" + Pick(r, Labels) + "', '" + Pick(r, Labels) + "']\nprint(len(items))",
                r => "data = dict(count=" + Digit(r) + ", size=" + Digit(r) + ")\ncount = data.get('count', 0)"),
            CodeLevel("blocks", "Функции и условия", "Двоеточие, переносы строк и отступы в функциях, циклах и условиях.", 4, false,
                r => "def clamp(value):\n    return max(value, " + Digit(r) + ")\n\nprint(clamp(" + Digit(r) + "))",
                r => "for item in range(" + Digit(r) + "):\n    if item % 2 == 0:\n        print(item)",
                r => "def label(value):\n    if value >= " + Digit(r) + ":\n        return str(value)\n    return ''\n\nprint(label(" + Digit(r) + "))",
                r => "items = [" + NumberPair(r) + "]\nprint([item for item in items if item > " + Digit(r) + "])",
                r => "score = " + Digit(r) + "\nif score != " + Digit(r) + ":\n    print(f'score: {score}')"),
            CodeLevel("repair-calls", "Backspace в словах и вызовах", "Наберите лишнюю букву и удалите её по знаку ⌫. Например, prim⌫nt превращается в print.", 10, true,
                r => InsertMistake(InsertMistake("print(" + Digit(r) + ")", 5, "t"), 3, "m"),
                r => InsertMistake("print(len('" + Pick(r, Labels) + "'))", 7, "w"),
                r => InsertMistake("total = " + Digit(r) + " + " + Digit(r), 3, "l"),
                r => InsertMistake("count = int('" + Digit(r) + "')", 4, "r"),
                r => InsertMistake("value = " + Digit(r), 8, "="),
                r => InsertMistake("items = [" + NumberPair(r) + "]", 9, "["),
                r => InsertMistake("data = dict(size=" + Digit(r) + ")", 10, "s")),
            CodeLevel("repair-blocks", "Backspace в многострочном коде", "Исправляйте лишние буквы, числа и парные знаки. В некоторых местах Backspace нужно нажать дважды.", 5, true,
                r => InsertMistake("for item in range(" + Digit(r) + "):\n    print(item)", 4, "it"),
                r => InsertMistake("def scale(value):\n    return value * " + Digit(r), 20, "r"),
                r => InsertMistake("items = [" + NumberPair(r) + "]\nprint(items[" + Index(r) + "])", 8, "(("),
                r => InsertMistake("data = {'size': " + Digit(r) + "}\nprint(data['size'])", 14, "3"),
                r => InsertMistake("count = " + Digit(r) + "\nif count >= " + Digit(r) + ":\n    print(count)", 22, ">>"),
                r => InsertMistake("score = " + Digit(r) + "\nprint(f'score: {score}')", 16, "'"))
        };

        private static PythonDrillLevel WordLevel(string id, string title, string description, string[] groups, int length)
        {
            return new PythonDrillLevel("python-words-" + id, title, description, random =>
            {
                var phrases = new List<string>();
                int characters = 0;
                while (characters < length)
                {
                    foreach (string phrase in Shuffle(random, groups))
                    {
                        phrases.Add(phrase);
                        characters += phrase.Length + 1;
                        if (characters >= length) break;
                    }
                }
                return string.Join(" ", phrases);
            });
        }

        private static PythonDrillLevel CodeLevel(string id, string title, string description, int count,
            bool hasRepairs, params Func<Random, string>[] blocks)
        {
            return new PythonDrillLevel("python-code-" + id, title, description, random =>
            {
                var lines = new List<string>();
                while (lines.Count < count)
                {
                    foreach (var block in Shuffle(random, blocks))
                    {
                        lines.Add(block(random));
                        if (lines.Count == count) break;
                    }
                }
                return string.Join("\n", lines);
            }, hasRepairs);
        }

        private static T[] Shuffle<T>(Random random, T[] choices)
        {
            var order = (T[])choices.Clone();
            for (int i = order.Length - 1; i > 0; i--)
            {
                int swap = random.Next(i + 1);
                T previous = order[swap];
                order[swap] = order[i];
                order[i] = previous;
            }
            return order;
        }

        private static string Pick(Random random, string[] choices) { return choices[random.Next(choices.Length)]; }
        private static string Digit(Random random) { return random.Next(1, 10).ToString(CultureInfo.InvariantCulture); }
        private static string Index(Random random) { return random.Next(0, 2).ToString(CultureInfo.InvariantCulture); }
        private static string NumberPair(Random random) { return Digit(random) + ", " + Digit(random); }

        private static string InsertMistake(string source, int before, string extra)
        {
            return source.Insert(before, extra + new string(CodeText.Erase, extra.Length));
        }
    }
}
