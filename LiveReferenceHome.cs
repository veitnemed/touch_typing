using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace TouchTypingStudio
{
    internal sealed partial class LiveStudio
    {
        private static readonly Brush ReferenceText = Ui.Brush("#F1EDF5");
        private static readonly Brush ReferenceMuted = Ui.Brush("#C6BFCC");
        private static readonly Brush ReferenceEdge = Ui.Brush("#504C58");
        private static readonly Brush ReferencePrimary = Ui.Brush("#64556E");
        private static readonly Brush ReferenceAccent = Ui.Brush("#A89BB9");
        private static readonly Brush ReferencePanel = Ui.Brush("#28272E");
        private static readonly Brush ReferenceField = Ui.Brush("#303039");
        private readonly bool customLiveChrome;
        private bool quietLiveAppearance = SystemParameters.HighContrast;
        private Border referenceHeader;
        private TextBlock referenceLayoutEn, referenceLayoutRu;
        private Border referenceEnChip, referenceRuChip;
        private Button referenceTheme;
        private Grid referenceHome;
        private FrameworkElement referenceWave;

        private void ConfigureLiveChrome()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 71, CornerRadius = new CornerRadius(0),
                ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });
        }

        private Border BuildReferenceHeader()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var brand = Stack(true);
            brand.VerticalAlignment = VerticalAlignment.Center;
            var emblem = new Border { Width = 44, Height = 44, Background = ReferenceGradient("#75667E", "#64556E", 85),
                CornerRadius = new CornerRadius(12), Child = ReferenceIcon("brand", 28) };
            brand.Children.Add(emblem);
            var brandText = ReferenceLabel("КОД ВСЛЕПУЮ", 22, ReferenceText, true);
            brandText.VerticalAlignment = VerticalAlignment.Center;
            brandText.Margin = new Thickness(13, 0, 0, 0);
            brand.Children.Add(brandText);
            var brandButton = ReferenceButton(delegate { NavigateReference(LivePage, true); }, 10);
            brandButton.Content = brand;
            brandButton.BorderThickness = new Thickness(0);
            brandButton.Background = Brushes.Transparent;
            AutomationProperties.SetName(brandButton, "Код вслепую · Главная");
            grid.Children.Add(brandButton);

            top.VerticalAlignment = VerticalAlignment.Center;
            top.HorizontalAlignment = HorizontalAlignment.Left;
            top.Margin = new Thickness(37, 0, 3, 0);
            Grid.SetColumn(top, 1);
            grid.Children.Add(top);

            var trailing = Stack(true);
            trailing.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailing, 2);
            var language = Stack(true);
            language.VerticalAlignment = VerticalAlignment.Center;
            language.ToolTip = "Системная раскладка Windows. Переключение: Win + пробел.";
            referenceEnChip = ReferenceLanguage("EN", out referenceLayoutEn);
            referenceRuChip = ReferenceLanguage("RU", out referenceLayoutRu);
            language.Children.Add(referenceEnChip);
            language.Children.Add(referenceRuChip);
            trailing.Children.Add(language);
            referenceTheme = ReferenceHeaderButton("Упростить фон", "moon", delegate
            {
                quietLiveAppearance = !quietLiveAppearance;
                ApplyReferenceAppearance();
            });
            AutomationProperties.SetAutomationId(referenceTheme, "HomeAppearance");
            trailing.Children.Add(referenceTheme);

            if (customLiveChrome)
            {
                var minimize = ReferenceHeaderButton("Свернуть", "minimize", delegate { SystemCommands.MinimizeWindow(this); });
                var maximize = ReferenceHeaderButton("Развернуть или восстановить", "maximize", delegate
                {
                    if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
                    else SystemCommands.MaximizeWindow(this);
                });
                var close = ReferenceHeaderButton("Закрыть", "close", delegate { SystemCommands.CloseWindow(this); });
                foreach (var button in new[] { minimize, maximize, close })
                {
                    button.Width = 24;
                    trailing.Children.Add(button);
                }
            }
            grid.Children.Add(trailing);
            referenceHeader = new Border { Height = 73, BorderBrush = Ui.Brush("#3A3740"), BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(27, 0, 14, 0), Child = grid };
            referenceHeader.SizeChanged += delegate
            {
                bool compact = referenceHeader.ActualWidth < 970;
                brandText.FontSize = compact ? 17 : 22;
                brandText.Margin = new Thickness(compact ? 8 : 13, 0, 0, 0);
                referenceHeader.Padding = new Thickness(compact ? 12 : 27, 0, compact ? 9 : 14, 0);
                top.Margin = new Thickness(compact ? 10 : 37, 0, 3, 0);
                foreach (Button child in top.Children)
                {
                    var row = child.Content as StackPanel;
                    if (row == null || row.Children.Count < 2) continue;
                    row.Children[0].Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
                    child.Padding = new Thickness(compact ? 7 : 11, 0, compact ? 7 : 11, 0);
                }
            };
            ApplyReferenceAppearance();
            return referenceHeader;
        }

        private void NavigateReference(string destination, bool useCatalog)
        {
            if (active) FinishSession();
            if (active) return;
            if (useCatalog) legacyLive = false;
            ShowPage(destination);
        }

        private void UpdateReferenceHeader()
        {
            top.Children.Clear();
            string[] keys = { LivePage, TypingPage };
            string[] titles = { "Live", "Печать" };
            string[] icons = { "practice", "learn" };
            for (int index = 0; index < keys.Length; index++)
            {
                string target = keys[index];
                var button = ReferenceButton(delegate { NavigateReference(target, target == LivePage); }, 11);
                var row = Stack(true);
                row.Children.Add(ReferenceIcon(icons[index], 21, new Thickness(0, 0, 7, 0)));
                var label = ReferenceLabel(titles[index], 13, ReferenceText, true);
                label.FontWeight = FontWeights.Medium;
                label.FontFamily = LiveUiFont.ForWeight(FontWeights.Medium);
                label.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(label);
                button.Content = row;
                button.Height = 40;
                button.Padding = new Thickness(11, 0, 11, 0);
                button.Margin = new Thickness(0, 0, 2, 0);
                button.BorderThickness = new Thickness(0);
                bool selected = target == page || target == LivePage && page == ConfigurePage;
                button.Background = selected ? ReferenceGradient("#71617B", "#64556E", 90) : Brushes.Transparent;
                if (selected && !quietLiveAppearance)
                    button.Effect = new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#8050FD"), BlurRadius = 16,
                        ShadowDepth = 0, Opacity = .35 };
                if (referenceHeader != null && referenceHeader.ActualWidth > 0 && referenceHeader.ActualWidth < 970)
                {
                    row.Children[0].Visibility = Visibility.Collapsed;
                    button.Padding = new Thickness(7, 0, 7, 0);
                }
                AutomationProperties.SetName(button, titles[index]);
                AutomationProperties.SetAutomationId(button, "ReferenceNav-" + icons[index]);
                top.Children.Add(button);
            }
            bool russian = CurrentLayout() == "RU";
            referenceEnChip.Background = !russian ? ReferenceGradient("#71617B", "#64556E", 90) : Brushes.Transparent;
            referenceRuChip.Background = russian ? ReferenceGradient("#71617B", "#64556E", 90) : Brushes.Transparent;
            referenceLayoutEn.FontWeight = !russian ? FontWeights.Medium : FontWeights.Normal;
            referenceLayoutEn.FontFamily = LiveUiFont.ForWeight(referenceLayoutEn.FontWeight);
            referenceLayoutRu.FontWeight = russian ? FontWeights.Medium : FontWeights.Normal;
            referenceLayoutRu.FontFamily = LiveUiFont.ForWeight(referenceLayoutRu.FontWeight);
            AutomationProperties.SetName(!russian ? referenceEnChip : referenceRuChip, "Текущая раскладка: " + (russian ? "RU" : "EN"));
            AutomationProperties.SetName(russian ? referenceEnChip : referenceRuChip, russian ? "EN" : "RU");
        }

        private void ApplyReferenceAppearance()
        {
            if (referenceHeader != null)
                referenceHeader.Background = quietLiveAppearance ? Ui.Brush("#25242B") : ReferenceGradient("#26242C", "#1C1B23", 90);
            if (referenceHome != null)
                referenceHome.Background = quietLiveAppearance ? Ui.Brush("#191820") : ReferenceHomeSurface();
            if (referenceWave != null)
                referenceWave.Visibility = quietLiveAppearance ? Visibility.Collapsed : Visibility.Visible;
            if (referenceTheme != null)
            {
                string name = quietLiveAppearance ? "Вернуть декоративный фон" : "Упростить фон";
                referenceTheme.ToolTip = name;
                AutomationProperties.SetName(referenceTheme, name);
            }
        }

        private static Border ReferenceLanguage(string code, out TextBlock label)
        {
            label = ReferenceLabel(code, 13, ReferenceText, false);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            return new Border { Child = label, Width = 35, Height = 27, CornerRadius = new CornerRadius(8),
                Margin = new Thickness(1, 0, 1, 0) };
        }

        private static Button ReferenceHeaderButton(string name, string glyph, Action action)
        {
            var b = ReferenceButton(action, 7);
            b.Content = ReferenceIcon(glyph, glyph == "minimize" || glyph == "maximize" || glyph == "close" ? 13 : 20);
            b.Width = 29; b.Height = 31;
            b.Background = Brushes.Transparent; b.BorderThickness = new Thickness(0);
            b.Margin = new Thickness(2, 0, 0, 0);
            AutomationProperties.SetName(b, name);
            b.ToolTip = name;
            return b;
        }

        private FrameworkElement BuildReferenceHome()
        {
            var catalog = Request("lab-catalog", "desktopProfile", labDesktopProfile);
            var classes = catalog.Items("classes").Where(UsesSelectedDesktop).ToArray();
            var distinctConfirmed = new HashSet<string>(library.Items("liveSkills").Where(item => item.B("confirmed")).Select(item => item.S("key")));
            var main = new Grid { Background = ReferenceHomeSurface() };
            referenceHome = main;

            var wave = ReferenceHeroWave();
            referenceWave = wave;
            main.Children.Add(wave);

            var pageContent = Stack();
            pageContent.Margin = new Thickness(35, 47, 27, 33);
            pageContent.MaxWidth = 1120;
            var hero = Stack();
            hero.Margin = new Thickness(18, 0, 0, 0);
            hero.MinHeight = 157;
            var headline = ReferenceLabel("Практика без мыши", 40, ReferenceText, true);
            hero.Children.Add(headline);
            var lead = ReferenceLabel("Управляйте системой, приложениями и рабочими задачами\nтолько с клавиатуры.", 18, ReferenceMuted, false);
            lead.Margin = new Thickness(0, 8, 0, 0);
            lead.MaxWidth = 600;
            lead.HorizontalAlignment = HorizontalAlignment.Left;
            hero.Children.Add(lead);
            pageContent.Children.Add(hero);
            var profile = BuildDesktopProfileChoice(delegate { center.Content = BuildReferenceHome(); });
            profile.Margin = new Thickness(18, 0, 0, 14);
            pageContent.Children.Add(profile);

            var grid = new Grid();
            pageContent.Children.Add(grid);
            liveCatalogButtons.Clear();
            var cards = new List<FrameworkElement>();
            var density = new List<Action<bool>>();
            foreach (var item in classes)
            {
                string app = item.S("id");
                string[] shortcuts = item.Items("shortcuts").Select(x => x.S("id")).Where(x => x.Length > 0).Distinct().ToArray();
                int completed = shortcuts.Count(id => distinctConfirmed.Contains("lab/" + app + "/" + id));
                int percent = shortcuts.Length > 0 ? (int)Math.Round(completed * 100.0 / shortcuts.Length) : 0;
                Action<bool> applyDensity;
                var card = BuildReferenceCard(app, item.S("title"), percent, completed, shortcuts.Length, app == labApp, out applyDensity);
                density.Add(applyDensity);
                liveCatalogButtons[app] = card;
                var tile = Stack();
                tile.Children.Add(card);
                var actions = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
                var configure = ReferenceButton(delegate { OpenLiveConfigure(app); }, 8);
                configure.Content = "Настроить";
                configure.Padding = new Thickness(12, 7, 12, 7);
                configure.Margin = new Thickness(0, 0, 7, 0);
                configure.Foreground = ReferenceText;
                configure.Background = Ui.Brush("#303039");
                AutomationProperties.SetName(configure, "Настроить: " + item.S("title"));
                AutomationProperties.SetAutomationId(configure, "LiveConfigure-" + app);
                actions.Children.Add(configure);
                if (HasCompatibleLiveDraft(catalog, app))
                {
                    var resume = ReferenceButton(delegate { LaunchLiveCard(app, true); }, 8);
                    resume.Content = "Продолжить";
                    resume.Margin = new Thickness(0);
                    resume.Padding = new Thickness(12, 7, 12, 7);
                    resume.HorizontalAlignment = HorizontalAlignment.Left;
                    resume.Background = Ui.Brush("#48414F");
                    resume.Foreground = ReferenceText;
                    AutomationProperties.SetName(resume, "Продолжить: " + item.S("title"));
                    AutomationProperties.SetAutomationId(resume, "LiveResume-" + app);
                    actions.Children.Add(resume);
                }
                tile.Children.Add(actions);
                density.Add(delegate(bool compact)
                {
                    actions.Margin = new Thickness(0, compact ? 4 : 7, 0, 0);
                    foreach (Button action in actions.Children)
                        action.Padding = new Thickness(11, compact ? 4 : 7, 11, compact ? 4 : 7);
                });
                cards.Add(tile);
            }

            if (cards.Count > 0)
            {
                Action<int, bool> arrange = delegate(int columns, bool compact)
                {
                    foreach (var card in cards)
                    {
                        var parent = card.Parent as Panel;
                        if (parent != null) parent.Children.Remove(card);
                    }
                    grid.Children.Clear();
                    grid.ColumnDefinitions.Clear();
                    grid.RowDefinitions.Clear();
                    if (columns == 3 && cards.Count == 5 && !compact)
                    {
                        var upper = ReferenceCardRow(new[] { 314, 20, 296, 20, 294 });
                        var lower = ReferenceCardRow(new[] { 9, 344, 24, 356, 211 });
                        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        Grid.SetRow(upper, 0);
                        Grid.SetRow(lower, 1);
                        lower.Margin = new Thickness(0, 22, 0, 0);
                        grid.Children.Add(upper);
                        grid.Children.Add(lower);
                        for (int i = 0; i < cards.Count; i++)
                        {
                            var card = cards[i];
                            var panel = i < 3 ? upper : lower;
                            card.MinHeight = i < 3 ? 273 : 255;
                            card.Margin = new Thickness(0);
                            Grid.SetColumn(card, i < 3 ? i * 2 : (i - 3) * 2 + 1);
                            Grid.SetRow(card, 0);
                            panel.Children.Add(card);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
                        for (int i = 0; i < (cards.Count + columns - 1) / columns; i++)
                            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        for (int i = 0; i < cards.Count; i++)
                        {
                            var card = cards[i];
                            int col = i % columns, row = i / columns;
                            card.MinHeight = compact ? 0 : 232;
                            card.Margin = new Thickness(0, 0, col == columns - 1 ? 0 : compact ? 12 : 18, compact ? 10 : 18);
                            Grid.SetColumn(card, col);
                            Grid.SetRow(card, row);
                            grid.Children.Add(card);
                        }
                    }
                };
                int currentColumns = 0;
                bool wasCompact = false;
                Action reflow = delegate
                {
                    if (main.ActualWidth <= 0 || main.ActualHeight <= 0) return;
                    bool compact = main.ActualHeight <= 705;
                    int requested = main.ActualWidth >= (compact ? 850 : 875) ? 3 : main.ActualWidth >= 535 ? 2 : 1;
                    if (requested == currentColumns && compact == wasCompact) return;
                    currentColumns = requested;
                    wasCompact = compact;
                    var focused = Keyboard.FocusedElement as FrameworkElement;
                    bool cardHadFocus = focused != null && cards.Any(tile => tile.IsAncestorOf(focused));
                    pageContent.Margin = compact ? new Thickness(20, 15, 20, 12) : new Thickness(35, 47, 27, 33);
                    hero.Margin = compact ? new Thickness(2, 0, 0, 7) : new Thickness(18, 0, 0, 0);
                    hero.MinHeight = compact ? 0 : 157;
                    headline.FontSize = compact ? 28 : 40;
                    headline.LineHeight = headline.FontSize * 1.26;
                    lead.FontSize = compact ? 14 : 18;
                    lead.LineHeight = lead.FontSize * 1.26;
                    lead.Margin = new Thickness(0, compact ? 3 : 8, 0, 0);
                    lead.MaxWidth = compact ? 820 : 600;
                    lead.Text = compact ? "Управляйте системой, приложениями и рабочими задачами только с клавиатуры." :
                        "Управляйте системой, приложениями и рабочими задачами\nтолько с клавиатуры.";
                    profile.Margin = compact ? new Thickness(2, 0, 0, 7) : new Thickness(18, 0, 0, 14);
                    ConfigureDesktopDensity(profile, compact);
                    foreach (var set in density) set(compact);
                    arrange(requested, compact);
                    if (cardHadFocus) focused.Focus();
                };
                arrange(3, false);
                main.SizeChanged += delegate { reflow(); };
                main.Loaded += delegate { reflow(); };
            }
            else
            {
                pageContent.Children.Add(ReferenceLabel("Каталог недоступен. Откройте Live, чтобы повторить загрузку.", 17, ReferenceMuted, false));
            }

            var scroll = new ScrollViewer { Content = pageContent, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            ApplyLiveScrollStyle(scroll);
            main.Children.Add(scroll);
            AutomationProperties.SetAutomationId(scroll, "HomeDashboard");
            AutomationProperties.SetName(scroll, "Практика без мыши");
            AutomationProperties.SetAutomationId(hero.Children[0], "HomeTitle");
            ApplyReferenceAppearance();
            return main;
        }

        private static Grid ReferenceCardRow(int[] weights)
        {
            var row = new Grid();
            foreach (int weight in weights)
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weight, GridUnitType.Star) });
            return row;
        }

        private Button BuildReferenceCard(string app, string title, int percent, int completed, int total, bool selected, out Action<bool> applyDensity)
        {
            bool compactCard = app == "terminal" || app == "workday";
            var body = Stack();
            var emblem = new Border
            {
                Width = compactCard ? 62 : 70, Height = compactCard ? 62 : 70, CornerRadius = new CornerRadius(17), HorizontalAlignment = HorizontalAlignment.Left,
                Background = app == "windows" ? ReferenceGradient("#21557D", "#183855", 70) :
                    app == "ubuntu" ? ReferenceGradient("#823E50", "#632831", 70) :
                    app == "workday" ? Ui.Brush("#23423D") : app == "terminal" ? Ui.Brush("#3C375E") : Ui.Brush("#303057"),
                Child = ReferenceIcon(app, 46)
            };
            var identity = new DockPanel();
            DockPanel.SetDock(emblem, Dock.Top);
            identity.Children.Add(emblem);
            var primary = ReferenceLabel(title, 27, ReferenceText, true);
            primary.Margin = new Thickness(0, 8, 0, 0);
            identity.Children.Add(primary);
            body.Children.Add(identity);
            string summary = app == "ubuntu" ? "Работа в Linux: окна, файлы, приложения, система." :
                app == "windows" ? "Работа в Windows 10: Пуск, окна, задачи и рабочие столы." :
                app == "chrome" ? "Эффективная работа в браузере без мыши." :
                app == "code" ? "Навигация, редактирование и управление проектом." :
                app == "terminal" ? "Быстрая работа в консоли: команды, файлы, процессы." :
                "Реальные сценарии: переключение, поиск, организация.";
            var secondary = ReferenceLabel(summary, 17, ReferenceMuted, false);
            secondary.Margin = new Thickness(0, 6, 0, 0);
            secondary.MinHeight = compactCard ? 44 : 49;
            body.Children.Add(secondary);
            var row = Stack(true);
            row.Margin = new Thickness(0, 13, 0, 9);
            row.Children.Add(ReferenceIcon("progress", 21, new Thickness(4, 0, 12, 0), Ui.Brush("#AD96FF")));
            row.Children.Add(ReferenceLabel("Прогресс: ", 17, ReferenceMuted, false));
            row.Children.Add(ReferenceLabel(percent + "%", 17, ReferenceText, true));
            body.Children.Add(row);
            var meter = ReferenceProgress(percent);
            body.Children.Add(meter);

            var button = ReferenceButton(delegate { LaunchLiveCard(app, false); }, 19);
            applyDensity = delegate(bool compact)
            {
                DockPanel.SetDock(emblem, compact ? Dock.Left : Dock.Top);
                emblem.Margin = compact ? new Thickness(0, 0, 8, 0) : new Thickness(0);
                emblem.Width = emblem.Height = compact ? 35 : compactCard ? 62 : 70;
                emblem.CornerRadius = new CornerRadius(compact ? 9 : 17);
                var drawing = emblem.Child as FrameworkElement;
                if (drawing != null) drawing.Width = drawing.Height = compact ? 24 : 46;
                primary.FontSize = compact ? 19 : 27;
                primary.LineHeight = primary.FontSize * 1.26;
                primary.VerticalAlignment = compact ? VerticalAlignment.Center : VerticalAlignment.Top;
                primary.Margin = new Thickness(0, compact ? 0 : 8, 0, 0);
                secondary.FontSize = compact ? 13 : 17;
                secondary.LineHeight = secondary.FontSize * 1.26;
                secondary.MinHeight = compact ? 35 : compactCard ? 44 : 49;
                secondary.Margin = new Thickness(0, compact ? 4 : 6, 0, 0);
                row.Margin = new Thickness(0, compact ? 3 : 13, 0, compact ? 4 : 9);
                ((FrameworkElement)row.Children[0]).Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
                foreach (var label in row.Children.OfType<TextBlock>())
                {
                    label.FontSize = compact ? 13 : 17;
                    label.LineHeight = label.FontSize * 1.24;
                }
                meter.Height = compact ? 6 : 12;
                button.Padding = compact ? new Thickness(12, 9, 12, 9) :
                    new Thickness(30, 18, 26, compactCard ? 16 : 19);
            };
            button.Content = body;
            button.Cursor = Cursors.Hand;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.VerticalContentAlignment = VerticalAlignment.Top;
            button.Padding = new Thickness(30, 18, 26, compactCard ? 16 : 19);
            button.BorderThickness = new Thickness(selected ? 2 : 1);
            button.BorderBrush = selected ? Ui.Brush("#A89BB9") : Ui.Brush("#504C58");
            button.Background = selected ? ReferenceGradient("#403849", "#2D2934", 60) :
                ReferenceGradient("#25242C", "#222129", 80);
            AutomationProperties.SetName(button, title + ". " + summary + " Прогресс: " + percent + "%.");
            AutomationProperties.SetAutomationId(button, "HomeClass-" + app);
            button.ToolTip = "Подтверждено сочетаний в текущей раскладке: " + completed + " из " + total +
                (app == "code" ? ". Начать маршрут VS Code: 40 действий без таймера." : ". Начать тренировку сразу.");
            return button;
        }

        private static Grid ReferenceProgress(int percent)
        {
            var meter = new Grid { Height = 12 };
            meter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(percent, GridUnitType.Star) });
            meter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - percent, GridUnitType.Star) });
            var track = new Border { Background = Ui.Brush("#413D48"), CornerRadius = new CornerRadius(6) };
            Grid.SetColumnSpan(track, 2);
            meter.Children.Add(track);
            if (percent > 0) meter.Children.Add(new Border { Background = ReferenceGradient("#A89BB9", "#887993", 90), CornerRadius = new CornerRadius(6) });
            return meter;
        }

        private static TextBlock ReferenceLabel(string text, double size, Brush foreground, bool strong)
        {
            return new TextBlock { Text = text, FontSize = size, FontFamily = strong ? LiveUiFont.ForWeight(FontWeights.Bold) : LiveUiFont.Sans, Foreground = foreground, TextWrapping = TextWrapping.Wrap,
                FontWeight = strong ? FontWeights.Bold : FontWeights.Normal, LineHeight = size * 1.26,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        }

        private static Brush ReferenceGradient(string first, string second, double angle)
        {
            var gradient = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(first), (Color)ColorConverter.ConvertFromString(second), angle);
            gradient.Freeze();
            return gradient;
        }

        private static Brush ReferenceHomeSurface()
        {
            return ReferenceGradient("#191820", "#201D26", 60);
        }

        private static FrameworkElement ReferenceHeroWave()
        {
            var surface = new Grid { IsHitTestVisible = false, Height = 245, VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true };
            var glow = new Rectangle { Fill = new RadialGradientBrush(
                (Color)ColorConverter.ConvertFromString("#305B486B"), Colors.Transparent)
            { Center = new Point(.83, .37), GradientOrigin = new Point(.83, .37), RadiusX = .49, RadiusY = .77 } };
            surface.Children.Add(glow);
            var wave = new Path
            {
                Data = Geometry.Parse("M 0,1 C 0.15,0.78 0.195,0.80 0.31,0.72 C 0.58,0.58 0.59,0.05 0.9,0.02 C 0.94,0.02 0.97,0.05 1,0.1 L 1,0.62 C 0.85,0.45 0.74,0.62 0.62,0.72 C 0.48,0.85 0.39,0.84 0.22,0.92 Z"),
                Stretch = Stretch.Fill, Margin = new Thickness(457, 27, 0, 29)
            };
            wave.Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop((Color)ColorConverter.ConvertFromString("#0C5B486B"), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString("#505F516D"), .29),
                new GradientStop((Color)ColorConverter.ConvertFromString("#3550445B"), .65),
                new GradientStop(Colors.Transparent, 1)
            }, new Point(.82, 0), new Point(.35, .94));
            surface.Children.Add(wave);
            return surface;
        }

        private static Button ReferenceButton(Action action, double radius)
        {
            var b = new Button { HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            if (action != null) b.Click += delegate { action(); };
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            var template = new ControlTemplate(typeof(Button));
            var frame = new FrameworkElementFactory(typeof(Border), "ReferenceFocusFrame");
            frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            frame.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            frame.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            frame.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            frame.AppendChild(presenter);
            template.VisualTree = frame;
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BorderBrushProperty, Ui.Brush("#A89BB9"), "ReferenceFocusFrame"));
            over.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "ReferenceFocusFrame"));
            template.Triggers.Add(over);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, Ui.Brush("#D4C8DE"), "ReferenceFocusFrame"));
            focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "ReferenceFocusFrame"));
            template.Triggers.Add(focus);
            b.Template = template;
            return b;
        }

        private static FrameworkElement ReferenceIcon(string icon, double size, Thickness? margin = null, Brush color = null)
        {
            var drawing = new DrawingGroup();
            Brush white = color ?? ReferenceText;
            var bounds = new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 48, 48)));
            drawing.Children.Add(bounds);
            Action<string, Brush> fill = (path, brush) => drawing.Children.Add(new GeometryDrawing(brush, null, Geometry.Parse(path)));
            Action<string, Brush, double> stroke = (path, brush, width) =>
            {
                var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                drawing.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse(path)));
            };
            Action<double, double, double, Brush> circle = (cx, cy, radius, brush) =>
                drawing.Children.Add(new GeometryDrawing(brush, null, new EllipseGeometry(new Point(cx, cy), radius, radius)));

            switch (icon)
            {
                case "brand":
                    stroke("M 15,13 L 5,24 15,34 M 33,13 L 43,24 33,34 M 29,9 L 19,39", white, 5.3); break;
                case "windows":
                    fill("M6,10 L22,7 L22,23 L6,23 Z M25,7 L43,4 L43,23 L25,23 Z M6,26 L22,26 L22,42 L6,39 Z M25,26 L43,26 L43,45 L25,42 Z", Ui.Brush("#6FC9FF"));
                    break;
                case "ubuntu":
                    var orange = Ui.Brush("#FF8415");
                    stroke("M 13,13 A 15,15 0 0 1 33,12 M 37,19 A 15,15 0 0 1 33,35 M 27,39 A 15,15 0 0 1 9,29", orange, 5.7);
                    circle(35, 6, 4.7, orange); circle(37, 40, 4.7, orange); circle(4.7, 23, 4.7, orange);
                    break;
                case "chrome":
                    fill("M 24,2 A 22,22 0 0 1 43.1,13 L 24,13 A 11,11 0 0 0 15,30 L 6,15 A 22,22 0 0 1 24,2 Z", Ui.Brush("#E64135"));
                    fill("M 43.1,13 A 22,22 0 0 1 23,46 L 33.5,29.5 A 11,11 0 0 0 24,13 Z", Ui.Brush("#F7BD13"));
                    fill("M 23,46 A 22,22 0 0 1 6,15 L 15,30 A 11,11 0 0 0 33.5,29.5 Z", Ui.Brush("#16A660"));
                    circle(24, 24, 11, Ui.Brush("#EAF5FA")); circle(24, 24, 8.6, Ui.Brush("#1D7AE8")); break;
                case "code":
                    fill("M 34,3 L 46,9 L 46,39 L 34,45 L 7,30 L 2,34 L 0,31 L 9,23 L 0,16 L 3,11 L 9,16 Z M 34,14 L 17,23 34,34 Z", Ui.Brush("#0E8DD1"));
                    fill("M 34,3 L 46,9 L 46,39 L 34,45 Z", Ui.Brush("#27B6ED")); break;
                case "terminal": stroke("M 7,12 L 20,24 7,36 M 24,36 L 41,36", white, 4.5); break;
                case "workday": stroke("M 8,16 H 40 Q 42,16 42,19 V 35 Q 42,38 39,38 H 9 Q 6,38 6,35 V 19 Q 6,16 8,16 Z M 16,16 V 10 Q 16,7 19,7 H 29 Q 32,7 32,10 V 16", Ui.Brush("#55F588"), 3.8); break;
                case "home": stroke("M 5,23 L 24,7 43,23 M 10,20 L 10,41 19,41 19,29 29,29 29,41 38,41 38,20", white, 3.8); break;
                case "practice": stroke("M 38,17 A 17,17 0 1 1 29,7 M 32,20 A 9,9 0 1 1 25,15 M 24,24 L 40,8 M 39,8 L 40,2 M 40,8 L 46,9", white, 3.7); break;
                case "learn": stroke("M 24,13 Q 16,5 5,9 V 38 Q 17,34 24,42 Q 31,34 43,38 V 9 Q 32,5 24,13 Z M 24,13 V 42", white, 3.8); break;
                case "progress": stroke("M 10,40 V 29 M 22,40 V 19 M 34,40 V 7", white, 5.9); break;
                case "gear": stroke("M24,5 V11 M24,37 V43 M5,24 H11 M37,24 H43 M11,11 L15,15 M33,33 L37,37 M11,37 L15,33 M33,15 L37,11", white, 3.2); circle(24,24,11,Ui.Brush("#9271E8")); circle(24,24,5,Ui.Brush("#342758")); break;
                case "moon": fill("M 33,37 A 19,19 0 1 1 16,7 A 15,15 0 0 0 33,37 Z", Ui.Brush("#C4AEF7")); break;
                case "minimize": stroke("M 8,26 H 39", white, 3.3); break;
                case "maximize": stroke("M 11,11 H 37 V 37 H 11 Z", white, 3.3); break;
                case "close": stroke("M 11,11 L 37,37 M 37,11 L 11,37", white, 3.3); break;
                default: circle(12, 24, 2.4, white); circle(24, 24, 2.4, white); circle(36, 24, 2.4, white); break;
            }
            drawing.Freeze();
            return new Image { Source = new DrawingImage(drawing), Width = size, Height = size, Margin = margin ?? new Thickness(0),
                IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }
    }
}
