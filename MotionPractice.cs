using System;
using System.Collections.Generic;
using System.Text;

namespace TouchTypingStudio
{
    internal static class MotionPractice
    {
        private const int LearnLength = 120;
        private const int MixLength = 145;
        private const int IntroLength = 25;

        public static Lesson[] Foundations()
        {
            return new[]
            {
                Keys("home-motion", "Основной ряд · d j f k", "dj|f|k", "",
                    "Найдите выступы F/J. Для d используйте левый средний палец, для j — правый указательный."),
                Plus(),
                EqualsAndEnter(),
                Backspace(),
                Keys("home-rest", "Основной ряд · остальные буквы", "as|l;|gh", "dfjk",
                    "На каждом шаге добавляйте не больше двух клавиш. Возвращайте указательные на F/J."),
                Keys("upper-motion", "Верхний ряд", "er|ui|ty|qw|op", "asdfghjkl;",
                    "Первую пару отработайте отдельно. Возвращайтесь на основной ряд после движения вверх."),
                Keys("lower-motion", "Нижний ряд и минус", "vb|nm|xc|z-", "adfjkqwop",
                    "Начните с двух движений вниз. Удерживайте исходную позицию остальных пальцев."),
                Keys("digit-motion", "Цифры верхнего ряда", "45|78|39|12|0", "6+=fj",
                    "Набирайте цифры верхним рядом и возвращайте руку к исходной позиции.")
            };
        }

        private static Lesson Keys(string id, string title, string groups, string previous, string skill)
        {
            string active = groups.Replace("|", "");
            var intro = new OpeningSequence();
            AddNewKeys(intro, groups);
            intro.Finish(Sequence(active, previous, 29, MixLength - intro.Length));
            var rounds = new[]
            {
                intro.Text, Sequence(active, previous, 41, MixLength),
                Sequence(active, previous, 61, MixLength), Sequence(active, previous, 73, MixLength)
            };
            return Make(id, title, KeysLabel(active), skill, rounds, intro);
        }

        private static Lesson Plus()
        {
            var intro = new OpeningSequence();
            intro.Add("Новые движения · 6 и +", "666+++ 6+6+6++6 66++66+ 6++6+66");
            intro.Finish(Blocks("6+66+6|j6j6|6++66|fdjkj|66+6+6|jfj6|6+6++66", 4, MixLength - intro.Length));
            var rounds = new[]
            {
                intro.Text,
                Blocks("66++6+6|f6f6|6+6+6++6|jfjk6|6++6++6|d6k6j", 2, MixLength),
                Blocks("6++6+6|djffj|66+6+6|j6jj6|+66++6|kfdfj|666+++6", 1, MixLength),
                Blocks("66+6++6|j6+6+f|666+66|djkj6|6+6+6++6|f6j6k", 3, MixLength)
            };
            return Make("plus-motion", "Верхний ряд · 6 и +", "6 · +",
                "Цифра 6 — правым указательным. Для + нажмите левый Shift и клавишу = правым мизинцем.",
                rounds, intro);
        }

        private static Lesson EqualsAndEnter()
        {
            var intro = new OpeningSequence();
            intro.Add("Новое движение · =", "===666 6=6==6 66===6 6==6=6");
            intro.Add("Новое движение · Enter", "\n6=6\n66=6\n6=66", "");
            intro.Finish(Blocks("f = 6|j = 6|d = 6 + 6|k = 6|j = 6 + 6|f = 6 + 6 + 6", 0, MixLength - intro.Length, "\n"), "\n");
            var rounds = new[]
            {
                intro.Text,
                Blocks("f = 6|j = 6|d = 6 + 6|k = 6|j = 6 + 6|f = 6 + 6 + 6", 2, MixLength, "\n"),
                Blocks("j = 6|f = 6 + 6|k = 6|d = 6 + 6 + 6|f = 6|k = 6 + 6", 1, MixLength, "\n"),
                Blocks("d = 6|k = 6 + 6|j = 6 + 6 + 6|f = 6|d = 6 + 6|j = 6", 3, MixLength, "\n")
            };
            return Make("equals-motion", "Равно и переход на новую строку", "= · Enter",
                "Начните со знака = без Shift. В той же строке добавится Enter, затем короткие присваивания.",
                rounds, intro);
        }

