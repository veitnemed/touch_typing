using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TouchTypingStudio
{
    internal sealed class TrainingHome : ScrollViewer
    {
        public readonly UIElement InitialFocus;

        public TrainingHome(StudioWindow owner, StudioProfile profile)
        {
            Background = FlightUi.Backdrop(profile);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var stack = FlightUi.Sheet();
            Ui.Add(stack, Ui.Text("Выберите тренировку", 34, Ui.Ink, true), 5);
            Ui.Add(stack, Ui.Text("Задания идут непрерывно. Результаты появятся после остановки.", 15, Ui.Muted), 22);

            Ui.Add(stack, Ui.Text("Печать", 19, Ui.Ink, true), 8);
            var typing = new UniformGrid { Columns = 3 };
            InitialFocus = AddCard(typing, "Python", "Код, отступы, операторы и Backspace. Можно менять сложность.",
                delegate { owner.OpenTyping(ContinuousKind.Python); });
            AddCard(typing, "Русский текст", "Связные тексты с заглавными буквами и пунктуацией.",
                delegate { owner.OpenTyping(ContinuousKind.Russian); });
            AddCard(typing, "English text", "Обычные фразы и абзацы с заглавными буквами.",
                delegate { owner.OpenTyping(ContinuousKind.English); });
            AddCard(typing, "Спецсимволы", "Скобки, операторы, кавычки и знаки с Shift.",
                delegate { owner.OpenTyping(ContinuousKind.Symbols); });
            AddCard(typing, "Горячие клавиши", "Короткие задания на сочетания в учебном окне.",
                delegate { owner.OpenWorkbench(WorkbenchArea.Shortcuts); });
            AddCard(typing, "Свои наборы", "Добавьте текст, знаки или сочетания и тренируйте их.",
                delegate { owner.ShowPracticeSets(); });
            Ui.Add(stack, typing, 15);

            Ui.Add(stack, Ui.Text("Работа без мыши", 19, Ui.Ink, true), 8);
            var work = new UniformGrid { Columns = 1 };
            AddCard(work, "Windows без мыши", "Настоящий рабочий стол. Связанные задания в Проводнике, Блокноте, Chrome и VS Code.",
                owner.ShowDesktopTraining);
            Ui.Add(stack, work, 8);
            Ui.Add(stack, Ui.Text("Tab — выбор · Enter — открыть · F2 — к режимам · F4 — клавиатура во время печати", 13, Ui.Muted));
            Content = stack;
        }

        private static Button AddCard(Panel parent, string title, string detail, Action start)
        {
            var entry = Ui.Button(title, start);
            var content = new StackPanel();
            Ui.Add(content, Ui.Text(title, 20, Ui.Ink, true), 6);
            Ui.Add(content, Ui.Text(detail, 13, Ui.Muted));
            entry.Content = content;
            entry.HorizontalContentAlignment = HorizontalAlignment.Left;
            entry.Background = Ui.Brush("#19283B");
            entry.BorderBrush = Ui.Brush("#34495F");
            entry.Padding = new Thickness(14);
            entry.MinHeight = 102;
            entry.Margin = new Thickness(0, 0, 9, 9);
            Ui.Name(entry, title);
            parent.Children.Add(entry);
            return entry;
        }
    }
}
