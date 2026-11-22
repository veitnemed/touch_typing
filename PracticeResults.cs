using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TouchTypingStudio
{
    internal sealed class PracticeResultsPage : ScrollViewer
    {
        public readonly UIElement InitialFocus;

        public PracticeResultsPage(StudioProfile profile, string name, ContinuousTypingSnapshot result,
            PracticeRecord best, Action resume, Action restart, Action home)
        {
            Background = FlightUi.Backdrop(profile);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var stack = FlightUi.Sheet();
            Ui.Add(stack, Ui.Text(name, 32, Ui.Ink, true), 5);
            bool qualified = PracticeRecords.CanRecordTyping(result);
            string note = qualified ? "Результат засчитан" :
                "Для зачёта нужно " + PracticeRecords.MinimumCharacters + " символов. Набрано " + result.Count + ".";
            Ui.Add(stack, Ui.Text(note, 15, qualified ? Ui.Brush("#8BE3B4") : Ui.Muted), 19);

            var measures = new UniformGrid { Columns = 2 };
            AddMetric(measures, "Скорость", result.Cpm.ToString("0") + " сим/мин",
                result.Wpm.ToString("0") + " слов/мин · 5 символов на слово",
                best == null ? "Лучший результат появится после зачёта" : "Лучшая скорость · " + best.Speed.ToString("0") + " сим/мин",
                qualified ? ScoreColor(result.Cpm, 90, 165) : Ui.Muted,
                best == null ? Ui.Muted : ScoreColor(best.Speed, 90, 165));
            AddMetric(measures, "Точность", result.Accuracy.ToString("0.0") + "%",
                result.Count + " символов · " + Duration(result.Seconds),
                best == null ? "Лучший результат появится после зачёта" : "Лучшая точность · " + best.Accuracy.ToString("0.0") + "%",
                qualified ? ScoreColor(result.Accuracy, 92, 97) : Ui.Muted,
                best == null ? Ui.Muted : ScoreColor(best.Accuracy, 92, 97));
            Ui.Add(stack, measures, 10);

            var mistakes = new StackPanel();
            Ui.Add(mistakes, Ui.Text("Частые промахи", 17, Ui.Ink, true), 7);
            var missed = result.TopMistakes.Select(item => KeyLabel(item.Key) + "  ×" + item.Value).ToArray();
            Ui.Add(mistakes, Ui.Text(missed.Length == 0 ? "Промахов нет" : string.Join("    ·    ", missed), 15, Ui.Muted));
            Ui.Add(stack, Ui.Panel(mistakes, new Thickness(13), Ui.Brush("#182536")), 15);
            var actions = new WrapPanel();
            var next = Ui.Button("Продолжить набор", resume, true);
            Ui.Add(actions, next);
            Ui.Add(actions, Ui.Button("Новый сеанс", restart));
            Ui.Add(actions, Ui.Button("К режимам", home));
            Ui.Add(stack, actions);
            Content = stack;
            InitialFocus = next;
        }

        private static void AddMetric(Panel panel, string title, string value, string detail, string best, Brush accent, Brush bestColor)
        {
            var content = new StackPanel();
            Ui.Add(content, Ui.Text(title, 14, Ui.Muted, true), 5);
            Ui.Add(content, Ui.Text(value, 28, accent, true), 4);
            Ui.Add(content, Ui.Text(detail, 13, Ui.Muted), 13);
            Ui.Add(content, Ui.Text(best, 13, bestColor, true));
            var card = Ui.Panel(content, new Thickness(13), Ui.Brush("#182536"));
            card.Margin = new Thickness(0, 0, 8, 8);
            panel.Children.Add(card);
        }

        private static Brush ScoreColor(double value, double middle, double good)
        {
            return Ui.Brush(value >= good ? "#8BE3B4" : value >= middle ? "#FFD381" : "#FFA2A7");
        }

        private static string Duration(double seconds)
        {
            return TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss") + " активного набора";
        }

        private static string KeyLabel(char value)
        {
            if (value == ' ') return "Пробел";
            if (value == '\n') return "Enter";
            if (value == CodeText.Erase) return "Backspace";
            return "«" + value + "»";
        }
    }
}
