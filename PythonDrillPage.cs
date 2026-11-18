using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace TouchTypingStudio
{
    internal sealed class PythonDrillPage : ScrollViewer
    {
        public readonly UIElement InitialFocus;

        public PythonDrillPage(StudioWindow owner, StudioProfile profile, PythonDrillLevel selected)
        {
            Background = FlightUi.Backdrop(profile);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Focusable = false;
            var stack = FlightUi.Sheet();
            Ui.Add(stack, Ui.Text("Слова и код Python", 31, Ui.Ink, true), 6);
            Ui.Add(stack, Ui.Text("Выберите любой уровень, затем начните пробелом.", 15, Ui.Muted), 15);

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            var buttons = new Dictionary<PythonDrillLevel, Button>();
            var words = Group(owner, profile, "Только слова", "Строчные английские буквы и пробелы.", PythonDrills.WordLevels, buttons);
            words.Margin = new Thickness(0, 0, 6, 0);
            var code = Group(owner, profile, "Адаптивный Python", "Новые фрагменты идут подряд. Следующая ступень зависит от точности.", PythonDrills.CodeLevels, buttons);
            code.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(code, 1);
            columns.Children.Add(words);
            columns.Children.Add(code);
            Ui.Add(stack, columns, 12);
            Ui.Add(stack, Ui.Text("Tab — выбор уровня · Enter — открыть · Пробел — начать набор · Esc — назад", 13, Ui.Muted), 8);
            var back = Ui.QuietButton("К набору", owner.ClosePanel);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            Ui.Add(stack, back);
            Content = stack;
            InitialFocus = selected != null && buttons.ContainsKey(selected) ? buttons[selected] : buttons[PythonDrills.WordLevels[0]];
        }

        private static Border Group(StudioWindow owner, StudioProfile profile, string title, string note,
            PythonDrillLevel[] levels, Dictionary<PythonDrillLevel, Button> buttons)
        {
            var group = new StackPanel();
            Ui.Add(group, Ui.Text(title, 21, Ui.Ink, true), 3);
            Ui.Add(group, Ui.Text(note, 13, Ui.Muted), 9);
            for (int index = 0; index < levels.Length; index++)
            {
                var level = levels[index];
                var button = Ui.QuietButton((index + 1) + ". " + level.Title, delegate { owner.OpenPythonDrill(level); });
                button.FontSize = 15;
                button.Foreground = FlightUi.Accent(profile);
                button.HorizontalAlignment = HorizontalAlignment.Left;
                Ui.Name(button, title + ", уровень " + (index + 1) + ", " + level.Title);
                Ui.Add(group, button);
                Ui.Add(group, Ui.Text(level.Description, 13, Ui.Muted), 10);
                buttons.Add(level, button);
            }
            return Ui.Panel(group, new Thickness(13, 10, 13, 8), Ui.Brush("#182535"));
        }
    }
}
