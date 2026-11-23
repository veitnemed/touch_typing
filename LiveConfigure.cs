using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace TouchTypingStudio
{
    internal sealed partial class LiveStudio
    {
        private string liveConfiguredMissionApp, liveConfiguredMission;

        private void OpenLiveConfigure(string app)
        {
            liveReturnApp = labApp = app;
            NavigateReference(ConfigurePage, true);
        }

        private FrameworkElement BuildLiveConfigure()
        {
            var catalog = Request("lab-catalog", "desktopProfile", labDesktopProfile);
            var course = catalog.Items("classes").FirstOrDefault(item => item.S("id") == labApp);
            string title = course != null ? course.S("title") : ReferenceAppTitle();
            var stack = Stack();
            stack.MaxWidth = 590;
            stack.Margin = new Thickness(16, 17, 16, 20);
            var back = ReferenceButton(ReturnToLiveCatalog, 8);
            back.Content = "‹  К списку Live";
            back.Padding = new Thickness(10, 6, 10, 6);
            back.Foreground = ReferenceText;
            back.Background = Ui.Brush("#322650");
            back.HorizontalAlignment = HorizontalAlignment.Left;
            AutomationProperties.SetAutomationId(back, "LiveConfigureBack");
            stack.Children.Add(back);
            var heading = ReferenceLabel("Настроить · " + title, 25, ReferenceText, true);
            heading.Margin = new Thickness(0, 12, 0, 10);
            AutomationProperties.SetAutomationId(heading, "LiveConfigureHeading");
            stack.Children.Add(heading);

            var form = Stack();
            var choiceGroup = ConfigureGroup(form, "Упражнение");
            var mission = LiveSelect("Поручение", "LabMission", new string[0], -1);
            var level = LiveSelect("Сложность", "LabLevel", new[] { "Базовый", "Средний", "Сложный" }, labLevel);
            ConfigureField(choiceGroup, "Поручение", mission);
            ConfigureField(choiceGroup, "Сложность", level);
            var guideGroup = ConfigureGroup(form, "Как тренироваться");
            var mode = LiveSelect("Режим подсказок", "LabMode", new[] { "Самостоятельно", "По шагам" }, labMode == "sandbox" ? 1 : 0);
            ConfigureField(guideGroup, "Подсказки", mode);
            var durationGroup = ConfigureGroup(form, "Длительность");
            var actions = LiveSelect("Лимит действий", "LabDuration",
                new[] { "Без лимита действий", "20 действий", "40 действий", "60 действий", "100 действий" }, lastLabLimit);
            var timer = LiveSelect("Таймер", "LabTimer", new[] { "Без таймера", "3 минуты", "10 минут" }, lastLabTimer);
            ConfigureField(durationGroup, "Действия", actions);
            ConfigureField(durationGroup, "Таймер", timer);
            stack.Children.Add(ReferenceLessonPanel(form, new Thickness(17, 8, 17, 13), 14));
            Option[] missions = new Option[0];
            Action refresh = delegate
            {
                missions = new[] { new Option("", "Подобрать поручение") }.Concat(catalog.Items("missions")
                    .Where(item => item.S("app") == labApp && item.I("level") == level.SelectedIndex && UsesSelectedDesktop(item))
                    .Select(item => new Option(item.S("id"), item.S("title")))).ToArray();
                mission.ItemsSource = missions;
                mission.SelectedIndex = Pick(missions, liveConfiguredMissionApp == labApp ? liveConfiguredMission : "");
            };
            level.SelectionChanged += delegate { refresh(); };
            refresh();
            var start = ReferenceButton(delegate
            {
                labLevel = Math.Max(0, level.SelectedIndex);
                labMode = mode.SelectedIndex == 0 ? "drill" : "sandbox";
                lastLabLimit = Math.Max(0, actions.SelectedIndex);
                lastLabTimer = Math.Max(0, timer.SelectedIndex);
                liveConfiguredMissionApp = labApp;
                liveConfiguredMission = Get(mission);
                StartLabAttempt(liveConfiguredMission, false);
            }, 10);
            start.Content = "Начать";
            start.IsDefault = true;
            start.IsEnabled = course != null;
            start.Padding = new Thickness(25, 10, 25, 10);
            start.Margin = new Thickness(0, 13, 0, 0);
            start.HorizontalAlignment = HorizontalAlignment.Right;
            start.Background = ReferenceGradient("#9070FA", "#7149DF", 70);
            start.Foreground = ReferenceText;
            AutomationProperties.SetAutomationId(start, "LabStart");
            beginControl = start;
            stack.Children.Add(start);

            var utility = new Expander { Header = "Клавиатура, Fn и дополнительные упражнения", Foreground = ReferenceMuted,
                Margin = new Thickness(0, 10, 0, 0) };
            var support = Stack();
            var keyboardProfile = LiveSelect("Профиль клавиатуры", "LiveHardwareKeyboard", new[] { "MateBook D16", "Стандартная" },
                library.S("keyboardProfile") == "standard" ? 1 : 0);
            var fn = LiveSelect("Режим Fn", "LiveHardwareFn", new[] { "Мультимедиа без Fn", "F1–F12 без Fn" },
                library.S("fnMode") == "functions" ? 1 : 0);
            ConfigureField(support, "Клавиатура", keyboardProfile);
            ConfigureField(support, "Fn", fn);
            keyboardProfile.SelectionChanged += delegate { SaveLiveOption("keyboardProfile", keyboardProfile.SelectedIndex == 1 ? "standard" : "matebook-d16"); };
            fn.SelectionChanged += delegate { SaveLiveOption("fnMode", fn.SelectedIndex == 1 ? "functions" : "hotkeys"); };
            support.Children.Add(ActionButton("Отдельные сочетания", delegate { legacyLive = true; ShowPage(LivePage); }));
            support.Children.Add(ActionButton("Редактор упражнений", delegate { NavigateReference(EditorPage, false); }));
            utility.Content = support;
            stack.Children.Add(utility);

            var scroll = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = ReferenceHomeSurface(), Focusable = false };
            ApplyLiveScrollStyle(scroll);
            AutomationProperties.SetAutomationId(scroll, "LiveConfigurePanel");
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(delegate
            { if (ReferenceEquals(center.Content, scroll)) back.Focus(); }));
            return scroll;
        }

        private void SaveLiveOption(string key, string value)
        {
            var updated = Request("settings", key, value);
            if (updated.Exists && string.IsNullOrWhiteSpace(engine.Notice)) library = updated;
        }

        private static StackPanel ConfigureGroup(StackPanel form, string title)
        {
            var group = Stack();
            var heading = ReferenceLabel(title, 16, ReferenceText, true);
            heading.Margin = new Thickness(0, 9, 0, 3);
            group.Children.Add(heading);
            form.Children.Add(group);
            return group;
        }

        private static void ConfigureField(Panel group, string label, Control editor)
        {
            var text = ReferenceLabel(label, 12, ReferenceMuted, false);
            text.Margin = new Thickness(0, 5, 0, 3);
            group.Children.Add(text);
            group.Children.Add(editor);
        }

        private static ComboBox LiveSelect(string title, string id, string[] options, int index)
        {
            var box = new ComboBox
            {
                Style = null, FontFamily = LiveUiFont.Sans, FontSize = 14,
                Foreground = ReferenceText, Background = Ui.Brush("#27203E"), BorderBrush = Ui.Brush("#544474"),
                MinHeight = 34, MaxDropDownHeight = 216, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ItemsSource = options.Select((value, i) => new Option(i.ToString(), value)).ToArray(),
                SelectedIndex = index, Template = LiveSelectTemplate(), ItemContainerStyle = LiveSelectItemStyle()
            };
            AutomationProperties.SetName(box, title);
            AutomationProperties.SetAutomationId(box, id);
            return box;
        }

        private static Binding LiveSelectBind(string path)
        { return new Binding(path) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) }; }

        private static ControlTemplate LiveSelectTemplate()
        {
            var template = new ControlTemplate(typeof(ComboBox));
            var grid = new FrameworkElementFactory(typeof(Grid));
            var toggle = new FrameworkElementFactory(typeof(ToggleButton), "LivePickerToggle");
            toggle.SetValue(Control.FocusableProperty, false);
            toggle.SetValue(KeyboardNavigation.IsTabStopProperty, false);
            toggle.SetValue(ToggleButton.ClickModeProperty, ClickMode.Press);
            var open = LiveSelectBind("IsDropDownOpen");
            open.Mode = BindingMode.TwoWay;
            toggle.SetBinding(ToggleButton.IsCheckedProperty, open);
            var buttonTemplate = new ControlTemplate(typeof(ToggleButton));
            var tint = new FrameworkElementFactory(typeof(Border), "LivePickerEdge");
            tint.SetValue(Border.BackgroundProperty, Ui.Brush("#292041"));
            tint.SetValue(Border.BorderBrushProperty, Ui.Brush("#594679"));
            tint.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            tint.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            var arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.SetValue(TextBlock.TextProperty, "⌄");
            arrow.SetValue(TextBlock.FontSizeProperty, 17.0);
            arrow.SetValue(TextBlock.ForegroundProperty, Ui.Brush("#EBDEFF"));
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            tint.AppendChild(arrow);
            buttonTemplate.VisualTree = tint;
            foreach (DependencyProperty property in new[] { UIElement.IsMouseOverProperty, ToggleButton.IsCheckedProperty })
            {
                var over = new Trigger { Property = property, Value = true };
                over.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Brush("#352852"), "LivePickerEdge"));
                over.Setters.Add(new Setter(Border.BorderBrushProperty, Ui.Brush("#AD86FF"), "LivePickerEdge"));
                buttonTemplate.Triggers.Add(over);
            }
            toggle.SetValue(Control.TemplateProperty, buttonTemplate);
            grid.AppendChild(toggle);
            var label = new FrameworkElementFactory(typeof(ContentPresenter));
            label.SetBinding(ContentPresenter.ContentProperty, LiveSelectBind("SelectionBoxItem"));
            label.SetValue(UIElement.IsHitTestVisibleProperty, false);
            label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            label.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 33, 0));
            grid.AppendChild(label);
            var frame = new FrameworkElementFactory(typeof(Border), "LivePickerKeyboardFocus");
            frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            frame.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            frame.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            frame.SetValue(UIElement.IsHitTestVisibleProperty, false);
            grid.AppendChild(frame);

            var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.FocusableProperty, false);
            popup.SetBinding(Popup.IsOpenProperty, LiveSelectBind("IsDropDownOpen"));
            var panel = new FrameworkElementFactory(typeof(Border));
            panel.SetBinding(FrameworkElement.MinWidthProperty, LiveSelectBind("ActualWidth"));
            panel.SetBinding(FrameworkElement.MaxHeightProperty, LiveSelectBind("MaxDropDownHeight"));
            panel.SetValue(Border.BackgroundProperty, Ui.Brush("#231A39"));
            panel.SetValue(Border.BorderBrushProperty, Ui.Brush("#9D72F2"));
            panel.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            panel.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            panel.SetValue(Border.PaddingProperty, new Thickness(3));
            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scroll.SetValue(KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Contained);
            var items = new FrameworkElementFactory(typeof(ItemsPresenter));
            scroll.AppendChild(items);
            panel.AppendChild(scroll); popup.AppendChild(panel); grid.AppendChild(popup);
            template.VisualTree = grid;
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, Ui.Brush("#DCC7FF"), "LivePickerKeyboardFocus"));
            template.Triggers.Add(focus);
            return template;
        }

        private static Style LiveSelectItemStyle()
        {
            var template = new ControlTemplate(typeof(ComboBoxItem));
            var box = new FrameworkElementFactory(typeof(Border), "ItemEdge");
            box.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            box.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            box.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            box.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            box.SetValue(Border.PaddingProperty, new Thickness(8, 4, 8, 4));
            var item = new FrameworkElementFactory(typeof(ContentPresenter));
            item.SetValue(ContentPresenter.ContentSourceProperty, "Content");
            box.AppendChild(item); template.VisualTree = box;
            var chosen = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            chosen.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Brush("#523675"), "ItemEdge"));
            template.Triggers.Add(chosen);
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, Ui.Brush("#986ADB"), "ItemEdge"));
            template.Triggers.Add(hover);
            var focus = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, Ui.Brush("#E3D4FF"), "ItemEdge"));
            template.Triggers.Add(focus);
            var keyboard = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            keyboard.Setters.Add(new Setter(Border.BorderBrushProperty, Ui.Brush("#F4E6FF"), "ItemEdge"));
            template.Triggers.Add(keyboard);
            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.ForegroundProperty, ReferenceText));
            return style;
        }
    }
}