        private static Lesson Backspace()
        {
            var intro = new OpeningSequence();
            intro.Add("Новое движение · Backspace", Repairs("66!6 666!6 6!6 66!66"));
            intro.Add("Добавляем знакомый +", Repairs("6++!6 6+66! 66!+6 6+6+!"));
            intro.Finish(Repairs(Blocks("f = 66!|j = 6++!6|d = 66! + 6|k = 6+6+!|f = 6+66!|j = 6",
                0, MixLength - intro.Length, "\n")), "\n");
            var rounds = new[]
            {
                intro.Text,
                Repairs(Blocks("f = 66!|j = 6++!6|d = 66! + 6|k = 6+6+!|f = 6+66!|j = 6", 1, MixLength, "\n")),
                Repairs(Blocks("j = 6+6+!|k = 66!|d = 6++!6|f = 6+66!|d = 66! + 6", 2, MixLength, "\n")),
                Repairs(Blocks("k = 66! + 6|d = 6+66!|f = 6++!6|j = 66!|k = 6+6+!", 4, MixLength, "\n"))
            };
            return Make("backspace-motion", "Backspace · убрать лишний знак", "6 · + · ⌫",
                "Наберите указанное, включая лишний символ. Когда увидите ⌫, нажмите Backspace правым мизинцем.",
                rounds, intro);
        }

        private static string Repairs(string text) { return text.Replace('!', CodeText.Erase); }

        private static Lesson Make(string id, string title, string focus, string skill, string[] rounds, OpeningSequence intro)
        {
            return new Lesson(id, title, skill, "Упражнения на движение пальцев и клавиши Python.", focus,
                Layout.English, rounds) { Opening = intro.Stages, CheckRounds = 2 };
        }

        public static Lesson Prepare(Lesson course, string newGroups, string previous)
        {
            if (newGroups.Length == 0) return course;
            var intro = new OpeningSequence();
            AddNewKeys(intro, newGroups);
            string active = newGroups.Replace("|", "");
            if (previous.Length > 0)
                intro.Add("Новые клавиши вместе со знакомыми", Sequence(active, previous, 29, IntroLength));
            intro.Finish(course.Rounds[0], course.Language == Layout.Russian ? " " : "\n\n");
            course.Rounds[0] = intro.Text;
            course.Opening = intro.Stages;
            course.PreviousIntroRounds = LegacyIntroCount(newGroups, previous);
            return course;
        }

        private static int LegacyIntroCount(string groups, string previous)
        {
            string[] split = groups.Split('|');
            return split.Length + (split[0].Length == 2 ? 1 : 0) + (previous.Length > 0 ? 1 : 0);
        }

        private static void AddNewKeys(OpeningSequence intro, string groups)
        {
            string current = "";
            int seed = 1;
            foreach (string group in groups.Split('|'))
            {
                string caption = current.Length == 0 ? "Новые клавиши · " : "Добавляем · ";
                intro.Add(caption + KeysLabel(group), Sequence(group, current, seed++, IntroLength, true));
                current += group;
            }
        }

        private sealed class OpeningSequence
        {
            private readonly StringBuilder text = new StringBuilder();
            private readonly List<LessonStage> stages = new List<LessonStage>();
            public int Length { get { return text.Length; } }
            public string Text { get { return text.ToString(); } }
            public LessonStage[] Stages { get { return stages.ToArray(); } }

            public void Add(string caption, string part, string separator = " ")
            {
                if (text.Length > 0) text.Append(separator);
                stages.Add(new LessonStage(text.Length, caption));
                text.Append(part);
            }

            public void Finish(string part, string separator = " ")
            {
                Add("Закрепление", part, separator);
            }
        }

        private static string Sequence(string focus, string previous, int seed, int length, bool repeatFirst = false)
        {
            var random = new Random(seed);
            var result = new StringBuilder(length + 8);
            if (repeatFirst)
            {
                result.Append(new string(focus[0], 3));
                if (focus.Length > 1) result.Append(new string(focus[1], 3));
            }
            while (result.Length < length)
            {
                if (result.Length > 0) result.Append(' ');
                int count = 4 + random.Next(3);
                for (int i = 0; i < count; i++)
                {
                    // Give the new keys most of the repetitions while mixing in earlier movements.
                    string keys = previous.Length > 0 && (i == 2 || i == 4) ? previous : focus;
                    result.Append(keys[random.Next(keys.Length)]);
                }
            }
            return result.ToString();
        }

        private static string Blocks(string pattern, int offset, int length = LearnLength, string separator = " ")
        {
            var blocks = pattern.Split('|');
            var text = new StringBuilder(length + 16);
            for (int i = 0; text.Length < length; i++)
            {
                if (i > 0) text.Append(separator);
                text.Append(blocks[(i + offset) % blocks.Length]);
            }
            return text.ToString();
        }

        private static string KeysLabel(string keys) { return string.Join(" ", keys.ToCharArray()); }
    }
}
