using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    internal bool BlockRepair, SafeReorder;
    internal const string SafeReorderRevision = "editor-reorder-markdown/v1";
    private const string SafeReorderFile = Project + "/release-check.md";
    private const string SafeReorderDocument = "3. Read the test report\n1. Save changes\n2. Run the tests\n";
    internal bool OnReorderChecklist => SafeReorder && Active?.App == "code" && Active.Focus == "body" &&
        Active.Url == SafeReorderFile;

    internal void PrepareSafeReorder()
    {
        SafeReorder = true;
        Terminal.StoreFile(SafeReorderFile, SafeReorderDocument);
        originalFiles[SafeReorderFile] = SafeReorderDocument;
    }

    internal LabTask DescribeChecklistTask(LabTask task) => OnReorderChecklist ? task.Id switch
    {
        "move-line-down" => task with { Title = "Опустите пункт 3 на одну строку в release-check.md",
            Hint = "Задача: восстановить порядок 1 → 2 → 3. Исходный Python-код менять не нужно." },
        "save" => task with { Title = "Сохраните упорядоченный список проверки релиза" },
        "changes" => task with { Title = "Сверьте diff release-check.md в Source Control" },
        _ => task
    } : task;
    internal const string BlockRepairRoot = Project + "/checkout";
    internal const string BlockRepairPath = BlockRepairRoot + "/checkout.py";
    internal const string BrokenCheckout =
        "def checkout_total(price, count):\n    return total\n    subtotal = price * count\n    fee = 0\n    total = subtotal\n";
    internal const string RepairedCheckout =
        "def checkout_total(price, count):\n    subtotal = price * count\n    fee = 2\n    total = subtotal + fee\n    return total\n";

    private const string CheckoutTestSource =
        "import unittest\n\nfrom checkout import checkout_total\n\nclass CheckoutTests(unittest.TestCase):\n" +
        "    def test_order(self):\n        self.assertEqual(checkout_total(4, 3), 14)\n" +
        "    def test_empty_order(self):\n        self.assertEqual(checkout_total(1, 0), 2)\n";
    internal const string CheckoutExpectedCall = "checkout_total(4, 3)";
    internal const string CheckoutRevision = "checkout-stderr/v1";
    internal const string CheckoutReport = "checkout-test.txt";
    internal const string CheckoutOutput = "Учебная проверка checkout\nПроверки: обычный и пустой заказ.\nRan 2 tests\nOK\n";
    private static string RunCheckoutTests(VirtualTerminal fixture)
    {
        if (fixture.Cwd != BlockRepairRoot ||
            fixture.ReadFile(BlockRepairRoot + "/test_checkout.py") != CheckoutTestSource)
            throw new ArgumentException("Откройте каталог checkout и не заменяйте исходные проверки.");
        if (fixture.ReadFile(BlockRepairPath) != RepairedCheckout)
            throw new ArgumentException("Проверка не прошла: сначала сохраните исправленный checkout.py.");
        return CheckoutOutput;
    }

    internal void PrepareBlockRepair()
    {
        BlockRepair = true;
        PythonEditingExercise = true;
        Terminal.CreateFolder(BlockRepairRoot);
        Seed("checkout.py", BrokenCheckout);
        Seed("README.md", "# Checkout\nТребование: при любом заказе комиссия составляет 2. Сумма возвращается после расчёта.\n");
        Seed("test_checkout.py", CheckoutTestSource);
        Terminal.InstallUnitTest("test_checkout", RunCheckoutTests);
        OpenDocument(BlockRepairPath);
        Active.Cursor = BrokenCheckout.IndexOf("return total", StringComparison.Ordinal);
        void Seed(string filename, string body)
        {
            string path = BlockRepairRoot + "/" + filename;
            Terminal.StoreFile(path, body);
            originalFiles[path] = body;
        }
    }

    internal object CheckoutEvidence(LabTask task, string equivalentCommand)
    {
        if (task.Action == "command")
            return new { command = equivalentCommand ?? Terminal.LastCommand, Terminal.LastSuccess, Terminal.Cwd,
                Terminal.LastOutput, report = Terminal.ReadFile(BlockRepairRoot + "/" + CheckoutReport) };
        var w = Active;
        if (task.Action == "checkout-expect")
        {
            var selected = w?.Tab?.Position;
            var start = selected == null ? 0 : Math.Min(selected.Caret, selected.Anchor);
            var end = selected == null ? 0 : Math.Max(selected.Caret, selected.Anchor);
            return new { w?.Focus, w?.Url, w?.Find, matched = w?.Body[start..end] };
        }
        if (task.Action is "quick-next" or "quick-choose")
            return new { w?.Focus, w?.Url, chosen = w == null ? null : Entries(w).ElementAtOrDefault(w.OptionIndex)?.Path };
        return new { Layout, w?.App, w?.Focus, w?.Url, w?.Body, w?.Anchor, w?.Cursor, w?.Panel,
            w?.Find, selected = w?.Tab?.Position.Anchor != w?.Tab?.Position.Caret,
            saved = w?.Tab?.SavedBody, disk = Terminal.ReadFile(BlockRepairPath) };
    }

    internal LabTask DescribeBlockTask(LabTask task)
    {
        if (!BlockRepair) return task;
        return task.Id switch
        {
            "move-line-down" => task with {
                Title = "Опустите return total на строку ниже в checkout.py",
                Hint = "Расчёт выполняется сверху вниз. return должен оказаться после subtotal, fee и total."
            },
            "changes" => task with {
                Title = "Откройте Source Control и найдите правку checkout.py",
                Hint = "Панель сравнивает сохранённые файлы с исходным снимком проекта. Она не отправляет код в сеть."
            },
            "focus-editor" => task with {
                Title = "Верните фокус к коду checkout.py",
                Hint = "Ctrl+1 меняет фокус, а не файл. Source Control оставит правку на виду."
            },
            "save" => task with { Title = "Сохраните правку в checkout.py", Hint = "Проверьте порядок строк и точку возврата." },
            _ => task
        };
    }
}

internal static partial class LiveCourse
{
    private static void BlockRepairMissions(List<LiveMission> missions) =>
        missions.Add(new("code", 1, "code-repair-checkout", "Исправить порядок и пересобрать блок расчёта",
        [
            new("Поставьте return после вычислений, не разрывая объявление функции",
                "move-line-down move-line-down move-line-down save changes focus-editor"),
            new("Выделите только два ошибочных присваивания, не трогая return",
                "python-edit-up python-edit-up python-edit-home python-edit-select-down python-edit-select-end"),
            new("Пересоберите блок с комиссией 2 и пересчётом суммы",
                "python-edit-write-fee python-edit-insert-below python-edit-write-total save changes focus-editor"),
            new("Откройте исходные проверки и найдите ожидаемый расчёт",
                "quick-open quick:test_checkout.py quick-choose find checkout-expect escape"),
            new("Запустите две проверки и запишите результат в текстовый файл",
                "terminal-toggle cmd:cd~" + LabScene.BlockRepairRoot + " cmd:python3~-m~unittest~test_checkout " +
                "cmd:python3~-m~unittest~test_checkout~>~" + LabScene.CheckoutReport + "~2>&1 cmd:cat~" + LabScene.CheckoutReport),
            new("Откройте сохранённое свидетельство в редакторе",
                "terminal-toggle quick-open quick:" + LabScene.CheckoutReport + " quick-choose")
        ], FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareBlockRepair()));
}
