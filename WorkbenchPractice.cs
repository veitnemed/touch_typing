using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace TouchTypingStudio
{
    internal enum WorkbenchArea { Windows, Chrome, Code, Mixed, Shortcuts }

    internal sealed class WorkbenchChord
    {
        public readonly string Label;
        public readonly string PracticeLabel;
        public readonly Key Trigger;
        public readonly ModifierKeys Modifiers;

        private WorkbenchChord(string label, string practiceLabel, Key key, ModifierKeys modifiers)
        {
            Label = label;
            PracticeLabel = practiceLabel;
            Trigger = key;
            Modifiers = modifiers;
        }

        public bool Matches(Key key, ModifierKeys modifiers)
        {
            if (key != Trigger && !(Trigger >= Key.D0 && Trigger <= Key.D9 && key >= Key.NumPad0 && key <= Key.NumPad9 &&
                Trigger - Key.D0 == key - Key.NumPad0)) return false;
            return Modifiers == modifiers;
        }

        public static bool TryParse(string input, out WorkbenchChord chord)
        {
            chord = null;
            if (string.IsNullOrWhiteSpace(input)) return false;
            var parts = input.Split('+').Select(part => part.Trim()).ToArray();
            ModifierKeys modifiers = ModifierKeys.None;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                ModifierKeys modifier;
                switch (parts[i].ToUpperInvariant())
                {
                    case "CTRL": case "CONTROL": modifier = ModifierKeys.Control; break;
                    case "SHIFT": modifier = ModifierKeys.Shift; break;
                    case "ALT": modifier = ModifierKeys.Alt; break;
                    case "WIN": case "WINDOWS": modifier = ModifierKeys.Windows; break;
                    default: return false;
                }
                if ((modifiers & modifier) != 0) return false;
                modifiers |= modifier;
            }
            var token = parts.Last();
            Key key;
            if (token.Length == 1 && token[0] >= '0' && token[0] <= '9') key = Key.D0 + (token[0] - '0');
            else
            {
                switch (token.ToUpperInvariant())
                {
                    case "ESC": key = Key.Escape; break;
                    case "ENTER": case "RETURN": key = Key.Return; break;
                    case "PGUP": key = Key.PageUp; break;
                    case "PGDN": key = Key.PageDown; break;
                    case "DEL": key = Key.Delete; break;
                    case "BACKSPACE": key = Key.Back; break;
                    case "`": key = Key.Oem3; break;
                    case ".": key = Key.OemPeriod; break;
                    case ",": key = Key.OemComma; break;
                    case "-": case "−": key = Key.OemMinus; break;
                    case "PLUS": case "=": key = Key.OemPlus; break;
                    case "/": key = Key.Oem2; break;
                    default:
                        if (token.Length == 0 || char.IsDigit(token[0]) || !Enum.TryParse(token, true, out key)) return false;
                        break;
                }
            }
            if (key == Key.None || key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin || key == Key.System ||
                key == Key.DeadCharProcessed || key == Key.ImeProcessed || key == Key.PrintScreen ||
                (key == Key.Delete && modifiers == (ModifierKeys.Control | ModifierKeys.Alt))) return false;

            var practice = modifiers;
            if ((modifiers & ModifierKeys.Windows) != 0)
            {
                bool arrow = key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down;
                practice = (modifiers & ~ModifierKeys.Windows) | ModifierKeys.Control | (arrow ? ModifierKeys.Shift : ModifierKeys.Alt);
                if (arrow) practice &= ~ModifierKeys.Alt;
            }
            else if ((modifiers & ModifierKeys.Alt) != 0 && (key == Key.Tab || key == Key.F4 || key == Key.Escape))
                practice = (modifiers & ~ModifierKeys.Alt) | ModifierKeys.Control;
            else if (key == Key.Escape && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                practice = ModifierKeys.Control | ModifierKeys.Alt;
            else if (key == Key.Escape && modifiers == ModifierKeys.Control)
                practice = ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt;
            if (key == Key.Delete && (practice & (ModifierKeys.Control | ModifierKeys.Alt)) ==
                (ModifierKeys.Control | ModifierKeys.Alt)) return false;
            chord = new WorkbenchChord(Describe(key, modifiers), Describe(key, practice), key, practice);
            return true;
        }

        internal static WorkbenchChord Parse(string input)
        {
            WorkbenchChord chord;
            if (!TryParse(input, out chord)) throw new ArgumentException("Неверное сочетание: " + input);
            return chord;
        }

        private static string Describe(Key key, ModifierKeys modifiers)
        {
            var parts = new List<string>();
            if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
            if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
            string name;
            if (key >= Key.D0 && key <= Key.D9) name = ((int)(key - Key.D0)).ToString();
            else
            {
                switch (key)
                {
                    case Key.Return: name = "Enter"; break;
                    case Key.Escape: name = "Esc"; break;
                    case Key.Back: name = "Backspace"; break;
                    case Key.PageDown: name = "PgDn"; break;
                    case Key.PageUp: name = "PgUp"; break;
                    case Key.Oem3: name = "`"; break;
                    case Key.OemPeriod: name = "."; break;
                    case Key.OemComma: name = ","; break;
                    case Key.OemMinus: name = "−"; break;
                    case Key.OemPlus: name = "="; break;
                    case Key.Oem2: name = "/"; break;
                    default: name = key.ToString(); break;
                }
            }
            parts.Add(name);
            return string.Join("+", parts);
        }
    }

    internal sealed class WorkbenchCustomTask
    {
        public readonly WorkbenchArea Area;
        public readonly string Chord;
        public readonly string Command;

        public WorkbenchCustomTask(WorkbenchArea area, string chord, string command)
        {
            Area = area;
            Chord = chord;
            Command = command;
        }
    }

    internal sealed class WorkbenchScene
    {
        public WorkbenchArea Area;
        public string Caption = "";
        public string Location = "";
        public string Headline = "";
        public string Body = "";
        public string Detail = "";
        public bool Sidebar = true;
        public bool Terminal;
        public int Selection;
        public readonly List<string> Items = new List<string>();

        public string Selected { get { return Items.Count == 0 ? "" : Items[Math.Max(0, Math.Min(Selection, Items.Count - 1))]; } }
        public void ReplaceItems(params string[] items)
        {
            Items.Clear();
            Items.AddRange(items);
            Selection = 0;
        }
        public void AddItem(string item)
        {
            Items.Add(item);
            Selection = Items.Count - 1;
        }
        public void RemoveSelected()
        {
            if (Items.Count > 0) Items.RemoveAt(Selection);
            Selection = Math.Max(0, Math.Min(Selection, Items.Count - 1));
        }
    }

    internal sealed class WorkbenchStep
    {
        public readonly WorkbenchChord Chord;
        public readonly string Goal, EntryPrompt, EntryValue, SelectItem;
        public readonly Action<WorkbenchScene> Apply;

        public WorkbenchStep(string chord, string goal, Action<WorkbenchScene> apply = null, string prompt = null,
            string value = null, string select = null)
        {
            Chord = WorkbenchChord.Parse(chord);
            Goal = goal;
            Apply = apply;
            EntryPrompt = prompt;
            EntryValue = value;
            SelectItem = select;
        }

        public bool InputMatches(string input)
        {
            if (EntryValue == null) return false;
            var text = (input ?? "").Trim();
            if (EntryValue.StartsWith("workspace.test/", StringComparison.OrdinalIgnoreCase))
            {
                if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) text = text.Substring(8);
                if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) text = text.Substring(7);
                text = text.TrimEnd('/');
            }
            return string.Equals(text, EntryValue, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class WorkbenchTask
    {
        public readonly WorkbenchScene Scene;
        public readonly string Title;
        public readonly WorkbenchStep[] Steps;

        public WorkbenchTask(WorkbenchScene scene, string title, params WorkbenchStep[] steps)
        {
            Scene = scene;
            Title = title;
            Steps = steps;
        }
    }

    internal sealed class WorkbenchSession
    {
        private readonly Random random;
        private readonly WorkbenchArea area;
        private readonly bool onlyOwn;
        private readonly List<Func<Random, WorkbenchTask>> customFactories;
        private List<Func<Random, WorkbenchTask>> tasks;
        private readonly List<int> remaining = new List<int>();
        private int previous = -1;

        public WorkbenchTask Task { get; private set; }
        public int StepIndex { get; private set; }
        public WorkbenchStep Step { get { return StepIndex < Task.Steps.Length ? Task.Steps[StepIndex] : null; } }
        public bool NeedsInput { get; private set; }
        public int ScenarioCount { get { return tasks.Count; } }

        public WorkbenchSession(WorkbenchArea area, IEnumerable<WorkbenchCustomTask> custom, int difficulty, bool customOnly,
            Random random = null)
        {
            this.random = random ?? new Random();
            this.area = area;
            onlyOwn = area == WorkbenchArea.Shortcuts || customOnly;
            var own = (custom ?? Enumerable.Empty<WorkbenchCustomTask>())
                .Where(item => item != null && (area == WorkbenchArea.Mixed || area == WorkbenchArea.Shortcuts ||
                    item.Area == area || item.Area == WorkbenchArea.Mixed) && !string.IsNullOrWhiteSpace(item.Command))
                .Where(item => { WorkbenchChord chord; return WorkbenchChord.TryParse(item.Chord, out chord); }).ToList();
            customFactories = own.Select(item => new Func<Random, WorkbenchTask>(rng => WorkbenchTasks.Custom(item))).ToList();
            SetDifficulty(difficulty);
            NextTask();
        }

        public void SetDifficulty(int difficulty)
        {
            tasks = onlyOwn && customFactories.Count > 0
                ? customFactories
                : WorkbenchTasks.Get(area, difficulty).Concat(customFactories).ToList();
            remaining.Clear();
            previous = -1;
        }

        public void NextTask()
        {
            if (remaining.Count == 0) remaining.AddRange(Enumerable.Range(0, tasks.Count));
            var candidates = remaining.Count > 1 ? remaining.Where(value => value != previous).ToArray() : remaining.ToArray();
            int chosen = candidates[random.Next(candidates.Length)];
            remaining.Remove(chosen);
            previous = chosen;
            Task = tasks[chosen](random);
            StepIndex = 0;
            NeedsInput = false;
        }

        public bool Press(Key key, ModifierKeys modifiers)
        {
            var step = Step;
            if (step == null || NeedsInput) return false;
            if (step.Chord.Matches(key, modifiers) &&
                (step.SelectItem == null || Task.Scene.Selected == step.SelectItem))
            {
                if (step.EntryValue == null) Advance();
                else NeedsInput = true;
                return true;
            }
            if (step.SelectItem != null && modifiers == ModifierKeys.None && Task.Scene.Items.Count > 0 &&
                (key == Key.Up || key == Key.Down || key == Key.Home || key == Key.End))
            {
                int last = Task.Scene.Items.Count - 1;
                if (key == Key.Home) Task.Scene.Selection = 0;
                else if (key == Key.End) Task.Scene.Selection = last;
                else Task.Scene.Selection = Math.Max(0, Math.Min(last, Task.Scene.Selection + (key == Key.Up ? -1 : 1)));
                return true;
            }
            return false;
        }

        public void CancelInput() { NeedsInput = false; }

        public bool Submit(string text)
        {
            if (!NeedsInput || !Step.InputMatches(text)) return false;
            Advance();
            return true;
        }

        private void Advance()
        {
            var action = Step.Apply;
            if (action != null) action(Task.Scene);
            NeedsInput = false;
            StepIndex++;
        }
    }
}
