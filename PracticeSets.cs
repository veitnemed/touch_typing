using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TouchTypingStudio
{
    internal sealed class PracticeSetsPage : ScrollViewer
    {
        public readonly UIElement InitialFocus;
        private readonly StudioWindow owner;
        private readonly ProfileStore store;
        private readonly ComboBox kinds, saved;
        private readonly TextBox name, content;
        private readonly TextBlock instructions, notice;

        public PracticeSetsPage(StudioWindow owner, ProfileStore store, PracticeSetKind initial)
        {
            this.owner = owner;
            this.store = store;
            Background = FlightUi.Backdrop(store.Read());
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var stack = FlightUi.Sheet();
            Ui.Add(stack, Ui.Text("Свои наборы", 30, Ui.Ink, true), 15);
            var selectors = new WrapPanel();
            kinds = new ComboBox { Width = 180, Margin = new Thickness(0, 0, 8, 8),
                ItemsSource = new[] { "Спецсимволы", "Горячие клавиши", "Свой текст" } };
            Ui.Name(kinds, "Тип набора");
            saved = new ComboBox { MinWidth = 220, Margin = new Thickness(0, 0, 8, 8) };
            Ui.Name(saved, "Сохранённые наборы");
            Ui.Add(selectors, kinds);
            Ui.Add(selectors, saved);
            Ui.Add(selectors, Ui.Button("Новый набор", delegate { saved.SelectedIndex = -1; LoadSet(null); name.Focus(); }));
            Ui.Add(stack, selectors, 8);

            Ui.Add(stack, Ui.Text("Название", 13, Ui.Muted), 4);
            name = Ui.Editor("", 15);
            name.AcceptsReturn = false;
            name.MaxLength = 70;
            Ui.Name(name, "Название набора");
            Ui.Add(stack, name, 12);
            instructions = Ui.Text("", 14, Ui.Muted);
            Ui.Add(stack, instructions, 6);
            content = Ui.Editor("", 16);
            content.FontFamily = Ui.Mono.FontFamily;
            content.MaxLength = 12000;
            content.MinHeight = 155;
            content.Height = 210;
            content.TextWrapping = TextWrapping.Wrap;
            Ui.Name(content, "Содержимое набора");
            Ui.Add(stack, content, 10);
            var actions = new WrapPanel();
            Ui.Add(actions, Ui.Button("Начать тренировку", delegate { Save(true); }, true));
            Ui.Add(actions, Ui.Button("Сохранить", delegate { Save(false); }));
            Ui.Add(actions, Ui.Button("Назад", owner.ShowHome));
            Ui.Add(stack, actions, 6);
            notice = Ui.Text("", 14, Ui.Accent);
            Ui.Add(stack, notice);
            Content = stack;

            kinds.SelectionChanged += delegate { Populate(null); };
            saved.SelectionChanged += delegate { LoadSet(saved.SelectedItem as PracticeSet); };
            kinds.SelectedIndex = (int)initial;
            InitialFocus = kinds;
        }

        private PracticeSetKind Kind { get { return (PracticeSetKind)Math.Max(0, kinds.SelectedIndex); } }

        private void Populate(string select)
        {
            var items = store.Read().PracticeSets.Where(set => set.Kind == Kind).OrderBy(set => set.Name).ToArray();
            saved.ItemsSource = items;
            if (select != null) saved.SelectedItem = items.FirstOrDefault(set => set.Id == select);
            if (saved.SelectedIndex < 0) LoadSet(null);
            instructions.Text = Kind == PracticeSetKind.Shortcuts ? "Каждое сочетание на новой строке. Например, Ctrl+Shift+T | Вернуть вкладку" :
                Kind == PracticeSetKind.Symbols ? "Введите знаки или группы знаков, по одной на строку. Например, () [] {} или +=." :
                "Вставьте свой текст или код. Пустая строка разделяет фрагменты.";
        }

        private void LoadSet(PracticeSet selected)
        {
            name.Text = selected == null ? "" : selected.Name;
            content.Text = selected == null ? "" : selected.Content;
            notice.Text = "";
        }

        private void Save(bool start)
        {
            string title = name.Text.Trim();
            if (title.Length == 0) { notice.Text = "Введите название набора."; name.Focus(); return; }
            var existing = saved.SelectedItem as PracticeSet;
            var set = new PracticeSet
            {
                Id = existing == null ? Guid.NewGuid().ToString("N") : existing.Id,
                Kind = Kind, Name = title, Content = CodeText.Normalize(content.Text)
            };
            string validation = PracticeSetInput.Validate(set);
            if (validation != null) { notice.Text = validation; content.Focus(); return; }
            store.Update(p =>
            {
                p.PracticeSets.RemoveAll(item => item.Id == set.Id);
                p.PracticeSets.Add(set);
            });
            owner.WarnStorage();
            if (store.Error != null) return;
            if (start) owner.OpenPracticeSet(set);
            else
            {
                Populate(set.Id);
                notice.Text = "Набор сохранён.";
            }
        }
    }
}
