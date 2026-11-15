using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TouchTypingStudio
{
    internal sealed class TypingPage : ScrollViewer
    {
        private readonly StudioWindow owner;
        private readonly ProfileStore store;
        private StudioProfile profile;
        private Lesson lesson;
        private TypingAttempt attempt;
        private int variant;
        private string name, source, skill;
        private bool reviewMode;
        private Exercise exercise, reviewExercise;
        private bool campaign, reviewCampaign, failed;
        private Key last = Key.None, wrong = Key.None, down = Key.None;
        private bool heldRepeat;
        private DateTime flashUntil, lastSave;
        private Layout lastLayout = Layout.Other;
        private readonly TextBlock headline, hint, layoutLabel, time, summary, status, editLine, modeNote, ghostNote;
        private readonly PracticeRibbon ribbon;
        private readonly Border tape;
        private readonly ProgressBar progress, ghostProgress;
        private readonly KeyboardPicture keyboard = new KeyboardPicture();
        private readonly Button start, repeat, next, review, transfer, chooseDrill;
        private readonly WrapPanel actions;
        private readonly DispatcherTimer tick;

        public TypingPage(StudioWindow owner, ProfileStore store, bool preferPython)
        {
            this.owner = owner;
            this.store = store;
            profile = store.Read();
            if (profile.TypingCourse != Lesson.CurrentCourse || profile.ProgressionVersion < 1)
            {
                if (store.Error == null) profile = store.Update(p => TrainingProgress.Migrate(p, DateTime.Now));
                else TrainingProgress.Migrate(profile, DateTime.Now);
            }
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Focusable = false;

            var stack = new StackPanel { Margin = new Thickness(32, 17, 32, 18), MaxWidth = 1450 };
            time = Ui.Text("00:00", 19, Ui.Ink, true);
            time.FontFamily = Ui.Mono.FontFamily;
            time.TextAlignment = TextAlignment.Center;
            time.TextWrapping = TextWrapping.NoWrap;
            var clock = Ui.Panel(time, new Thickness(12, 5, 12, 5), Ui.Brush("#262A33"));
            clock.HorizontalAlignment = HorizontalAlignment.Center;
            Ui.Add(stack, clock, 21);

            var controls = new DockPanel { Margin = new Thickness(0, 0, 0, 5), LastChildFill = true };
            var right = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(right, Dock.Right);
            start = Ui.QuietButton("Начать · Пробел", Start);
            Ui.Add(right, start);
            var options = Ui.QuietButton("⋯", ShowOptions);
            options.ToolTip = "Подсказки, полный фрагмент и закрепление · F10";
            Ui.Name(options, "Параметры упражнения");
            Ui.Add(right, options);
            controls.Children.Add(right);
            headline = Ui.Text("", 16, Ui.Ink, true);
            headline.Margin = new Thickness(7, 4, 12, 0);
            controls.Children.Add(headline);
            Ui.Add(stack, controls);

            ribbon = new PracticeRibbon { Height = 72 };
            tape = Ui.Panel(ribbon, new Thickness(0), Ui.Brush("#181A1E"));
            tape.Focusable = true;
            tape.FocusVisualStyle = null;
            tape.ClipToBounds = true;
            tape.BorderThickness = new Thickness(6);
            tape.BorderBrush = Ui.Brush("#343A45");
            // The clip keeps the shifting ribbon inside the rounded opening.
            ribbon.SizeChanged += delegate
            {
                ribbon.Clip = new RectangleGeometry(new Rect(0, 0, ribbon.ActualWidth, ribbon.ActualHeight), 6, 6);
            };
            tape.GotKeyboardFocus += delegate { tape.BorderBrush = Ui.Brush("#444B58"); };
            tape.LostKeyboardFocus += delegate
            {
                tape.BorderBrush = Ui.Brush("#343A45");
                PauseForFocusLoss();
            };
            tape.MouseLeftButtonDown += delegate { tape.Focus(); };
            tape.PreviewTextInput += ReceiveComposition;
            tape.PreviewKeyDown += PracticeKey;
            tape.PreviewKeyUp += delegate(object s, KeyEventArgs e)
            {
                if (down != e.Key) return;
                down = Key.None;
                heldRepeat = false;
            };
            Ui.Add(stack, tape, 11);

            progress = new ProgressBar
            {
                Height = 5, Maximum = 1, Foreground = Ui.Brush("#9BB7BC"), Background = Ui.Brush("#171B23"),
                BorderThickness = new Thickness(0), Focusable = false, Margin = new Thickness(28, 0, 28, 0)
            };
            Ui.Name(progress, "Набрано символов");
            Ui.Add(stack, progress, 6);
            ghostProgress = new ProgressBar { Height = 4, Maximum = 1, Foreground = Ui.Brush("#B99BF6"),
                Background = Ui.Brush("#171B23"), BorderThickness = new Thickness(0), Focusable = false, Margin = new Thickness(28, 0, 28, 0) };
            Ui.Name(ghostProgress, "Шкала предыдущего результата");
            Ui.Add(stack, ghostProgress, 5);
            ghostNote = Ui.Text("", 12, Ui.Brush("#D5C2F5"));
            ghostNote.TextAlignment = TextAlignment.Center;
            Ui.Add(stack, ghostNote, 4);

            var readout = new DockPanel { LastChildFill = true, Margin = new Thickness(7, 0, 7, 0) };
            layoutLabel = Ui.Text("", 13, Ui.Muted, true);
            layoutLabel.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(layoutLabel, Dock.Right);
            readout.Children.Add(layoutLabel);
            summary = Ui.Text("", 13, Ui.Muted);
            readout.Children.Add(summary);
            Ui.Add(stack, readout, 14);
            modeNote = Ui.Text("", 14, Ui.Accent);
            modeNote.TextAlignment = TextAlignment.Center;
            Ui.Add(stack, modeNote, 6);

            keyboard.Margin = new Thickness(0, 0, 0, 4);
            Ui.Add(stack, keyboard, 5);
            hint = Ui.Text("", 13, Ui.Muted);
            hint.TextAlignment = TextAlignment.Center;
            Ui.Add(stack, hint, 6);
            editLine = Ui.Text("", 14, Ui.Ink);
            editLine.FontFamily = Ui.Mono.FontFamily;
            editLine.TextAlignment = TextAlignment.Center;
            Ui.Name(editLine, "Текст после исправлений");
            Ui.Add(stack, editLine, 6);

            status = Ui.Text("", 14, Ui.Accent);
            status.TextAlignment = TextAlignment.Center;
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            Ui.Add(stack, status, 5);
            actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            repeat = Ui.Button("Повторить раунд", Repeat);
            next = Ui.Button("Дальше · Enter", Next, true);
            review = Ui.Button("Отработать ошибки", Review);
            transfer = Ui.Button("Исправить код в VS Code", delegate { owner.StartWindowsRoute("CodeLine"); });
            chooseDrill = Ui.Button("Выбрать уровень · F11", delegate { owner.ShowPythonDrills(exercise.DrillLevel); });
            Ui.Add(actions, next);
            Ui.Add(actions, repeat);
            Ui.Add(actions, review);
            Ui.Add(actions, transfer);
            Ui.Add(actions, chooseDrill);
            Ui.Add(stack, actions);
            Content = stack;
            PreviewKeyDown += PageKeys;
            tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(125) };
            tick.Tick += delegate { OnTick(); };
            SizeChanged += delegate { if (attempt != null) RenderKeyboard(); };
            Loaded += delegate { tick.Start(); FocusPractice(); };
            Unloaded += delegate { tick.Stop(); };
            Restore(preferPython);
        }

        public void OpenPython()
        {
            if (lesson != null && Atlas.ForLesson(lesson).Id == "python" && !reviewMode) { FocusPractice(); return; }
            PauseForFocusLoss();
            profile = store.Read();
            Restore(true);
            FocusPractice();
        }

        private void Restore(bool preferPython)
        {
            var chosen = Lessons.Find(preferPython ? profile.PythonLesson : profile.SelectedLesson);
            if (preferPython && (Atlas.ForLesson(chosen) == null || Atlas.ForLesson(chosen).Id != "python"))
            {
                var previous = Lessons.Find(profile.SelectedLesson);
                chosen = Atlas.ForLesson(previous) != null && Atlas.ForLesson(previous).Id == "python" ? previous : Lessons.Find("assign");
            }
            if (!preferPython && profile.SelectedLesson == "custom" && !string.IsNullOrEmpty(profile.CustomText))
                Set(null, 0, profile.CustomText, profile.CustomName, profile.FindDraft("custom"));
            else
            {
                chosen = chosen ?? Lessons.Python[0];
                LoadLesson(chosen, preferPython);
            }
        }

        public void Choose(Lesson chosen, bool fromCampaign = false)
        {
            if (chosen == null) return;
            PauseForFocusLoss();
            profile = store.Read();
            LoadLesson(chosen, fromCampaign);
            SaveDraft(true);
            FocusPractice();
        }

        private void LoadLesson(Lesson chosen, bool fromCampaign = false)
        {
            int index = fromCampaign ? TrainingProgress.Variant(chosen, profile) : chosen.NextRound(profile);
            var saved = profile.FindDraft(chosen.Id);
            if (saved != null && saved.Variant < chosen.Rounds.Length && saved.Variant >= 0 && chosen.Rounds[saved.Variant] == saved.Target)
                index = saved.Variant;
            Set(chosen, index, chosen.Rounds[index], null, saved, fromCampaign: fromCampaign);
        }

        public void ChooseExercise(Exercise selected)
        {
            if (selected == null) return;
            PauseForFocusLoss();
            profile = store.Read();
            Set(null, 0, selected.Text, selected.Title, null, activity: selected);
            FocusPractice();
        }

        private void Set(Lesson selected, int round, string text, string title, TypingDraft draft, bool isReview = false,
            bool fromCampaign = false, Exercise activity = null)
        {
            lesson = selected;
            variant = round;
            name = selected == null ? (title ?? "Свой код") : selected.Title;
            source = selected == null ? (isReview ? "Строки предыдущего упражнения с ошибками." : "Вставленный текст или выбранный фрагмент файла.") : selected.Source;
            skill = selected == null ? (isReview ? "После повтора вернитесь к полному упражнению." : "Наберите показанный фрагмент посимвольно.") : selected.Skill;
            reviewMode = isReview;
            campaign = fromCampaign;
            exercise = activity;
            failed = false;
            attempt = new TypingAttempt(text, draft);
            last = wrong = down = Key.None;
            status.Text = "";
            Render(true);
        }

        public void FocusPractice()
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (IsVisible && owner.IsActive)
                {
                    ScrollToTop();
                    tape.Focus();
                }
            }), DispatcherPriority.Input);
        }

        private bool Done { get { return failed || attempt.Finished; } }

        private void Start()
        {
            if (attempt.Running) { PauseForFocusLoss(); FocusPractice(); return; }
            profile = store.Read();
            if (Done) Repeat();
            attempt.Start();
            if (!IsIndependent) attempt.ShowGuidance();
            lastSave = DateTime.UtcNow;
            status.Text = "";
            ScrollToTop();
            tape.Focus();
            Render(false);
        }

        public void PauseForFocusLoss()
        {
            if (attempt == null || !attempt.Running) return;
            attempt.Pause();
            down = Key.None;
            status.Text = "";
            SaveDraft(false);
            Render(false);
        }

        public void Close()
        {
            tick.Stop();
            PauseForFocusLoss();
        }

        private void Repeat()
        {
            if (exercise != null && exercise.Kind == ExerciseKind.Ghost)
                exercise.Ghost = TrainingProgress.Best(profile, exercise.Task, true, true);
            Set(lesson, variant, attempt.Target, name, null, reviewMode, campaign, exercise);
            SaveDraft(true);
            FocusPractice();
        }

        private Lesson reviewOrigin;
        private int reviewVariant;
        private string reviewOriginalText, reviewOriginalName;
        private TypingDraft reviewDraft;

        private void Review()
        {
            string text = attempt.ReviewText();
            if (text.Length == 0) return;
            if (!reviewMode)
            {
                reviewOrigin = lesson;
                reviewVariant = variant;
                reviewOriginalText = attempt.Target;
                reviewOriginalName = name;
                reviewDraft = null;
                reviewExercise = exercise;
                reviewCampaign = campaign;
            }
            Set(null, 0, text, "Трудные строки", null, true);
            FocusPractice();
        }

        private void SpacedReview()
        {
            PauseForFocusLoss();
            profile = store.Read();
            var region = Atlas.ForLesson(lesson) ?? Atlas.Find(exercise == null ? (PreferredLayout() == Layout.Russian ? "ru" : "qwerty") : exercise.Region);
            var plan = ExerciseBank.Create(ExerciseKind.Weak, profile, region, DateTime.Now);
            if (plan == null)
            {
                status.Text = "По этому уровню пока нет трудных символов для отдельного повтора.";
                Render(false);
                return;
            }
            Set(null, 0, plan.Text, plan.Title, null, activity: plan);
            FocusPractice();
        }

        private void Next()
        {
            if (reviewMode)
            {
                Set(reviewOrigin, reviewVariant, reviewOriginalText, reviewOriginalName, reviewDraft, false, reviewCampaign, reviewExercise);
                reviewDraft = null;
                FocusPractice();
                return;
            }
            if (!Done) return;
            if (exercise != null)
            {
                if (exercise.DrillLevel != null) owner.OpenPythonDrill(exercise.DrillLevel);
                else if (exercise.Context != null) owner.ShowMode(3);
                else if (exercise.Kind == ExerciseKind.Daily) owner.OpenExercise(ExerciseKind.Daily, exercise.Region);
                else if (exercise.Kind == ExerciseKind.Boss) owner.ShowAtlas(exercise.Region);
                else owner.ShowModes(exercise.Region);
                return;
            }
            if (lesson == null) return;
            profile = store.Read();
            if (!TrainingProgress.Pass(profile, attempt.Accuracy))
            {
                status.Text = "Для зачёта нужна точность от " + profile.Training.Goal + "%. Повторите через Ctrl+R.";
                UpdateStatus();
                return;
            }
            var chosen = lesson;
            if (campaign && TrainingProgress.Cleared(chosen, profile))
            {
                var territory = Atlas.ForLesson(chosen);
                var nextNode = Atlas.NextAfter(chosen, profile);
                if (nextNode == null || nextNode.Lesson == null) { owner.ShowAtlas(territory.Id); return; }
                chosen = nextNode.Lesson;
            }
            else if (!campaign && chosen.GoodRounds(profile) >= chosen.Rounds.Length)
                chosen = Lessons.Next(chosen);
            if (chosen == null)
            {
                status.Text = "Все варианты готовы. Выберите упражнение из каталога для повтора.";
                return;
            }
            LoadLesson(chosen, campaign);
            SaveDraft(true);
            FocusPractice();
        }

        private void SaveDraft(bool selection)
        {
            if (reviewMode || exercise != null) return;
            var draft = !attempt.Finished && (attempt.Position > 0 || attempt.Errors > 0) ? attempt.Draft(lesson == null ? "custom" : lesson.Id, variant) : null;
            string id = lesson == null ? "custom" : lesson.Id;
            profile = store.Update(p =>
            {
                p.SaveDraft(id, draft);
                if (selection) p.SelectedLesson = id;
                if (lesson != null && Atlas.ForLesson(lesson).Id == "python") p.PythonLesson = id;
            });
            owner.WarnStorage();
        }

        private void OnTick()
        {
            if (DateTime.UtcNow >= flashUntil && (last != Key.None || wrong != Key.None))
            {
                last = wrong = Key.None;
                RenderRibbon();
                RenderKeyboard();
            }
            Layout now = Native.ActiveLayout();
            if (now != lastLayout)
            {
                RenderKeyboard();
            }
            if (attempt.Running)
            {
                UpdateStats();
                if (DateTime.UtcNow - lastSave > TimeSpan.FromSeconds(8) && (attempt.Position > 0 || attempt.Errors > 0))
                {
                    lastSave = DateTime.UtcNow;
                    SaveDraft(false);
                }
            }
        }

        private void PageKeys(object sender, KeyEventArgs e)
        {
            if (e.Handled) return;
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.R)
            {
                // Ctrl+R is available in pauses. It cannot erase an active round.
                if (!attempt.Running) Repeat();
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Right)
            {
                Next();
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers != ModifierKeys.None) return;
            if (e.Key == Key.Enter && Done && next.IsEnabled && tape.IsKeyboardFocused)
            {
                Next();
                e.Handled = true;
                return;
            }
            e.Handled = HandleShortcut(e.Key);
        }

        public bool HandleShortcut(Key key)
        {
            switch (key)
            {
                case Key.F2: owner.ShowAtlas(Atlas.ForLesson(lesson) != null ? Atlas.ForLesson(lesson).Id : exercise == null ? null : exercise.Region); break;
                case Key.F3: ShowImport(); break;
                case Key.F4: ToggleKeys(); break;
                case Key.F5: ToggleHands(); break;
                case Key.F6: FocusPractice(); break;
                case Key.F7: owner.ShowAnalytics(); break;
                case Key.F9: SpacedReview(); break;
                case Key.F10: ShowOptions(); break;
                default: return false;
            }
            return true;
        }

        private void PracticeKey(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                PauseForFocusLoss();
                if (Done && next.IsEnabled) next.Focus();
                else start.Focus();
                e.Handled = true;
                return;
            }
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return;
            if (!attempt.Running)
            {
                if (key == Key.Space && !e.IsRepeat)
                {
                    Start();
                    e.Handled = true;
                }
                return;
            }
            down = key;
            heldRepeat = e.IsRepeat;
            if (key == Key.LeftShift || key == Key.RightShift || key == Key.CapsLock)
            {
                last = key;
                flashUntil = DateTime.UtcNow.AddMilliseconds(200);
                RenderKeyboard();
                return;
            }
            char c;
            switch (key)
            {
                case Key.Space: c = ' '; break;
                case Key.Enter: c = '\n'; break;
                case Key.Tab:
                    if (Keyboard.Modifiers == ModifierKeys.Shift)
                    {
                        PauseForFocusLoss();
                        return;
                    }
                    c = '\t'; break;
                case Key.Back: c = '\b'; break;
                default: return;
            }
            e.Handled = true;
            if (!e.IsRepeat) Receive(c, key);
        }

        private void ReceiveComposition(object sender, TextCompositionEventArgs e)
        {
            e.Handled = true;
            if (!attempt.Running || heldRepeat || string.IsNullOrEmpty(e.Text)) return;
            if (e.Text.Length == 1) Receive(e.Text[0], down);
        }

        private void Receive(char input, Key physical)
        {
            char expected = attempt.Expected;
            var desired = TypingKeyboard.Hint(expected, PreferredLayout());
            bool accepted = attempt.Enter(input, TrainingProgress.Locale(desired == null ? PreferredLayout() : desired.Layout));
            last = accepted ? physical : Key.None;
            wrong = accepted ? Key.None : (physical == Key.None ? (desired == null ? Key.None : desired.Cap.Key) : physical);
            flashUntil = DateTime.UtcNow.AddMilliseconds(380);
            status.Text = accepted ? "" :
                "Ожидалось «" + CodeText.Display(expected) + "», получено «" + CodeText.Display(input) + "»." +
                (desired != null && desired.Layout != Native.ActiveLayout() ? " Проверьте раскладку." : "");
            failed = exercise != null && exercise.Stops(attempt);
            if (Done) { attempt.Pause(); Complete(); Render(true); }
            else
            {
                progress.Value = attempt.Position;
                UpdateStatus();
                RenderRibbon();
                UpdateStats();
                RenderKeyboard();
            }
        }

        private void Complete()
        {
            bool qualified = !failed && TrainingProgress.Pass(profile, attempt.Accuracy);
            var region = Atlas.ForLesson(lesson);
            string locale = CodeText.LanguageOfText(attempt.Target);
            string activity = exercise == null ? (lesson == null ? (reviewMode ? "Correction" : "Free") : "Campaign") :
                exercise.Task.StartsWith("Transfer/", StringComparison.Ordinal) ? "Transfer" : exercise.Kind.ToString();
            var item = new TypingResult
            {
                Lesson = name, Language = locale, Count = attempt.Position,
                Errors = attempt.Errors, Seconds = attempt.Seconds, Date = DateTime.Now,
                Weak = attempt.WeakKeys(), Activity = activity, Completed = !failed, Qualified = qualified, FirstMs = attempt.FirstMs,
                Context = exercise == null ? null : exercise.Context,
                Task = exercise == null ? ExerciseBank.Task(lesson == null ? activity : lesson.Id, attempt.Target) : exercise.Task,
                Region = exercise != null ? exercise.Region : region != null ? region.Id : locale == "RU" ? "ru" : "qwerty",
                Independent = IsIndependent && !attempt.HintsUsed
            };
            bool improved = false;
            int newXp = 0;
            profile = store.Update(p =>
            {
                int oldXp = TrainingProgress.Xp(p);
                if (!reviewMode && exercise == null) p.SaveDraft(lesson == null ? "custom" : lesson.Id, null);
                if (region != null && region.Id == "python") p.PythonLesson = lesson.Id;
                p.Results.Add(item);
                if (p.Results.Count > 500) p.Results.RemoveRange(0, p.Results.Count - 500);
                TrainingProgress.SaveMotor(p, attempt, item.Independent, item.Date, item.Task);
                if (lesson != null) TrainingProgress.OnLesson(p, lesson, variant, qualified, item.Date);
                if (exercise != null && exercise.Recall != null) TrainingProgress.OnRecall(p, exercise.Recall, qualified, item.Date);
                if (exercise != null && qualified)
                {
                    if (exercise.Branch != null) TrainingProgress.Earn(p, "branch/" + exercise.Branch, 30, item.Date);
                    if (exercise.Kind == ExerciseKind.Boss && !p.Exams.Contains(exercise.Region))
                    {
                        p.Exams.Add(exercise.Region);
                        TrainingProgress.Earn(p, "exam/" + exercise.Region, 110, item.Date);
                    }
                }
                if (qualified)
                {
                    improved = TrainingProgress.SaveRecord(p, item, attempt, exercise != null && exercise.Kind == ExerciseKind.Ghost);
                    if (improved) TrainingProgress.Earn(p, "record/" + item.Region, 25, item.Date);
                }
                newXp = TrainingProgress.Xp(p) - oldXp;
            });
            owner.WarnStorage();
            string weak = attempt.WeakKeys(), pairs = attempt.WeakPairs();
            string advance = !qualified && (lesson != null || exercise != null && (exercise.Kind == ExerciseKind.Boss || exercise.Recall != null))
                ? "Для зачёта нужно " + profile.Training.Goal + "%. " :
                lesson != null ? (TrainingProgress.Cleared(lesson, profile) ? "Уровень зачтён. " : "Вариант зачтён. ") :
                qualified && exercise != null && exercise.Kind == ExerciseKind.Boss ? "Экзамен сдан. " : "";
            var scheduled = exercise == null || exercise.Recall == null ? null : profile.RecallDates.Find(x => x.Lesson == exercise.Recall.Id);
            status.Text = (failed ? "Запас ошибок исчерпан. " : "Готово. ") + advance
                + (weak.Length == 0 ? "Ошибок нет." : "Трудные символы: " + weak)
                + (pairs.Length == 0 ? "" : "   Сочетания: " + pairs)
                + (scheduled == null ? "" : " Следующий повтор: " + scheduled.Due.ToString("dd.MM") + ".")
                + (improved ? " Новый личный рекорд." : "") + (newXp > 0 ? "  +" + newXp + " XP" : "");
        }

        private void Render(bool textChanged)
        {
            if (attempt == null) return;
            if (lesson == null) headline.Text = name;
            else
            {
                var region = Atlas.ForLesson(lesson);
                int node = Atlas.Number(lesson);
                bool opening = campaign && !TrainingProgress.Cleared(lesson, profile);
                string phase = opening ? lesson.Recall(variant) ? "   ·   Самостоятельная проверка" :
                    "   ·   Набор с подсказками" : "   ·   Вариант " + (variant + 1) + "/" + lesson.Rounds.Length;
                headline.Text = (region != null ? region.Name : Lessons.Track(lesson)) + "  ›  " +
                    (node > 0 ? node + ". " : "") + name + phase;
            }
            progress.Maximum = attempt.Target.Length;
            progress.Value = attempt.Position;
            start.Content = attempt.Running ? "Пауза · Esc" :
                Done ? "Повторить · Пробел" : attempt.Position > 0 ? "Продолжить · Пробел" : "Начать · Пробел";
            next.IsEnabled = reviewMode || (Done && (exercise != null || lesson != null && TrainingProgress.Pass(profile, attempt.Accuracy)));
            next.Content = reviewMode ? "К упражнению · Enter" : exercise == null ? "Дальше · Enter" :
                exercise.DrillLevel != null ? "Новый раунд · Enter" :
                exercise.Context != null ? "К плану · Enter" :
                exercise.Kind == ExerciseKind.Daily ? "Следующий повтор · Enter" :
                exercise.Kind == ExerciseKind.Boss ? "На карту · Enter" : "К режимам · Enter";
            next.Visibility = next.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            review.Visibility = Done && attempt.Errors > 0 && (exercise == null || exercise.Kind != ExerciseKind.Memory) ? Visibility.Visible : Visibility.Collapsed;
            transfer.Visibility = attempt.Finished && exercise != null && exercise.Kind == ExerciseKind.Python ? Visibility.Visible : Visibility.Collapsed;
            chooseDrill.Visibility = Done && exercise != null && exercise.DrillLevel != null ? Visibility.Visible : Visibility.Collapsed;
            actions.Visibility = Done || reviewMode && !attempt.Running ? Visibility.Visible : Visibility.Collapsed;
            modeNote.Text = exercise == null ? "" : exercise.Instruction;
            modeNote.Visibility = !string.IsNullOrEmpty(modeNote.Text) && !attempt.Running && !Done ? Visibility.Visible : Visibility.Collapsed;
            ghostProgress.Maximum = attempt.Target.Length;
            ghostProgress.Visibility = ghostNote.Visibility = exercise != null && exercise.Ghost != null ? Visibility.Visible : Visibility.Collapsed;
            UpdateStatus();
            if (textChanged) RenderRibbon();
            UpdateStats();
            RenderKeyboard();
            if (Done) UpdateCompletionFocus();
        }

        private void UpdateStatus()
        {
            status.Visibility = string.IsNullOrWhiteSpace(status.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateCompletionFocus()
        {
            if (!tape.IsKeyboardFocused) return;
            Ui.Name(tape, next.IsEnabled ? "Раунд завершён. Enter — дальше. Пробел — повторить." :
                "Раунд завершён. Пробел — повторить.");
        }

        private void UpdateStats()
        {
            time.Text = TimeSpan.FromSeconds(attempt.Seconds).ToString(@"mm\:ss");
            int speed = attempt.Seconds < 1 ? 0 : (int)Math.Round(60 * attempt.Position / attempt.Seconds);
            summary.Text = string.Format("{0}/{1}   ·   Ошибки {2}   ·   {3}   ·   {4} сим/мин",
                attempt.Position, attempt.Target.Length, attempt.Errors,
                attempt.Position + attempt.Errors == 0 ? "—" : attempt.Accuracy.ToString("0.0") + "%", speed);
            if (exercise != null && exercise.ErrorLimit > 0)
                summary.Text += "   ·   Запас: " + Math.Max(0, exercise.ErrorLimit - attempt.Errors);
            if (exercise != null && exercise.Ghost != null)
            {
                int ahead = attempt.Position - exercise.GhostPosition(attempt.Seconds);
                ghostProgress.Value = attempt.Position - ahead;
                ghostNote.Text = "Личная лучшая попытка · " + exercise.Ghost.Accuracy.ToString("0.0") + "%  ·  " +
                    (ahead == 0 ? "вровень" : ahead > 0 ? "вы впереди на " + ahead : "вы позади на " + -ahead);
            }
        }

        private void RenderRibbon()
        {
            if (attempt == null) return;
            bool memory = exercise != null && exercise.Kind == ExerciseKind.Memory;
            string repaired = attempt.HasRepairs ? attempt.Written : null;
            ribbon.Update(attempt.Target, attempt.Position, wrong != Key.None, memory, repaired);
            editLine.Visibility = attempt.HasRepairs && !IsIndependent ? Visibility.Visible : Visibility.Collapsed;
            if (editLine.Visibility == Visibility.Visible)
            {
                string written = repaired;
                int start = Math.Max(0, written.Length - 40);
                editLine.Text = written.Length == 0 ? "После набора здесь появится результат исправлений." :
                    (start > 0 ? "… " : "") + PracticeRibbon.VisibleText(written.Substring(start));
                Ui.Name(editLine, "После исправлений: " + written.Substring(start));
            }
            Ui.Name(tape, attempt.Finished ? "Фрагмент набран" : "Строка набора. Следующий символ: " + Spoken(attempt.Expected)
                + (memory ? "." : ". " + attempt.Target.Substring(attempt.Position, Math.Min(100, attempt.Target.Length - attempt.Position))));
        }

        private static string Spoken(char c)
        {
            if (c == ' ') return "пробел";
            if (c == '\n') return "Enter";
            if (c == CodeText.Erase) return "Backspace";
            return CodeText.Display(c);
        }

        private Layout PreferredLayout()
        {
            if (lesson != null && lesson.Language != Layout.Neutral) return lesson.Language;
            if (exercise != null && exercise.Kind != ExerciseKind.Mixed && exercise.Layout != Layout.Neutral) return exercise.Layout;
            return Native.ActiveLayout();
        }

        private void RenderKeyboard()
        {
            lastLayout = Native.ActiveLayout();
            KeyHint expected = TypingKeyboard.Hint(attempt.Expected, PreferredLayout());
            bool recall = IsIndependent;
            bool faded = !recall && lesson != null && variant > 0 && profile.Training.FadeFrom < 100 &&
                100 * attempt.Position >= attempt.Target.Length * Math.Max(20, profile.Training.FadeFrom);
            bool fixedHelp = exercise != null && exercise.FixedGuidance;
            bool showKeys = (fixedHelp || profile.ShowKeys) && !recall && !Done;
            bool showHands = (fixedHelp || profile.ShowHands) && !recall && !faded && !Done;
            bool tab = attempt.CanIndent();
            bool caps = Keyboard.IsKeyToggled(Key.CapsLock);
            string text = "";
            if (!Done && recall) text = "Самостоятельный набор";
            else if (!Done && expected != null && !faded)
            {
                text = Spoken(attempt.Expected) + "   ·   " + TypingKeyboard.Finger(expected.Cap.Finger);
                if (expected.Modifier != Key.None)
                    text += expected.Modifier == Key.LeftShift ? "   + левый Shift" : "   + правый Shift";
                if (tab) text += "   ·   Tab = 4 пробела";
            }
            if (!Done && !recall && attempt.Expected == CodeText.Erase)
                text = "Backspace · удалите «" + CodeText.Display(attempt.LastWritten) + "»";
            else if (!Done && lesson != null && !recall)
            {
                string phase = lesson.OpeningPhase(variant, attempt.Position);
                if (phase.Length > 0) text = phase + "   ·   " + text;
            }
            if (caps && !Done) text += "   ·   Caps Lock включён";
            hint.Text = text;
            hint.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            bool switchLayout = expected != null && expected.Layout != lastLayout;
            layoutLabel.Text = switchLayout ? CodeText.LanguageName(lastLayout) + " → " +
                CodeText.LanguageName(expected.Layout) + "  ·  Win+Space" : CodeText.LanguageName(lastLayout);
            layoutLabel.Foreground = switchLayout ? Ui.Brush("#FFD29B") : Ui.Muted;
            double available = ActualHeight <= 0 ? 610 : ActualHeight;
            keyboard.Height = showKeys ? Math.Max(145, Math.Min(242, available - (attempt.HasRepairs ? 348 : 316))) : 111;
            keyboard.Visibility = showKeys || showHands ? Visibility.Visible : Visibility.Collapsed;
            Layout drawn = lastLayout;
            if (expected != null && (!attempt.Running || lastLayout == Layout.Other)) drawn = expected.Layout;
            keyboard.Update(drawn, faded ? null : expected, last, wrong, !faded && tab, caps, showKeys, showHands);
        }

        private bool IsIndependent { get { return exercise != null && exercise.Independent || lesson != null && lesson.Recall(variant); } }

        private void ToggleKeys()
        {
            if (IsIndependent || exercise != null && exercise.FixedGuidance) return;
            bool value = !profile.ShowKeys;
            profile = store.Update(p => p.ShowKeys = value);
            owner.WarnStorage();
            Render(false);
        }

        private void ToggleHands()
        {
            if (IsIndependent || exercise != null && exercise.FixedGuidance) return;
            bool value = !profile.ShowHands;
            profile = store.Update(p => p.ShowHands = value);
            owner.WarnStorage();
            Render(false);
        }

        private void ShowOptions()
        {
            PauseForFocusLoss();
            profile = store.Read();
            var panel = new StackPanel { Margin = new Thickness(25), MaxWidth = 940 };
            Ui.Add(panel, Ui.Text(name, 25, Ui.Ink, true), 9);
            if (lesson != null)
                Ui.Add(panel, Ui.Text(lesson.Phase(variant, attempt.Position) + "  ·  Зачтено " + lesson.GoodRounds(profile) +
                    "/" + lesson.Rounds.Length + "\n" + lesson.FocusKeys + "  ·  " + skill + "\n" + source, 14, Ui.Muted), 12);
            if (exercise == null || exercise.Kind != ExerciseKind.Memory)
            {
                var field = Ui.Editor(CodeText.VisibleScript(attempt.Target), 16);
                field.IsReadOnly = true;
                field.FontFamily = Ui.Mono.FontFamily;
                field.Height = 230;
                Ui.Name(field, "Полный фрагмент упражнения");
                Ui.Add(panel, field, 12);
            }
            var bar = new WrapPanel();
            var close = Ui.Button("К набору · Esc", owner.ClosePanel, true);
            Ui.Add(bar, close);
            if (!IsIndependent && (exercise == null || !exercise.FixedGuidance))
            {
                var keyboardToggle = Ui.Button(profile.ShowKeys ? "Скрыть клавиатуру" : "Показать клавиатуру",
                    delegate { ToggleKeys(); ShowOptions(); });
                Ui.Add(bar, keyboardToggle);
                Ui.Add(bar, Ui.Button(profile.ShowHands ? "Скрыть руки" : "Показать руки",
                    delegate { ToggleHands(); ShowOptions(); }));
            }
            if (attempt.HasRepairs)
                Ui.Add(panel, Ui.Text("⌫ означает Backspace. Перед ним наберите указанный символ, затем удалите его.", 14, Ui.Muted), 6);
            Ui.Add(bar, Ui.Button("Трудные клавиши · F9", delegate { owner.ClosePanel(); SpacedReview(); }));
            Ui.Add(bar, Ui.Button("Повторить раунд · Ctrl+R", delegate { owner.ClosePanel(); Repeat(); }));
            Ui.Add(panel, bar, 6);
            var due = reviewMode ? null : Lessons.NextDue(profile, DateTime.Today);
            if (due != null)
                Ui.Add(panel, Ui.Button("Закрепить сегодня · " + due.Title, delegate { owner.OpenExercise(ExerciseKind.Daily, null); }));
            else if (profile.RecallDates.Count > 0)
                Ui.Add(panel, Ui.Text("Следующее закрепление — " + profile.RecallDates.Min(r => r.Due).ToString("dd.MM"), 14, Ui.Muted));
            owner.OpenPanel(Ui.Scroll(panel), close);
        }

        private void ShowImport()
        {
            PauseForFocusLoss();
            profile = store.Read();
            var panel = new DockPanel { Margin = new Thickness(18) };
            var head = new StackPanel();
            Ui.Add(head, Ui.Text("Вставьте код или откройте файл", 23, Ui.Ink, true), 5);
            Ui.Add(head, Ui.Text("Ctrl+O — открыть .py или .txt. Ctrl+L — ввести путь. Выделите строки и нажмите Ctrl+Enter. Без выделения берётся весь текст.", 14, Ui.Muted), 10);
            DockPanel.SetDock(head, Dock.Top);
            panel.Children.Add(head);
            var field = Ui.Editor(profile.CustomText);
            field.FontFamily = Ui.Mono.FontFamily;
            Ui.Name(field, "Текст упражнения");
            string file = profile.CustomName;
            var foot = new StackPanel();
            var message = Ui.Text("Табуляция преобразуется в отступы по четыре колонки.", 13, Ui.Muted);
            Ui.Add(foot, message, 6);
            var pathLabel = Ui.Text("Полный путь к .py или .txt", 13, Ui.Muted);
            var pathField = Ui.Editor("", 14);
            pathField.AcceptsReturn = false;
            pathField.Visibility = pathLabel.Visibility = Visibility.Collapsed;
            Ui.Name(pathField, "Путь к файлу упражнения");
            Ui.Add(foot, pathLabel, 4);
            Ui.Add(foot, pathField, 8);
            var buttons = new WrapPanel();
            Action<string> load = delegate(string path)
            {
                try
                {
                    field.Text = CodeText.ReadImport(path);
                    file = Path.GetFileName(path);
                    message.Text = file + " загружен. Можно выделить нужный фрагмент.";
                    pathField.Visibility = pathLabel.Visibility = Visibility.Collapsed;
                    field.Focus();
                    field.Select(0, 0);
                }
                catch (Exception error) { message.Text = error.Message; }
            };
            Action enterPath = delegate
            {
                pathField.Visibility = pathLabel.Visibility = Visibility.Visible;
                message.Text = "Введите полный путь и нажмите Enter.";
                pathField.Focus();
                pathField.SelectAll();
            };
            Action open = delegate
            {
                var picker = new OpenFileDialog
                {
                    Title = "Открыть текст упражнения", Filter = "Код и текст (*.py, *.txt)|*.py;*.txt",
                    CheckFileExists = true, Multiselect = false
                };
                if (picker.ShowDialog(owner) != true) { field.Focus(); return; }
                load(picker.FileName);
            };
            Action apply = delegate
            {
                string text = CodeText.Normalize(field.SelectionLength > 0 ? field.SelectedText : field.Text);
                if (string.IsNullOrWhiteSpace(text)) { message.Text = "Добавьте хотя бы один символ кроме пробелов и переносов."; field.Focus(); return; }
                if (text.Length > 4000) { message.Text = "Выделите фрагмент до 4000 символов."; field.Focus(); return; }
                var unavailable = text.Where(c => !TypingKeyboard.Supports(c)).Distinct().Take(5).ToArray();
                if (unavailable.Length > 0)
                {
                    message.Text = "Для этих символов нет клавиши в поддерживаемых EN/RU: " +
                        string.Join(", ", unavailable.Select(c => "U+" + ((int)c).ToString("X4")));
                    field.Focus();
                    return;
                }
                var saved = text == profile.CustomText ? profile.FindDraft("custom") : null;
                profile = store.Update(p =>
                {
                    p.CustomName = file ?? "Свой код";
                    p.CustomText = text;
                    p.SelectedLesson = "custom";
                    if (saved == null) p.SaveDraft("custom", null);
                });
                Set(null, 0, text, file, saved);
                owner.ClosePanel();
                owner.WarnStorage();
            };
            Ui.Add(buttons, Ui.Button("Открыть .py / .txt · Ctrl+O", open));
            Ui.Add(buttons, Ui.Button("Ввести путь · Ctrl+L", enterPath));
            Ui.Add(buttons, Ui.Button("К набору · Ctrl+Enter", apply, true));
            var cancel = Ui.Button("К набору · Esc", owner.ClosePanel);
            Ui.Add(buttons, cancel);
            Ui.Add(foot, buttons);
            DockPanel.SetDock(foot, Dock.Bottom);
            panel.Children.Add(foot);
            panel.Children.Add(field);
            panel.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && pathField.IsKeyboardFocused)
                {
                    e.Handled = true;
                    string path = pathField.Text.Trim().Trim('"');
                    if (path.Length == 0) message.Text = "Введите полный путь к файлу.";
                    else load(path);
                    return;
                }
                if (Keyboard.Modifiers != ModifierKeys.Control) return;
                if (e.Key == Key.O) { open(); e.Handled = true; }
                if (e.Key == Key.L) { enterPath(); e.Handled = true; }
                if (e.Key == Key.Enter) { apply(); e.Handled = true; }
            };
            owner.OpenPanel(panel, field);
        }

    }
}
