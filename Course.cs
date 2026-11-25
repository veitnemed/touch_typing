using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio
{
    internal static class Lessons
    {

        private static readonly string[] Digits = { "5", "4", "7", "6", "54", "76", "45", "67", "564", "746" };
        private static readonly string[] AnyDigits = { "1", "5", "8", "2", "54", "30", "9", "67", "564", "21" };
        private static readonly string[] Names = { "qty", "power", "output", "query", "width", "port", "price", "owner", "write", "worker" };
        private static readonly string[] ShortTexts = { "ok", "up", "out", "low", "off", "slow", "skip", "wide", "full", "good" };
        private static readonly string[] Fields = { "id", "size", "port", "qty", "row", "col", "tag", "path", "mode", "name" };

        public static readonly Lesson[] Python = Number(MotionPractice.Foundations().Concat(Spiral(new[]
        {
            En("assign", "Присваивания на основном ряду", "F/J · = · 4 5 6 7 · Enter",
                "После равно и цифры верните пальцы на основной ряд.",
                "src/configs/comon.py · короткие присваивания, имена и значения учебные.",
                Assignments),
            En("reach", "Буквы на верхнем ряду", "Q W O P · верхний ряд · остальные цифры",
                "Q и P — мизинцы, W и O — безымянные. С той же позиции наберите имена целиком.",
                "src/configs/comon.py · тот же ритм присваивания с переменными вместо констант.",
                UpperRow),
            En("names", "Нижний ряд и знак минус", "Z X C V B N M · - · U I",
                "После X и C возвращайте пальцы на основной ряд. Наберите минус без Shift.",
                "src/configs/comon.py · несколько связанных присваиваний из структуры настроек.",
                i => i == 0 ? "x = 5\nc = 6\nx = x - 4\nc = c - 5" : Lines(4, n =>
                {
                    string name = Pick(new[] { "x", "count", "max", "min", "name", "box", "value", "index", "base", "size" }, n * 3, i);
                    return name + " = " + Pick(AnyDigits, n, i) + (n > 1 ? " - " + Pick(Digits, n + 1, i) : "");
                })),
            En("apostrophe", "Строки в одинарных кавычках", "'",
                "Замыкайте пару кавычек одним мизинцем. Перед следующим словом возвращайтесь к F/J.",
                "src/model.py · строковые имена параметров, вынесенные в отдельные переменные.",
                i => Lines(5, n => Pick(Fields, n, i) + " = '" + Pick(ShortTexts, n * 3, i) + "'")),
            En("pairs", "Круглые скобки", "( ) · левый Shift",
                "Сначала закрепите пару на 9 и 0. Удерживайте левый Shift для обеих скобок в ().",
                "src/model.py · те же скобки, что в вызовах. Здесь пары используются в присваиваниях.",
                i => i == 0 ? "x = ()\nf = ()\nj = (5)\nx = (6)" :
                    Lines(6, n => n % 3 == 0 ? "x = ()" : (n % 3 == 1 ? "value" : "item") + " = (" + Pick(Digits, n, i) + ")")),
            En("print", "Вызовы print() и len()", "слово → ( → аргумент → )",
                "Переносите движение для скобок на вызов. Для каждого вызова заново находите исходную позицию.",
                "src/model.py и src/spectrum.py · форма часто встречающихся вызовов.",
                i => Lines(6, n => n == 0 ? "word = '" + Pick(ShortTexts, n, i) + "'" :
                    n == 4 ? "size = len(word)" : (n % 2 == 0 ? "print(word)" : "print(" + Pick(AnyDigits, n, i) + ")"))),
            En("list-index", "Списки и индексы", "[ ] · ,",
                "Откройте и закройте квадратную скобку правым мизинцем. Потренируйте запятую средним пальцем.",
                "src/model.py и src/spectrum.py · индексы и выбор значений, список заменяет массив.",
                i => "items = [" + Pick(AnyDigits, 0, i) + ", " + Pick(AnyDigits, 2, i) + ", " + Pick(AnyDigits, 4, i) + "]\n"
                    + "first = items[0]\nprint(items[1])\nprint(items[2])" + (i % 2 == 0 ? "\nsize = len(items)" : "\nprint(first)")),
            En("call", "Вызовы с аргументами", ", · ' · = внутри ()",
                "После запятой делайте один пробел. В именованном аргументе наберите равно вплотную.",
                "src/model.py · позиционные и именованные аргументы в вызове, пример на встроенных функциях.",
                i => "print('" + Pick(ShortTexts, 0, i) + "', '" + Pick(ShortTexts, 3, i) + "')\n"
                    + "print(" + Pick(AnyDigits, 1, i) + ", " + Pick(AnyDigits, 2, i) + ", sep='" + new[] { " ", ",", "-" }[i % 3] + "')\n"
                    + "print('" + Pick(ShortTexts, 5, i) + "', end=' ')"),
            En("dict", "Двойные кавычки и словарь", "\" · : · { }",
                "Двойные кавычки, двоеточие и обе фигурные скобки набирайте с левым Shift. Слева удерживайте Shift, справа меняйте клавишу.",
                "src/model.py · словарь значений и обращение по строковому ключу.",
                i => "data = {\"" + Pick(Fields, 0, i) + "\": " + Pick(AnyDigits, 0, i) + ", \"" + Pick(Fields, 1, i) + "\": " + Pick(AnyDigits, 3, i) + "}\n"
                    + "value = data[\"" + Pick(Fields, 0, i) + "\"]\nprint(data[\"" + Pick(Fields, 1, i) + "\"])\nprint(value)"),
            En("attributes", "Точка, подчёркивание и импорт", ". · _",
                "Точка без Shift правым безымянным, подчёркивание с левым Shift. Отработайте цепочку имя.метод.",
                "src/spectrum.py и src/configs/comon.py · импорт и вызовы через точку.",
                i => "import math\n" + Pick(Fields, 0, i) + "_value = math.floor(" + i + ".5)\n"
                    + "print(math.ceil(" + i + ".4))\nprint(" + Pick(Fields, 0, i) + "_value)"),
            En("function", "Функция и отступ", "def · : → Enter → Tab",
                "После двоеточия нажмите Enter. В начале строки Tab даёт четыре пробела.",
                "src/spectrum.py · структура def → присваивание → return; данные заменены.",
                i =>
                {
                    string method = Pick(new[] { "echo", "double", "first", "label", "clean", "store", "keep", "wrap", "send", "read" }, 0, i);
                    return "def " + method + "(value):\n    result = value\n    return result\n\nprint(" + method + "(" + Pick(AnyDigits, 2, i) + "))";
                }),
            En("compare", "Условие и сравнение", "== · < · > · !=",
                "Повторите соседние символы сравнения. После двоеточия вернитесь на исходный ряд и сделайте отступ.",
                "src/spectrum.py · условия с len и сравнением, числа и вывод учебные.",
                i => "size = " + Pick(AnyDigits, 0, i) + "\nif size " + new[] { "==", "!=", "<", ">", ">=", "<=" }[i % 6]
                    + " " + Pick(AnyDigits, 3, i) + ":\n    print('" + Pick(ShortTexts, 2, i) + "')\nelse:\n    print(size)"),
            En("for", "Цикл и распаковка пары", "for · , · : · отступ",
                "В заголовке цикла чередуйте запятую, пробел и двоеточие. Повторите отступ на двух строках.",
                "src/model.py · for с enumerate; список и тело цикла заменены.",
                i => "items = ['" + Pick(ShortTexts, 0, i) + "', '" + Pick(ShortTexts, 1, i) + "']\n"
                    + "for index, value in enumerate(items):\n    print(index)\n    print(value)"),
            En("operators", "Операторы и составные знаки", "+ · * · ** · / · // · %",
                "Сначала один знак, затем пара. Смотрите на пробелы по обе стороны оператора.",
                "src/configs/comon.py и tests/test_energy.py · формы операций, значения учебные.",
                i => "value = " + Pick(AnyDigits, 0, i) + " + " + Pick(Digits, 2, i) + "\n"
                     + "product = value * " + (i + 2) + "\n"
                     + "print(product ** 2)\nprint(product " + new[] { "/", "//", "%" }[i % 3] + " 3)"),
            En("assert", "Проверки и тесты", "assert · == · []",
                "Проверьте движение для двух знаков равно, ключа словаря и отступа внутри теста.",
                "tests/test_energy.py и tests/test_fft.py · форма assert, сравниваются простые значения.",
                i => "def test_size():\n    data = {\"" + Pick(Fields, 0, i) + "\": " + i + "}\n"
                     + "    assert len(data) == 1\n    assert data[\"" + Pick(Fields, 0, i) + "\"] == " + i),
            En("comprehension", "Списковое включение и f-строка", "[] → for → if · f\"{}\"",
                "Смешайте ранее освоенные движения в двух строках. Делайте паузу на вложенных скобках.",
                "src/model.py и src/pseudospectrum.py · формы выборки и вывода результата.",
                i => "items = [" + i + ", " + (i + 1) + ", " + (i + 2) + "]\n"
                     + "values = [item for item in items if item > " + i + "]\nprint(f\"Size: {len(values)}\")")
        })).ToArray());

        public static readonly Lesson[] Russian = Number(Spiral(new[]
        {
            Ru("ru-home", "ва ол", "В А · О Л",
                "Положите указательные на А/О по выступам. Соседние В и Л набираются средними пальцами.",
                "ва|ав|ол|ло|вал|вол|лава|овал|алло|лав"),
            Ru("ru-fingers", "фы дж", "Ф Ы · Д Ж",
                "Добавьте мизинцы и безымянные. Смешивайте новые буквы с первым уроком.",
                "фы|дж|ыф|жд|вода|два|жало|дало|валы|фалда"),
            Ru("ru-bottom", "ми ть", "М И · Т Ь",
                "Добавьте нижний ряд. М и И набирает левый указательный, Т и Ь — правый указательный.",
                "ми|ть|им|ат|мимо|тьма|мама|мать|дом|том"),
            Ru("ru-index", "еп нр", "Е П · Н Р",
                "Поднимайте указательные к Е/Н и возвращайте их на А/О после каждого движения.",
                "еп|нр|ре|пе|пена|море|лето|поле|перо|дело"),
            Ru("ru-upper", "ук гш", "У К · Г Ш",
                "Отрабатывайте движения к верхнему ряду. Возвращайте левую руку к А, правую к О.",
                "ук|гш|ку|шг|книга|шум|урок|угол|рука|шкаф"),
            Ru("ru-sides", "чс бю", "Ч С · Б Ю",
                "Ч — левый безымянный, С — левый средний. Б и Ю набираются правой рукой.",
                "чс|бю|сч|юб|сумка|бочка|люди|собака|чашка|блюдо"),
            Ru("ru-outer", "йц щз", "Й Ц · Щ З",
                "Освойте внешние клавиши верхнего ряда. Следите за положением ладоней.",
                "йц|щз|цй|зщ|звонок|щит|клей|цена|задача|плащ"),
            Ru("ru-end", "я эхъ", "Я Э · Х Ъ",
                "Выполняйте движение мизинцем с устойчивой опорой на основной ряд.",
                "яэ|хъ|эх|ъя|эхо|яхта|объект|съезд|ясно|ящик"),
            Ru("ru-shift", "Ё, заглавные и пунктуация", "Ё · Shift · . , ?",
                "Набирайте заглавную букву с противоположным Shift. У точки и запятой в RU одна физическая клавиша.",
                "Ёлка.|Да,|нет.|Всё.|Как?|Ещё|Здесь.|Рядом.|Готово.|Хорошо.")
        }));

        public static readonly Lesson[] Extra =
        {
            Drill("qwop", "Q W O P · разминка", "Q W O P", "Клавиши урока 30 Stamina.",
                "qw|op|wq|po|qo|wp|ow|pq|qwo|opw"),
            Drill("xc", "X C , . · разминка", "X C , .", "Клавиши нижнего ряда из каталога Stamina.",
                "xc|,.|cx|.,|x,|c.|.x|,c|xc,|c.x"),
            new Lesson("mixed", "Код с русским комментарием",
                "После # смените раскладку для букв комментария. Перед следующей строкой вернитесь на EN.",
                "Python и русский текст в одном фрагменте.", "Alt+Shift / Win+Space · EN ↔ RU", Layout.Neutral, Rounds(i =>
                "value = " + i + "\n# " + Pick(new[] { "лава", "вода", "дом", "овал", "алло", "том", "два", "мама", "вал", "мимо" }, 0, i)
                + "\nprint(value)")),
            new Lesson("work-note-bilingual", "Заметка коллеге · RU / EN",
                "Наберите сообщение целиком: даты, суммы и коды должны совпасть. Для имён файлов и пометки на английском смените раскладку обычным способом.",
                "Вымышленные рабочие заметки. Ничего не отправляется.", "Путь · время · № / # · RU ↔ EN", Layout.Neutral,
                "Тема: передача отчёта № 204\n"
                + "Привет! Таблицу за 07.12 я положила в reports/weekly.csv. В ней 28 строк, две проверки пока не закрыты. На листе итогов зафиксирована сумма 1 240,50 руб.\n"
                + "Открой задачу QA-204 и перед встречей в 16:30 проверь две последние строки. Если суммы не совпадут, напиши на ops@example.test с темой QA-204.\n"
                + "Status: ready for review; don't replace the source file.",
                "Тема: подтверждение выдачи\n"
                + "Заявку № 318 согласовали на 11.12, время 09:15. У стойки нужно назвать код BOX-318. В файле pickup/summary.csv отмечено 12 коробок; четыре отложены отдельно.\n"
                + "Если выдачу перенесут, обнови время в заметке и отправь уточнение на desk@example.test. Не меняй дату в исходной таблице до подтверждения.\n"
                + "Pickup note: confirm the time before marking the order complete.",
                "Тема: сверка счёта\n"
                + "Привет! Проверь счёт № 672 на 2 370,80 руб. до 14:45. Исходный файл лежит в billing/invoice_672.pdf, выгрузка для сверки в billing/check_672.csv.\n"
                + "В записи INV-672 указана скидка 8%. Если итог не совпадёт, оставь в задаче обе суммы и путь к таблице. Ответ жду на audit@example.test до 18.12.\n"
                + "Review #672: keep the original and share the corrected total.",
                "Тема: обновление расписания\n"
                + "На 14.12 перенесли встречу с 13:10 на 14:20. Уточнённое время записано в team/slot_15.txt. Проверь, что старая встреча № 15 не дублируется в календаре.\n"
                + "Отправь приглашение с кодом CAL-015 на team@example.test. В ответе попроси подтвердить время, а не заново переслать весь список участников.\n"
                + "Calendar update: use the new slot and keep the same attendee list.",
                "Тема: проверка резервной копии\n"
                + "В 10:05 закончилось копирование отчётов. Протокол за 16.12 находится в backup/check_52.txt. В нём 18 файлов, один помечен как неполный.\n"
                + "Открой заявку BK-052 и приложи список, не заменяя двоичные файлы. Если второй прогон снова не пройдёт, пришли описание на help@example.test до 12:30.\n"
                + "Backup check #52: report the missing file and leave both copies in place.",
                "Тема: остаток для заявки № 93\n"
                + "В таблице stock/jan_93.csv проверь строку ITEM-930: по списку 16 комплектов, в заявке 18. До 17:40 пришли на store@example.test количество, подтверждённое кладовщиком.\n"
                + "В поле заметки укажи дату 22.12 и разницу в 2 комплекта. Старый остаток не удаляй: он нужен для сравнения с новой записью.\n"
                + "Stock note #93: confirm the quantity before you close the request.")
                { WholeRounds = true, CheckRounds = 2 }
        };

        public static readonly Lesson[] All = Python.Concat(Russian).Concat(Extra).ToArray();
        public static int RoundCount { get { return All.Sum(level => level.Rounds.Length); } }

        private static Lesson En(string id, string title, string focus, string skill, string origin, Func<int, string> exercise)
        {
            var course = new Lesson(id, title, skill, origin, focus, Layout.English, Rounds(exercise));
            switch (id)
            {
                case "assign": return course;
                case "reach": return MotionPractice.Prepare(course, "qw|op", "dfjk=");
                case "names": return MotionPractice.Prepare(course, "xc|-", "f6=op");
                case "apostrophe": return MotionPractice.Prepare(course, "'", "asop");
                case "pairs": return MotionPractice.Prepare(course, "()", "6f=");
                case "print": return course;
                case "list-index": return MotionPractice.Prepare(course, "[]|,", "6j()");
                case "call": return MotionPractice.Prepare(course, "", "");
                case "dict": return MotionPractice.Prepare(course, "\":|{}", "6j[]");
                case "attributes": return MotionPractice.Prepare(course, "._", "dfjk");
                case "compare": return MotionPractice.Prepare(course, "<>|!", "6=");
                case "operators": return MotionPractice.Prepare(course, "*/|%", "6+");
                default: return course;
            }
        }

        private static Lesson Ru(string id, string title, string focus, string skill, string words)
        {
            var course = new Lesson(id, title, skill, "Порядок групп клавиш из русского курса Stamina. Строки составлены для тренажёра.",
                focus, Layout.Russian, WordRounds(words, true));
            string groups;
            string previous;
            switch (id)
            {
                case "ru-home": groups = "во|а|л"; previous = ""; break;
                case "ru-fingers": groups = "фы|дж"; previous = "ваол"; break;
                case "ru-bottom": groups = "ми|ть"; previous = "ваолд"; break;
                case "ru-index": groups = "еп|нр"; previous = "волмть"; break;
                case "ru-upper": groups = "ук|гш"; previous = "аолне"; break;
                case "ru-sides": groups = "чс|бю"; previous = "таворк"; break;
                case "ru-outer": groups = "йц|щз"; previous = "алвчсу"; break;
                case "ru-end": groups = "яэ|хъ"; previous = "волсез"; break;
                default: groups = "ёЁ|.,|?"; previous = "амно"; break;
            }
            return MotionPractice.Prepare(course, groups, previous);
        }

        private static Lesson Drill(string id, string title, string focus, string origin, string words)
        {
            var baseLesson = new Lesson(id, title, "Равномерный набор с возвратом к F/J.", origin, focus, Layout.English, WordRounds(words, false));
            return MotionPractice.Prepare(baseLesson, id == "qwop" ? "qw|o|p" : "xc|,|.", "dfjk");
        }

        private static string[] WordRounds(string input, bool preferWords)
        {
            var words = input.Split('|');
            return Rounds(i =>
            {
                var random = new Random(17 + i);
                int count = preferWords ? 25 + i % 3 : 31 + i % 3;
                return string.Join(" ", Enumerable.Range(0, count)
                    .Select(n => words[random.Next(preferWords && n >= 4 ? 4 : 0, words.Length)]));
            });
        }

        private static string[] Rounds(Func<int, string> exercise)
        {
            const int variants = 10;
            var originals = Enumerable.Range(0, variants).Select(exercise).ToArray();
            var rounds = new string[24];
            for (int i = 0; i < rounds.Length; i++)
            {
                var text = new System.Text.StringBuilder(originals[i % variants]);
                int stride = i / variants + 1;
                int offset = i + stride;
                while (text.Length < 120)
                {
                    text.Append("\n\n");
                    text.Append(originals[offset % variants]);
                    offset += stride;
                }
                for (int cycle = 0; cycle < i / variants; cycle++)
                {
                    text.Append("\n\n");
                    text.Append(originals[offset % variants]);
                    offset += stride;
                }
                rounds[i] = text.ToString();
            }
            return rounds;
        }

        private static string Assignments(int round)
        {
            if (round == 0) return "f = 5\nj = 6\nf = 6\nj = 5";
            if (round == 1) return "d = 4\nk = 7\nf = 5\nj = 6\nd = 6\nk = 5";
            if (round == 2) return "a = 5\nl = 6\ns = 4\nk = 7\na = 6\nl = 5";
            var names = new[] { "f", "j", "a", "s", "d", "k", "l", "g", "h", "add" };
            return Lines(6, n => Pick(names, n, round) + " = " + Pick(Digits, n * 3, round));
        }

        private static string UpperRow(int round)
        {
            if (round == 0) return "q = 1\nw = 2\no = 3\np = 8\nq = 9\np = 0";
            return Lines(5, n => Pick(Names, n * 3, round) + " = " + Pick(AnyDigits, n, round));
        }

        private static string Lines(int count, Func<int, string> line)
        { return string.Join("\n", Enumerable.Range(0, count).Select(line)); }

        private static string Pick(string[] values, int offset, int round)
        { return values[(offset + round) % values.Length]; }

        private static Lesson[] Spiral(Lesson[] lessons)
        {
            for (int i = 0; i < lessons.Length; i++)
            {
                var level = lessons[i];
                if (i == 0) continue;
                var previous = lessons[i - 1];
                for (int round = 14; round <= 16; round++)
                {
                    // Recall one movement from the preceding level before the new material.
                    char separator = level.Language == Layout.Russian ? ' ' : '\n';
                    string sample = previous.Rounds[round - 13];
                    string warmup;
                    if (level.Language == Layout.Russian) warmup = string.Join(" ", sample.Split(' ').Take(4));
                    else
                    {
                        var lines = sample.Split('\n');
                        var statement = lines.Take(1);
                        if (lines[0].EndsWith(":", StringComparison.Ordinal))
                            statement = statement.Concat(lines.Skip(1).TakeWhile(line => line.StartsWith(" ", StringComparison.Ordinal)));
                        warmup = string.Join("\n", statement);
                    }
                    level.Rounds[round] = warmup + separator + level.Rounds[round];
                }
            }
            return lessons;
        }

        private static Lesson[] Number(Lesson[] lessons)
        {
            for (int i = 0; i < lessons.Length; i++)
            {
                lessons[i].Number = i + 1;
                lessons[i].CourseSize = lessons.Length;
            }
            return lessons;
        }

        private static Lesson[] Course(Lesson lesson)
        {
            if (Python.Contains(lesson)) return Python;
            if (Russian.Contains(lesson)) return Russian;
            return Extra.Take(2).ToArray();
        }

        public static string Track(Lesson lesson)
        {
            if (Python.Contains(lesson)) return "Python EN";
            if (Russian.Contains(lesson)) return "Русский RU";
            return "Разминка";
        }

        public static Lesson Find(string id) { return All.FirstOrDefault(l => l.Id == id); }

        public static void Upgrade(StudioProfile profile)
        {
            if (profile.TypingCourse == Lesson.CurrentCourse) return;
            if (profile.TypingCourse < 4)
            {
                // This profile predates the course with key exercises before code.
                profile.PythonLesson = Python[0].Id;
                if (Python.Contains(Find(profile.SelectedLesson))) profile.SelectedLesson = Python[0].Id;
                profile.RecallDates.Clear();
            }
            else
            {
                // Keep credit for exercises after the opening. The opening itself now has different sequences.
                foreach (var level in All)
                {
                    if (level.Rounds.Length == 24)
                    {
                        int first = level.Opening.Length == 0 ? 0 : 1;
                        for (int variant = first; variant < level.Rounds.Length; variant++)
                        {
                            string prior = "course4/" + level.Id + "/" + (variant + level.PreviousIntroRounds);
                            string pass = level.PassId(variant);
                            if (profile.GoodPasses.Contains(prior) && !profile.GoodPasses.Contains(pass)) profile.GoodPasses.Add(pass);
                        }
                    }
                    var draft = profile.FindDraft(level.Id);
                    if (draft != null)
                    {
                        int index = Array.IndexOf(level.Rounds, draft.Target);
                        if (index >= 0) draft.Variant = index;
                        else profile.SaveDraft(level.Id, null);
                    }
                }
                profile.RecallDates.RemoveAll(entry =>
                {
                    var level = Find(entry.Lesson);
                    return level == null || level.GoodRounds(profile) < level.Rounds.Length;
                });
            }
            profile.TypingCourse = Lesson.CurrentCourse;
        }

        public static Lesson NextDue(StudioProfile profile, DateTime today)
        {
            return profile.RecallDates.Where(item => item.Due.Date <= today.Date)
                .OrderBy(item => item.Due).Select(item => Find(item.Lesson)).FirstOrDefault(item => item != null);
        }

        public static string RecallText(Lesson lesson, int pass)
        {
            // A shorter complete excerpt from the independent part of this level.
            return lesson.Rounds[lesson.Rounds.Length - 1 - pass % lesson.CheckRounds];
        }

        public static void ScheduleRecall(StudioProfile profile, Lesson level, DateTime now, bool afterReview, bool passed)
        {
            var entry = profile.RecallDates.Find(r => r.Lesson == level.Id);
            if (entry == null)
            {
                entry = new RecallDate { Lesson = level.Id, Due = now.Date.AddDays(profile.Training.Delay(0)) };
                profile.RecallDates.Add(entry);
                return;
            }
            if (!afterReview) return;
            if (passed) entry.Checks++;
            int days = profile.Training.Delay(passed ? entry.Checks : 0);
            entry.Due = now.Date.AddDays(days);
        }

        public static Lesson Next(Lesson lesson)
        {
            var course = Course(lesson);
            int index = Array.IndexOf(course, lesson);
            return index >= 0 && index + 1 < course.Length ? course[index + 1] : null;
        }

        public static Lesson Previous(Lesson lesson)
        {
            var course = Course(lesson);
            int index = Array.IndexOf(course, lesson);
            return index > 0 && lesson.Number > 0 ? course[index - 1] : null;
        }

    }
}
