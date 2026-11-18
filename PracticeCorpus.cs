using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TouchTypingStudio
{
    internal static class PracticeCorpus
    {
        private static readonly string[][] Russian =
        {
            new[]
            {
                "Утром окна запотели, и я включил свет.", "В конверте лежала карта нашего района.",
                "Лёд растаял, а на реке появилась рябь.", "Маша закрыла книгу и посмотрела на часы.",
                "На столе осталась чашка горячего чая.", "Где начинается тропа к озеру?",
                "В саду расцвели белые яблони.", "У окна спал кот, свернувшись клубком.",
                "Вера повесила снимок рядом с дверью.", "Наконец-то выглянуло солнце!",
                "Мы вернулись домой до начала дождя.", "Дежурный принёс ключи от зала."
            },
            new[]
            {
                "Поезд подъехал тихо, хотя перрон был почти пуст.", "Если успеешь, захвати фонарь и запасные батарейки.",
                "Когда закроется выставка и где можно взять каталог?", "Утром ветер стих, зато к вечеру опять поднялись волны.",
                "Назови удобный день, и мы перенесём встречу.", "Через десять минут занавес поднялся, и оркестр заиграл.",
                "Кто-то оставил у входа зонтик, перчатки и блокнот.", "Неужели этот снимок сделан возле нашего дома?",
                "Я вышел чуть раньше, чтобы пройтись вдоль набережной.", "Вот и первый снег, а на дорожке уже появились следы!",
                "Рядом с хлебом лежали яблоки, орехи и горсть изюма.", "Пока вода грелась, мы раскладывали карточки на столе."
            },
            new[]
            {
                "На наклейке было написано: \"Открывать с этой стороны\".", "Три детали уже готовы; четвёртую проверим после обеда.",
                "Запиши адрес: улица Полевая, дом 7, подъезд 2.", "Завтра у нас две встречи: в 10:30 и в 14:00.",
                "В зале (слева от входа) установили новый экран.", "Билет № 14 оказался просрочен: дата на нём была вчерашняя.",
                "Проверь цифры: на второй странице указано 35%, а на первой - 53%.", "Он спросил: \"У нас есть запасной ключ?\"",
                "Мы взяли воду, бинт и карту; дорога петляла между холмами.", "В расписании (его обновили в четверг) поменялись два пункта.",
                "Раздел 3: вопросы, ответы и несколько примеров.", "Путь занял 2 часа, хотя мы планировали уложиться в 90 минут."
            }
        };

        private static readonly string[][] English =
        {
            new[]
            {
                "The garden was quiet in the morning.", "A red bicycle stood by the gate.",
                "Nora packed a notebook and a pencil.", "Where did you leave the keys?",
                "Rain tapped against the kitchen window.", "Please bring a cup of tea.",
                "The clouds broke, and the sun appeared.", "A small boat crossed the lake.",
                "Daniel made a map of the old town.", "We found the last missing piece!",
                "Lucy opened the letter after lunch.", "The train arrived at the far platform."
            },
            new[]
            {
                "When the bell rang, we folded the map and went outside.", "Maya checked the address, then slipped the letter into her bag.",
                "If the trail is muddy, take the longer path across the field.", "Would you like to stop for coffee before we head home?",
                "The shelves held old records, loose photographs, and a blue vase.", "We missed the first bus, but the next one came within minutes.",
                "I couldn't hear the announcement over the sound of the rain.", "Wait until both lights turn green!",
                "After dinner, we took turns reading from the same book.", "The box looked empty, yet something rattled inside it.",
                "Sam walked back to the bridge, where we had left the canoe.", "Don't forget your scarf; the wind is getting colder."
            },
            new[]
            {
                "The sign read: \"Please close the gate behind you.\"", "Our meeting starts at 10:30; bring the latest draft.",
                "Check the last page: the total should be $32.50, including tax.", "At 7:15, the room was still empty (except for two folding chairs).",
                "I had four options: walk, wait, call a friend, or turn back.", "She asked, \"Can you meet me at the west entrance?\"",
                "We expected a 5% increase, but sales rose by 8% in July.", "Write the route on the label: Market Street -> River Road.",
                "That was a close call - the gate shut just as we reached it!", "Package B contains 12 brackets, 24 screws, and a spare label.",
                "The notes (pages 4-6) list the sources; the appendix explains the method.", "Reply with \"yes\" or \"no\" before Friday, and include room #204."
            }
        };

        private static readonly string[] Symbols =
        {
            "!", "?", ".", ",", "+", "-", "=", "()", "[]", "{}", "'", "\"", ":", ";", "*", "&", "%",
            "+=", "-=", "==", "!=", ">=", "<=", ":=", "//", "**", "->", "&&", "||", "_", "\\", "@", "#", "$", "^", "~", "`", "0", "9"
        };

        private static readonly Func<Random, string>[][] AdvancedPython =
        {
            new Func<Random, string>[]
            {
                r => Repair("values = [" + Number(r) + ", 8, 1]\npairs = {f'n_{item}': item**2 for item in values if item % 2 != 0}", "{", "]"),
                r => Repair("rows = [('a', " + Number(r) + "), ('b', 2)]\nresult = sorted(rows, key=lambda row: row[1], reverse=True)", "[1]", "}"),
                r => Repair("config = {'start': " + Number(r) + ", 'step': 2, 'stop': 20}\nindices = list(range(config['start'], config['stop'], config['step']))", "['stop']", ","),
                r => Repair("vector = [-1, " + Number(r) + ", 5]\nfiltered = [value**2 if value >= 0 else 0 for value in vector]", ">=", "_"),
                r => Repair("pages = {'first': [" + Number(r) + ", 8], 'last': [2, 7]}\nprint('last=%r' % (pages['last'][::-1],))", "[::-1]", ")")
            },
            new Func<Random, string>[]
            {
                r => Repair("record = {'ready': True, 'points': (" + Number(r) + ", 5)}\nassert record['ready'] and (record['points'][0] >= 1), f'{record!r}'", ">=", "<="),
                r => Repair("data = {'items': [[1, " + Number(r) + "], [3, 4]], 'flags': {'on', 'off'}}\nresult = {f'row_{i}': row[::-1] for i, row in enumerate(data['items']) if set(row) & {1, 3}}", "[::-1]", "]]"),
                r => Repair("slots: dict[str, tuple[int, int]] = {'east': (" + Number(r) + ", 7), 'west': (2, 9)}\nfor zone, (x, y) in slots.items():\n    print(f'{zone}: {x * y:04d}', end='; ')", ":04d", "::"),
                r => Repair("base = {'user': {'roles': ['reader'], 'active': False}, 'time': " + Number(r) + "}\npatch = {'time': base['time'] // 2}\nprint({**base, **patch}['time'] != 0, sep=' | ')", "**patch", "(("),
                r => Repair("samples = [(x, (x << 2) | 1) for x in range(" + Number(r) + ")]\nsummary = [f'{n:02d}={v:#06b}' for n, v in samples if v & 0b100]\nprint(' / '.join(summary))", ":#06b", "#:")
            }
        };

        public static Func<Random, string>[] Python(int level)
        {
            return level < 3 ? PythonDrills.CodeLevels[level].Blocks : AdvancedPython[level - 3];
        }

        public static Func<Random, string>[] Prose(ContinuousKind kind, int level)
        {
            string[] sentences = kind == ContinuousKind.Russian ? Russian[level] : English[level];
            return AsBlocks(sentences);
        }

        public static Func<Random, string>[] AsBlocks(IEnumerable<string> snippets)
        {
            return snippets.Select(line => (Func<Random, string>)(_ => line)).ToArray();
        }

        public static Func<Random, string>[] SymbolBlocks(int level, string[] custom)
        {
            var bank = custom.Length > 0 ? custom : Symbols.Take(level == 0 ? 17 : level == 1 ? 29 : Symbols.Length).ToArray();
            return new Func<Random, string>[] { random => SymbolLine(random, bank, level) };
        }

        private static string SymbolLine(Random random, string[] bank, int level)
        {
            int tokenCount = 10 + level * 5;
            var parts = new List<string>();
            int size = 0;
            while (parts.Count < tokenCount && size < 160)
            {
                string token = bank[random.Next(bank.Length)];
                if (bank.Length > 1 && parts.Count > 0 && parts.Last() == token)
                {
                    int next = (Array.IndexOf(bank, token) + 1) % bank.Length;
                    token = bank[next];
                }
                parts.Add(token);
                size += token.Length + 1;
            }
            return string.Join(level == 2 ? "" : " ", parts);
        }

        private static string Number(Random random)
        {
            return random.Next(1, 10).ToString(CultureInfo.InvariantCulture);
        }

        private static string Repair(string code, string before, string extra)
        {
            int index = code.IndexOf(before, StringComparison.Ordinal);
            return code.Insert(index, extra + new string(CodeText.Erase, extra.Length));
        }
    }
}
