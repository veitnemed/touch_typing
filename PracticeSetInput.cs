using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio
{
    public enum PracticeSetKind { Symbols, Shortcuts, Text }

    public class PracticeSet
    {
        public string Id, Name, Content;
        public PracticeSetKind Kind;
        public override string ToString() { return Name; }
    }

    internal static class PracticeSetInput
    {
        public static string[] Fragments(PracticeSet set)
        {
            return set.Content.Split(new[] { set.Kind == PracticeSetKind.Symbols ? "\n" : "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => set.Kind == PracticeSetKind.Symbols ? value.Trim() : value.Trim('\n')).ToArray();
        }

        public static string Validate(PracticeSet set)
        {
            if (set.Kind == PracticeSetKind.Shortcuts)
            {
                WorkbenchCustomTask[] ignored;
                return ParseShortcuts(set.Content, out ignored);
            }
            var fragments = Fragments(set);
            if (set.Kind == PracticeSetKind.Symbols && fragments.Length == 0)
                return "Добавьте хотя бы одну группу знаков.";
            return ContinuousTyping.ValidateSnippets(
                set.Kind == PracticeSetKind.Symbols ? ContinuousKind.Symbols : ContinuousKind.Custom, fragments);
        }

        public static string ParseShortcuts(string content, out WorkbenchCustomTask[] tasks)
        {
            tasks = null;
            var result = new List<WorkbenchCustomTask>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = content.Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.Length == 0) continue;
                int split = line.IndexOf('|');
                int sourceLine = index + 1;
                WorkbenchChord chord;
                if (split < 0 || !WorkbenchChord.TryParse(line.Substring(0, split).Trim(), out chord))
                    return "Проверьте сочетание в строке " + sourceLine + ". Формат Ctrl+T | Новая вкладка.";
                string command = line.Substring(split + 1).Trim();
                if (command.Length == 0 || command.Length > 120) return "Добавьте короткое действие в строке " + sourceLine + ".";
                if (!unique.Add(chord.Label)) return "Сочетание «" + chord.Label + "» уже есть в наборе.";
                result.Add(new WorkbenchCustomTask(WorkbenchArea.Shortcuts, chord.Label, command));
                if (result.Count > 80) return "В одном наборе можно сохранить до 80 сочетаний.";
            }
            if (result.Count == 0) return "Добавьте хотя бы одно сочетание.";
            tasks = result.ToArray();
            return null;
        }
    }
}
