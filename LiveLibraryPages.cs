using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace TouchTypingStudio
{
    internal sealed partial class LiveStudio
    {
        private static readonly string[] EditorFingerNames =
        {
            "Авто", "Левый мизинец", "Левый безымянный", "Левый средний", "Левый указательный",
            "Правый указательный", "Правый средний", "Правый безымянный", "Правый мизинец",
            "Левый большой", "Правый большой"
        };

        private string editorSection = "Сочетания", editorShortcutId = "", editorSetId = "";
        private string editorNotice = "";

        private FrameworkElement BuildEditor()
        {
            var page = LibraryPage("Редактор", "Сочетания Live, тексты и настройки Windows");
            var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 13) };
            foreach (string section in new[] { "Сочетания", "Тексты и символы", "Раскладка Windows" })
            {
                string choice = section;
                tabs.Children.Add(ActionButton(choice, delegate
                {
                    editorSection = choice;
                    editorNotice = "";
                    ShowPage("Редактор");
                }, choice == editorSection));
            }
            page.Children.Add(tabs);
            if (editorSection == "Тексты и символы") page.Children.Add(BuildTextSetEditor());
            else if (editorSection == "Раскладка Windows") page.Children.Add(BuildLayoutEditor());
            else page.Children.Add(BuildShortcutEditor());
            return LibraryViewport(page);
        }

        private FrameworkElement BuildShortcutEditor()
        {
            var panel = Stack();
            var items = library.Items("skills").ToArray();
            CollectionField(panel, "Сочетание");
            var selector = LibraryCombo("Сочетание", new[] { "Новое сочетание" }.Concat(items.Select(item =>
            {
                var skill = item.D("skill");
                return skill.S("group") + "  ·  " + skill.S("title") + (skill.B("hidden") ? "  ·  скрыто" : "");
            })));
            panel.Children.Add(selector);

            var form = Stack();
            form.Margin = new Thickness(0, 14, 0, 0);
            panel.Children.Add(form);
            var title = LibraryInput("Название сочетания", 80);
            var group = LibraryInput("Группа сочетания", 50);
            var keys = LibraryInput("Клавиши сочетания", 250);
            form.Children.Add(LibraryPair(LibraryField("Название", title), LibraryField("Группа", group)));
            CollectionField(form, "Клавиши");
            form.Children.Add(keys);
            form.Children.Add(LibraryNote("Например, RCtrl+Shift+V или Ctrl+T → Ctrl+W."));
            var commandRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            form.Children.Add(commandRow);
            var fingers = new WrapPanel { Margin = new Thickness(0, 0, 0, 9) };
            form.Children.Add(fingers);
            var notice = Label(editorNotice, 12, Accent);
            form.Children.Add(notice);
            var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            form.Children.Add(actions);
            var assignments = new Dictionary<string, ComboBox>(StringComparer.Ordinal);
            var recorded = new List<string>();
            var held = new HashSet<string>();
            bool recording = false;
            string inspectedKeys = "";
            Func<string> assignmentsText = delegate
            {
                return string.Join("\n", assignments.Where(pair => pair.Value.SelectedIndex > 0)
                    .Select(pair => pair.Key + "=" + (pair.Value.SelectedIndex - 1).ToString(CultureInfo.InvariantCulture)));
            };
            Action<string> loadFingers = delegate(string source)
            {
                assignments.Clear();
                fingers.Children.Clear();
                inspectedKeys = "";
                if (string.IsNullOrWhiteSpace(keys.Text)) return;
                var inspected = Request("inspect", "keys", keys.Text, "fingers", source);
                if (!inspected.Exists) return;
                inspectedKeys = keys.Text;
                foreach (var choice in inspected.Items("choices"))
                {
                    string key = choice.S("key");
                    var picker = LibraryCombo("Палец для " + EditorPhysicalLabel(key), EditorFingerNames);
                    picker.Width = 154;
                    picker.SelectedIndex = choice.B("custom") ? Math.Max(0, Math.Min(EditorFingerNames.Length - 1, choice.I("finger") + 1)) : 0;
                    var field = LibraryField(EditorPhysicalLabel(key), picker);
                    field.Margin = new Thickness(0, 0, 9, 0);
                    assignments[key] = picker;
                    fingers.Children.Add(field);
                }
            };
            var recordButton = ActionButton("Записать нажатием", delegate
            {
                recording = true;
                recorded.Clear();
                held.Clear();
                notice.Text = "Нажмите сочетание и отпустите все клавиши. Win, PrtSc и Alt+F4 введите вручную.";
            });
            commandRow.Children.Add(recordButton);
            commandRow.Children.Add(ActionButton("Настроить пальцы", delegate
            {
                loadFingers(inspectedKeys == keys.Text ? assignmentsText() : "");
                if (assignments.Count > 0) assignments.First().Value.Focus();
            }));
            recordButton.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (!recording) return;
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                e.Handled = true;
                string canonical = PhysicalKey(key);
                bool reserved = key == Key.LWin || key == Key.RWin || key == Key.PrintScreen ||
                    (key == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) != 0);
                if (key == Key.Escape || reserved || string.IsNullOrEmpty(canonical))
                {
                    recording = false;
                    held.Clear();
                    // Keep the initiating button from starting a new recording on this key release.
                    keys.Focus();
                    notice.Text = reserved ? "Введите системное сочетание вручную." : "Запись отменена.";
                    return;
                }
                if (!e.IsRepeat && held.Add(canonical)) recorded.Add(canonical);
            };
            recordButton.PreviewKeyUp += delegate(object sender, KeyEventArgs e)
            {
                if (!recording) return;
                e.Handled = true;
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (!held.Remove(PhysicalKey(key)) || held.Count != 0) return;
                recording = false;
                string gesture = string.Join("+", recorded);
                var result = Request("inspect", "keys", gesture, "fingers", "");
                if (!result.Exists)
                {
                    notice.Text = "Запишите сочетание заново или введите клавиши текстом.";
                    return;
                }
                keys.Text = result.S("keys", gesture);
                keys.Focus();
                loadFingers("");
                notice.Text = "Сочетание записано.";
            };
            recordButton.LostKeyboardFocus += delegate
            {
                if (!recording) return;
                recording = false;
                held.Clear();
                notice.Text = "Запись отменена.";
            };

            Action<int, bool> load = null;
            load = delegate(int index, bool copy)
            {
                recording = false;
                Data item = index > 0 && index <= items.Length ? items[index - 1] : library.D("__empty");
                Data skill = item.D("skill");
                string id = skill.S("id");
                editorShortcutId = id;
                title.Text = skill.S("title");
                group.Text = !skill.Exists || copy ? "Мои сочетания" : skill.S("group");
                keys.Text = skill.S("keys");
                bool editable = !skill.Exists || copy || item.B("editable");
                title.IsReadOnly = group.IsReadOnly = keys.IsReadOnly = !editable;
                commandRow.IsEnabled = editable;
                fingers.IsEnabled = editable;
                loadFingers(skill.S("fingers"));
                actions.Children.Clear();
                notice.Text = editorNotice;
                if (!editable && notice.Text.Length == 0)
                    notice.Text = "Встроенные сочетания можно скрыть или сохранить как личную копию.";
                if (editable)
                    actions.Children.Add(ActionButton("Сохранить сочетание", delegate
                    {
                        var changed = Request("shortcut-save", "id", copy ? "" : id, "title", title.Text, "group", group.Text,
                            "keys", keys.Text, "fingers", inspectedKeys == keys.Text ? assignmentsText() : "");
                        if (!changed.Exists) return;
                        editorShortcutId = copy || id.Length == 0 ? NewLibraryId(changed.Items("skills").Select(i => i.D("skill")),
                            items.Select(i => i.D("skill"))) : id;
                        UpdateEditor(changed, "Изменения сохранены.");
                    }, true));
                if (id.Length > 0 && !copy)
                {
                    bool hidden = skill.B("hidden");
                    actions.Children.Add(ActionButton(hidden ? "Показать" : "Скрыть", delegate
                    {
                        var changed = Request("shortcut-hide", "id", id, "hide", !hidden);
                        UpdateEditor(changed, hidden ? "Сочетание снова видно в Live." : "Сочетание скрыто.");
                    }));
                    if (editable)
                        actions.Children.Add(ActionButton("Удалить", delegate
                        {
                            if (!ConfirmRemoval("Удалить сочетание «" + skill.S("title") + "»?")) return;
                            var changed = Request("shortcut-delete", "id", id);
                            if (changed.Exists) editorShortcutId = "";
                            UpdateEditor(changed, "Сочетание удалено.");
                        }));
                    else if (keys.Text.Length > 0)
                        actions.Children.Add(ActionButton("Сделать личную копию", delegate
                        {
                            load(selector.SelectedIndex, true);
                            title.Focus();
                        }));
                }
            };
            selector.SelectedIndex = 1 + Array.FindIndex(items, item => item.D("skill").S("id") == editorShortcutId);
            selector.SelectionChanged += delegate { editorNotice = ""; load(selector.SelectedIndex, false); };
            load(selector.SelectedIndex, false);
            var wrapper = Stack();
            wrapper.Children.Add(LibraryCard(panel));
            var previous = library.D("typing").Items("oldShortcuts").ToArray();
            if (previous.Length > 0)
            {
                var imports = Stack();
                CollectionField(imports, "Ранее сохранённые наборы");
                foreach (var item in previous)
                {
                    string id = item.S("id");
                    imports.Children.Add(ActionButton("Импортировать «" + item.S("title") + "»", delegate
                    {
                        var changed = Request("shortcut-import", "id", id);
                        if (!changed.Exists) return;
                        editorShortcutId = NewLibraryId(changed.Items("skills").Select(i => i.D("skill")), items.Select(i => i.D("skill")));
                        UpdateEditor(changed, "Набор добавлен в Live.");
                    }));
                }
                wrapper.Children.Add(LibraryCard(imports));
            }
            return wrapper;
        }

        private FrameworkElement BuildTextSetEditor()
        {
            var panel = Stack();
            var items = library.D("typing").Items("sets").ToArray();
            CollectionField(panel, "Набор");
            var selector = LibraryCombo("Набор", new[] { "Новый набор" }.Concat(items.Select(set => set.S("title"))));
            panel.Children.Add(selector);
            var title = LibraryInput("Название набора печати", 70);
            var kind = LibraryCombo("Тип набора", new[] { "Текст или код", "Символы" });
            panel.Children.Add(LibraryPair(LibraryField("Название", title), LibraryField("Тип", kind)));
            var body = LibraryInput("Содержимое набора", 12000);
            body.Height = 185;
            body.AcceptsReturn = true;
            body.TextWrapping = TextWrapping.Wrap;
            body.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            body.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            body.VerticalContentAlignment = VerticalAlignment.Top;
            panel.Children.Add(LibraryField("Содержимое", body));
            panel.Children.Add(LibraryNote("Пустая строка разделяет фрагменты. Для символов используйте одну группу на строку."));
            var notice = Label(editorNotice, 12, Accent);
            panel.Children.Add(notice);
            var actions = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
            panel.Children.Add(actions);
            Action<int> load = delegate(int index)
            {
                Data item = index > 0 && index <= items.Length ? items[index - 1] : library.D("__empty");
                string id = item.S("id");
                string raw = id.StartsWith("set:", StringComparison.Ordinal) ? id.Substring(4) : id;
                editorSetId = id;
                title.Text = item.S("title");
                body.Text = item.S("content");
                kind.SelectedIndex = item.S("kind") == "Symbols" ? 1 : 0;
                notice.Text = editorNotice;
                actions.Children.Clear();
                actions.Children.Add(ActionButton("Сохранить набор", delegate
                {
                    var changed = Request("typing-save-set", "id", raw, "title", title.Text,
                        "symbols", kind.SelectedIndex == 1, "content", body.Text);
                    if (!changed.Exists) return;
                    editorSetId = id.Length == 0 ? NewLibraryId(changed.D("typing").Items("sets"), items) : id;
                    UpdateEditor(changed, "Набор сохранён.");
                }, true));
                if (id.Length > 0)
                {
                    actions.Children.Add(ActionButton("К упражнению", delegate { OpenTypingSet(id); }));
                    actions.Children.Add(ActionButton("Удалить", delegate
                    {
                        if (!ConfirmRemoval("Удалить набор «" + item.S("title") + "»?")) return;
                        var changed = Request("typing-delete-set", "id", raw);
                        if (changed.Exists) editorSetId = "";
                        UpdateEditor(changed, "Набор удалён.");
                    }));
                }
            };
            selector.SelectedIndex = 1 + Array.FindIndex(items, item => item.S("id") == editorSetId);
            selector.SelectionChanged += delegate { editorNotice = ""; load(selector.SelectedIndex); };
            load(selector.SelectedIndex);
            return LibraryCard(panel);
        }

        private FrameworkElement BuildLayoutEditor()
        {
            var panel = Stack();
            var forward = LibraryInput("Смена раскладки вперёд", 60);
            var backward = LibraryInput("Смена раскладки назад", 60);
            forward.Text = library.S("forward", "Super+Space").Replace("Super", "Win");
            backward.Text = library.S("backward", "Super+Space").Replace("Super", "Win");
            panel.Children.Add(LibraryPair(LibraryField("Следующая раскладка", forward),
                LibraryField("Предыдущая раскладка", backward)));
            panel.Children.Add(LibraryNote("Введите сочетания из настроек Windows, например Win+Space или Alt+Shift. Live проверит смену раскладки по фактическому вводу."));
            var notice = Label(editorNotice, 12, Accent);
            panel.Children.Add(notice);
            var actions = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
            actions.Children.Add(ActionButton("Сохранить подсказки", delegate
            {
                var changed = Request("settings", "forward", forward.Text, "backward", backward.Text);
                UpdateEditor(changed, "Подсказки сохранены.");
            }, true));
            panel.Children.Add(actions);
            return LibraryCard(panel);
        }

        private FrameworkElement BuildProgress()
        {
            var page = LibraryPage("Прогресс", "Результаты уже завершённых занятий");
            var attempts = library.Items("skills").Where(item =>
            {
                var score = item.D("score");
                return score.I("count") > 0 || score.I("substituteCount") > 0;
            }).ToArray();
            var metrics = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 10) };
            metrics.Children.Add(LibraryMetric("Навыки в практике", attempts.Length));
            metrics.Children.Add(LibraryMetric("Исходные попытки", attempts.Sum(item => item.D("score").I("count"))));
            metrics.Children.Add(LibraryMetric("Зачтено заменой", attempts.Sum(item => item.D("score").I("substituteCorrect"))));
            page.Children.Add(metrics);

            var live = Stack();
            live.Children.Add(Label("Live", 18, Ink, true));
            if (attempts.Length == 0) live.Children.Add(LibraryNote("Здесь появятся результаты после первой попытки."));
            foreach (var attempted in attempts.OrderBy(item => item.D("score").N("accuracy")).ThenBy(item => item.D("skill").S("title")))
            {
                var skill = attempted.D("skill");
                var score = attempted.D("score");
                string id = skill.S("id");
                int exact = score.I("count"), substitute = score.I("substituteCount");
                var line = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
                if (!skill.B("hidden"))
                {
                    var repeat = ActionButton("Повторить", delegate { OpenLiveSkill(id); });
                    repeat.ToolTip = "Повторить «" + skill.S("title") + "»";
                    DockPanel.SetDock(repeat, Dock.Right);
                    line.Children.Add(repeat);
                }
                var marks = Stack();
                marks.Children.Add(Label(skill.S("title"), 14, Ink, true));
                if (exact > 0)
                {
                    string accuracy = score.N("accuracy").ToString("0.#", CultureInfo.CurrentCulture);
                    string original = string.Format(CultureInfo.CurrentCulture, "Исходное: {0}/{1} · {2}%",
                        score.I("correct"), exact, accuracy);
                    if (score.I("median") > 0) original += " · " + score.I("median") + " мс";
                    marks.Children.Add(Label(original, 12, Dim));
                }
                if (substitute > 0)
                    marks.Children.Add(Label("Учебная замена: " + score.I("substituteCorrect") + "/" + substitute, 12, Dim));
                line.Children.Add(marks);
                live.Children.Add(line);
            }
            page.Children.Add(LibraryCard(live));

            var typing = Stack();
            typing.Children.Add(Label("Печать", 18, Ink, true));
            var history = library.Items("typingHistory").ToArray();
            if (history.Length == 0) typing.Children.Add(LibraryNote("Здесь сохраняются занятия печатью."));
            foreach (var item in history)
            {
                DateTime date;
                string rawDate = item.S("date");
                string formattedDate = DateTime.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date)
                    ? date.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture) : rawDate;
                var row = new DockPanel { Margin = new Thickness(0, 9, 0, 0) };
                var score = Label(string.Format(CultureInfo.CurrentCulture, "{0}  ·  {1} сим/мин  ·  {2:0.#}%  ·  ошибок {3}",
                    formattedDate, item.I("speed"), item.N("accuracy"), item.I("errors")), 12, Dim);
                DockPanel.SetDock(score, Dock.Right);
                score.Margin = new Thickness(12, 0, 0, 0);
                row.Children.Add(score);
                row.Children.Add(Label(item.S("lesson"), 13));
                typing.Children.Add(row);
            }
            page.Children.Add(LibraryCard(typing));
            return LibraryViewport(page);
        }

        private bool ConfirmRemoval(string prompt)
        {
            return MessageBox.Show(this, prompt, "Редактор", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        private void UpdateEditor(Data changed, string notice)
        {
            if (!changed.Exists) return;
            library = changed;
            editorNotice = notice;
            ShowPage("Редактор");
        }

        private static string NewLibraryId(IEnumerable<Data> current, IEnumerable<Data> previous)
        {
            var known = new HashSet<string>(previous.Select(item => item.S("id")), StringComparer.Ordinal);
            return current.Select(item => item.S("id")).FirstOrDefault(id => !known.Contains(id)) ?? "";
        }

        private StackPanel LibraryPage(string title, string subtitle)
        {
            var page = Stack();
            page.MaxWidth = 1000;
            page.Margin = new Thickness(24, 16, 24, 22);
            page.Children.Add(Label(title, 26, Ink, true));
            var note = Label(subtitle, 13, Dim);
            note.Margin = new Thickness(0, 3, 0, 16);
            page.Children.Add(note);
            return page;
        }

        private FrameworkElement LibraryViewport(FrameworkElement child)
        {
            return new ScrollViewer
            {
                Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false
            };
        }

        private Border LibraryCard(UIElement child)
        {
            return new Border
            {
                Child = child, Background = Card, BorderBrush = Edge, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(15), Margin = new Thickness(0, 0, 0, 10)
            };
        }

        private Border LibraryMetric(string caption, int count)
        {
            var body = Stack();
            body.Children.Add(Label(caption, 12, Dim));
            body.Children.Add(Label(count.ToString(CultureInfo.CurrentCulture), 23, Ink, true));
            var frame = LibraryCard(body);
            frame.Margin = new Thickness(0, 0, 8, 0);
            return frame;
        }

        private static Grid LibraryPair(FrameworkElement left, FrameworkElement right)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(right, 2);
            row.Children.Add(left);
            row.Children.Add(right);
            return row;
        }

        private FrameworkElement LibraryField(string caption, FrameworkElement control)
        {
            var group = Stack();
            CollectionField(group, caption);
            group.Children.Add(control);
            return group;
        }

        private void CollectionField(Panel group, string caption)
        {
            var label = Label(caption, 12, Dim, true);
            label.Margin = new Thickness(0, 8, 0, 4);
            group.Children.Add(label);
        }

        private TextBlock LibraryNote(string message)
        {
            var label = Label(message, 12, Dim);
            label.TextWrapping = TextWrapping.Wrap;
            label.Margin = new Thickness(0, 5, 0, 3);
            return label;
        }

        private TextBox LibraryInput(string name, int maximum)
        {
            var field = new TextBox
            {
                MaxLength = maximum, Foreground = Ink, CaretBrush = Ink, Background = Background,
                BorderBrush = Edge, BorderThickness = new Thickness(1), Padding = new Thickness(9, 5, 9, 5),
                FontSize = 14, MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(field, name);
            return field;
        }

        private ComboBox LibraryCombo(string name, IEnumerable<string> values)
        {
            var combo = new ComboBox
            {
                ItemsSource = values.ToArray(), MinHeight = 32, Padding = new Thickness(9, 0, 9, 0),
                Foreground = Ink, FontSize = 14, Background = Background, BorderBrush = Edge,
                MaxDropDownHeight = 320, VerticalContentAlignment = VerticalAlignment.Center, IsTextSearchEnabled = true
            };
            var items = new Style(typeof(ComboBoxItem));
            items.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
            items.Setters.Add(new Setter(Control.BackgroundProperty, Card));
            items.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
            combo.ItemContainerStyle = items;
            combo.Template = LibraryComboTemplate;
            AutomationProperties.SetName(combo, name);
            return combo;
        }

        private static ControlTemplate editorComboTemplate;
        private static ControlTemplate LibraryComboTemplate
        {
            get
            {
                if (editorComboTemplate == null)
                    editorComboTemplate = (ControlTemplate)XamlReader.Parse(@"<ControlTemplate
                        xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                        TargetType='ComboBox'>
                        <Grid>
                          <ToggleButton x:Name='DropToggle' Focusable='False' ClickMode='Press'
                              Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                              IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
                            <ToggleButton.Template>
                              <ControlTemplate TargetType='ToggleButton'>
                                <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                                        BorderThickness='1' CornerRadius='5'>
                                  <Path Data='M 0 0 L 4 4 L 8 0' Stroke='#C6B9AF' StrokeThickness='1.5'
                                        HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,10,0'/>
                                </Border>
                              </ControlTemplate>
                            </ToggleButton.Template>
                          </ToggleButton>
                          <ContentPresenter IsHitTestVisible='False' Margin='9,1,25,1' VerticalAlignment='Center'
                              Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'/>
                          <Popup x:Name='PART_Popup' Placement='Bottom' AllowsTransparency='True' Focusable='False'
                                 IsOpen='{TemplateBinding IsDropDownOpen}'>
                            <Border Background='#272524' BorderBrush='#5B504A' BorderThickness='1' Padding='4'
                                    MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'
                                    MaxHeight='{TemplateBinding MaxDropDownHeight}'>
                              <ScrollViewer VerticalScrollBarVisibility='Auto'>
                                <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/>
                              </ScrollViewer>
                            </Border>
                          </Popup>
                        </Grid>
                        <ControlTemplate.Triggers>
                          <Trigger Property='IsKeyboardFocusWithin' Value='True'>
                            <Setter TargetName='DropToggle' Property='BorderBrush' Value='#F7B575'/>
                          </Trigger>
                          <Trigger Property='IsEnabled' Value='False'>
                            <Setter TargetName='DropToggle' Property='Opacity' Value='0.5'/>
                          </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>");
                return editorComboTemplate;
            }
        }

        private static string EditorPhysicalLabel(string key)
        {
            switch (key)
            {
                case "LCtrl": return "Левый Ctrl";
                case "RCtrl": return "Правый Ctrl";
                case "LShift": return "Левый Shift";
                case "RShift": return "Правый Shift";
                case "LAlt": return "Левый Alt";
                case "RAlt": return "Правый Alt";
                case "LSuper": return "Левый Win";
                case "RSuper": return "Правый Win";
                case "Print": return "PrtSc";
                case "Space": return "Пробел";
                default: return key;
            }
        }

    }
}
