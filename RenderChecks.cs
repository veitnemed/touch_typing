using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TouchTypingStudio
{
    // Captures the application's WPF content at the requested DPI, without reading the desktop.
    internal static class RenderChecks
    {
        public static void Run(Application app, ProfileStore store, string directory)
        {
            Directory.CreateDirectory(directory);
            var window = new StudioWindow(store, false);
            app.MainWindow = window;
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.Left = SystemParameters.VirtualScreenLeft - 1300;
            window.Show();
            foreach (double scale in new[] { 1.0, 1.25, 1.5 })
            {
                window.OpenLesson("home-motion");
                // MateBook screen, with room reserved for the native window and taskbar.
                double width = Math.Min(1190, 1920 / scale - 30);
                double height = Math.Min(897, 1200 / scale - 70);
                string suffix = (scale * 100).ToString("0", CultureInfo.InvariantCulture);
                window.Width = width;
                window.Height = height;
                window.ShowHome();
                window.UpdateLayout();
                Save((FrameworkElement)window.Content, scale, Path.Combine(directory, "home-" + suffix + ".png"));
                window.ShowDesktopTraining();
                window.UpdateLayout();
                Save((FrameworkElement)window.Content, scale, Path.Combine(directory, "desktop-start-" + suffix + ".png"));
                var strip = new DesktopStrip(delegate { }, delegate { }, delegate { }, delegate { });
                strip.Set(2, 5, "Проверка сборки Python", "Сохраните строки о сбоях в отчёт", "Enter",
                    "findstr выбирает строки с указанным словом. Знак > записывает их в файл.",
                    "", "Тачпад включён · Ctrl+Alt+F10", true, true,
                    "findstr FAILED build.log > report.txt", "Командная строка", true,
                    "build.log", "report.txt", "В отчёте останутся только строки со словом FAILED.");
                SaveStrip(strip, 1920 / scale, scale, Path.Combine(directory, "desktop-panel-" + suffix + ".png"));
                strip.Set(5, 7, "Разложите файлы проекта", "Вставьте копию заметки", "Ctrl+V",
                    "Папка Copies уже открыта. Вставьте туда скопированный файл.", "", "Тачпад включён · Ctrl+Alt+F10",
                    true, false, source: "review-release.txt", dest: @"Copies\review-release.txt",
                    result: "У заметки появится копия с таким же содержимым.");
                SaveStrip(strip, 1920 / scale, scale, Path.Combine(directory, "explorer-panel-" + suffix + ".png"));
                strip.Set(2, 3, "Найдите материалы проекта", "Откройте учебный журнал", "Enter",
                    "Вставьте адрес в строку браузера.", "", "", true, true,
                    @"C:\Users\Учебная учётная запись\AppData\Local\TouchTypingStudio\DesktopPractice\example-project\browser\journal.html",
                    source: "Учебный журнал", dest: "Chrome", completed: 3);
                SaveStrip(strip, 1920 / scale, scale, Path.Combine(directory, "workday-address-" + suffix + ".png"));
                window.OpenWorkbench(WorkbenchArea.Windows);
                window.UpdateLayout();
                Save((FrameworkElement)window.Content, scale, Path.Combine(directory, "workbench-" + suffix + ".png"));
                foreach (int page in new[] { 1, 2 })
                {
                    window.ShowMode(page);
                    window.UpdateLayout();
                    Save((FrameworkElement)window.Content, scale,
                        Path.Combine(directory, (page == 1 ? "typing-" : "windows-") + suffix + ".png"));
                }
                window.ShowAtlas("python");
                window.UpdateLayout();
                Save((FrameworkElement)window.Content, scale, Path.Combine(directory, "atlas-" + suffix + ".png"));
                window.ShowPythonDrills();
                window.UpdateLayout();
                Save((FrameworkElement)window.Content, scale, Path.Combine(directory, "python-drills-" + suffix + ".png"));
                if (scale == 1.5)
                {
                    window.OpenLesson("ru-home");
                    window.UpdateLayout();
                    Save((FrameworkElement)window.Content, scale, Path.Combine(directory, "typing-ru.png"));
                    window.OpenLesson("plus-motion");
                    window.UpdateLayout();
                    Save((FrameworkElement)window.Content, scale, Path.Combine(directory, "typing-plus.png"));
                    window.OpenLesson("backspace-motion");
                    window.UpdateLayout();
                    Save((FrameworkElement)window.Content, scale, Path.Combine(directory, "typing-backspace.png"));
                    window.ShowModes("python");
                    window.UpdateLayout();
                    Save((FrameworkElement)window.Content, scale, Path.Combine(directory, "modes.png"));
                    window.ShowAnalytics();
                    window.UpdateLayout();
                    Save((FrameworkElement)window.Content, scale, Path.Combine(directory, "profile.png"));
                    window.ShowMode(3);
                    window.UpdateLayout();
                    Save((FrameworkElement)window.Content, scale, Path.Combine(directory, "challenge.png"));
                }
            }
            window.OpenTyping(ContinuousKind.Russian, 1);
            window.UpdateLayout();
            Save((FrameworkElement)window.Content, 1.5, Path.Combine(directory, "continuous-russian.png"));
            window.ShowPracticeSets(PracticeSetKind.Shortcuts);
            window.UpdateLayout();
            Save((FrameworkElement)window.Content, 1.5, Path.Combine(directory, "practice-sets.png"));
            var preview = new PracticeResultsPage(new StudioProfile(), "Python",
                new ContinuousTypingSnapshot(246, 7, 91, 97.2, new[] { new KeyValuePair<char, long>('(', 4) }),
                new PracticeRecord { Speed = 180, Accuracy = 99.5 }, delegate { }, delegate { }, delegate { });
            window.OpenPanel(preview, preview.InitialFocus);
            window.UpdateLayout();
            Save((FrameworkElement)window.Content, 1.5, Path.Combine(directory, "practice-results.png"));
            window.OpenPythonDrill(PythonDrills.WordLevels[0]);
            window.UpdateLayout();
            Save((FrameworkElement)window.Content, 1.5, Path.Combine(directory, "python-words.png"));
            window.OpenPythonDrill(PythonDrills.CodeLevels.First(level => level.HasRepairs));
            window.UpdateLayout();
            Save((FrameworkElement)window.Content, 1.5, Path.Combine(directory, "python-repairs.png"));
            // Non-persistent sample data is used only to exercise populated visuals.
            var sample = SampleProfile();
            var filled = new AnalyticsPage(window, sample, Layout.English);
            window.OpenPanel(filled, filled.InitialFocus);
            window.UpdateLayout();
            Save((FrameworkElement)window.Content, 1.5, Path.Combine(directory, "profile-sample.png"));
            var field = new AtlasPage(window, sample, "python");
            window.OpenPanel(field, field.InitialFocus);
            window.UpdateLayout();
            Save((FrameworkElement)window.Content, 1.5, Path.Combine(directory, "atlas-sample.png"));
            var card = new HintCard(new StudioProfile());
            card.Set("Две раскладки  ·  1/3", "Сохраните в Блокноте две строки: «ok» на EN и «лава» на RU.",
                "1. Выберите EN и наберите «ok», Enter.\n2. Win+Space — выбрать RU. Наберите «лава», затем Ctrl+S.", "",
                "Шаг 2 · ожидаю действие.", "Ctrl+Alt+H — подсказка  ·  Ctrl+Alt+стрелки — сдвиг\nAlt+Tab к тренажёру, Esc — остановить");
            card.Left = window.Left;
            card.Show();
            card.UpdateLayout();
            Save((FrameworkElement)card.Content, 1.5, Path.Combine(directory, "card.png"));
            card.Close();
            window.Close();
        }

        private static StudioProfile SampleProfile()
        {
            var p = new StudioProfile();
            DateTime day = DateTime.Now.AddHours(-5);
            foreach (var id in new[] { "home-motion", "plus-motion", "assign", "ru-home" })
            {
                Lesson lesson = Lessons.Find(id);
                foreach (int i in new[] { 0, 1, lesson.Rounds.Length - lesson.CheckRounds })
                    TrainingProgress.OnLesson(p, lesson, i, true, day);
            }
            foreach (int i in Enumerable.Range(0, 6))
            {
                var result = new TypingResult
                {
                    Lesson = "Присваивания на основном ряду", Count = 140, Errors = 12 - i * 2, Seconds = 240 - i * 10,
                    Date = day.AddDays(i - 5), Language = "EN", Activity = "Campaign", Independent = i > 3,
                    Qualified = i > 2, FirstMs = 580 - i * 20, Task = "preview-" + i, Region = "python"
                };
                p.Results.Add(result);
                foreach (var key in new[] { "f", "j", "6", "+", "a", "d", "e", "=", "n", "i", "s", "o", "ff" })
                    p.Motor.Add(new MotorSample { Date = result.Date, Locale = "EN", Sequence = key, Correct = 15,
                        Errors = key == "6" ? 4 : key == "+" ? 2 : key == "ff" ? 3 : 0,
                        Intervals = 14, Milliseconds = 9200, Independent = result.Independent });
            }
            return p;
        }

        private static void SaveStrip(DesktopStrip strip, double width, double scale, string path)
        {
            double height = Math.Max(DesktopTrainingWindow.PanelHeight, strip.RequiredHeight(width));
            strip.Measure(new Size(width, height));
            strip.Arrange(new Rect(0, 0, width, height));
            strip.UpdateLayout();
            Save(strip, scale, path);
        }

        private static void Save(FrameworkElement content, double scale, string path)
        {
            double width = content.ActualWidth, height = content.ActualHeight;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            var draw = new DrawingVisual();
            using (var surface = draw.RenderOpen())
            {
                surface.DrawRectangle(Ui.Night, null, new Rect(0, 0, width, height));
                var bounds = new Rect(0, 0, width, height);
                var contentBrush = new VisualBrush(content) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds };
                surface.DrawRectangle(contentBrush, null, bounds);
            }
            bitmap.Render(draw);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
