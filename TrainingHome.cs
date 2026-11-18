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
            if (!string.IsNullOrEmpty(profile.Theme)) Background = FlightUi.Backdrop(profile);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var stack = new StackPanel { MaxWidth = 1080, Margin = new Thickness(32, 27, 26, 24) };
            Ui.Add(stack, Ui.Text("Выберите тренировку", 34, Ui.Ink, true), 17);
            var main = new UniformGrid { Columns = 2 };
            InitialFocus = AddCard(main, "Python", "Пишите код в английской раскладке. Сложность можно менять по ходу набора.",
                delegate { owner.OpenTyping(ContinuousKind.Python); }, "#29363A", "#B2E2CF", "Alt + 1");
            AddCard(main, "Windows без мыши", "Работайте с файлами и окнами своего компьютера по подсказкам нижней панели.",
                owner.ShowDesktopTraining, "#323140", "#D9CBFA", "Alt + 2");
            Ui.Add(stack, main, 16);

            Ui.Add(stack, Ui.Text("Текст и символы", 19, Ui.Ink, true), 9);
            var typing = new UniformGrid { Columns = 3 };
            AddCard(typing, "Русский текст", "Фразы, заглавные буквы и пунктуация.",
                delegate { owner.OpenTyping(ContinuousKind.Russian); }, "#292D36", "#C2DAF0");
            AddCard(typing, "English text", "Английские фразы и абзацы.",
                delegate { owner.OpenTyping(ContinuousKind.English); }, "#292D36", "#EDCEA8");
            AddCard(typing, "Спецсимволы", "Скобки, операторы и знаки с Shift.",
                delegate { owner.OpenTyping(ContinuousKind.Symbols); }, "#292D36", "#ECC4CC");
            Ui.Add(stack, typing, 15);

            var other = new WrapPanel();
            Ui.Add(other, Ui.Button("Горячие клавиши", delegate { owner.OpenWorkbench(WorkbenchArea.Shortcuts); }));
            Ui.Add(other, Ui.Button("Свои наборы", delegate { owner.ShowPracticeSets(); }));
            Ui.Add(stack, other, 9);
            Ui.Add(stack, Ui.Text("Tab — выбрать     Enter — открыть", 13, Ui.Muted));
            Content = stack;
        }

        private static Button AddCard(Panel parent, string title, string detail, Action start, string surface, string tint, string shortcut = null)
        {
            var entry = Ui.Button(title, start);
            var content = new StackPanel();
            Ui.Add(content, Ui.Text(title, shortcut == null ? 19 : 27, Ui.Brush(tint), true), 6);
            Ui.Add(content, Ui.Text(detail, 14, Ui.Brush("#D2D3DA")));
            if (shortcut != null)
            {
                var key = Ui.Text(shortcut, 13, Ui.Brush(tint), true);
                key.FontFamily = Ui.Mono.FontFamily;
                var cap = Ui.Panel(key, new Thickness(9, 3, 9, 3), Ui.Brush("#20232B"));
                cap.BorderBrush = Ui.Brush("#575967");
                cap.CornerRadius = new CornerRadius(5);
                cap.HorizontalAlignment = HorizontalAlignment.Left;
                cap.Margin = new Thickness(0, 14, 0, 0);
                Ui.Add(content, cap);
            }
            entry.Content = content;
            entry.HorizontalContentAlignment = HorizontalAlignment.Left;
            entry.VerticalContentAlignment = VerticalAlignment.Center;
            entry.Background = Ui.Brush(surface);
            entry.BorderBrush = Ui.Brush("#444954");
            entry.Padding = new Thickness(shortcut == null ? 15 : 20);
            entry.MinHeight = shortcut == null ? 100 : 147;
            entry.Margin = new Thickness(0, 0, 9, 9);
            Ui.Name(entry, title);
            parent.Children.Add(entry);
            return entry;
        }
    }
}
