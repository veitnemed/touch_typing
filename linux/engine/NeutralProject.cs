using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal static class NeutralProject
{
    internal const string FixtureId = "task-list/v1";
    internal const string Name = "task-list";
    internal const string ProjectPage = "https://practice.local/task-list";
    internal const string GuidePage = "https://practice.local/task-list/guide";
    internal const string ReportName = "task-list-report.txt";
    internal const string ReportBody = "task-list · работа с задачами\nstatus: draft\nsource: tasks.py\n";
    internal const string ProjectBody = "# task-list\nДобавляйте задачи, находите их по названию и сохраняйте список в JSON.";
    internal const string GuideBody = "Как вести список задач\nДобавьте задачу с названием. Найдите её по названию и отметьте выполненной.\nСохраните список задач в JSON. Материалы и отчёт доступны ниже.";
    internal static readonly LabEntry[] Links = [
        new("Как вести список задач", GuidePage, "page"),
        new("Учебный проект task-list", ProjectPage, "page"),
        new("Документация Python", "https://docs.python.org/3/", "page"),
        new("Скачать отчёт о списке задач", ReportName, "download")
    ];

    internal const string App = "from tasks import add_task, find_task, mark_done\nfrom storage import save_tasks\n\n\ndef main():\n    \"\"\"Manage a short task list.\"\"\"\n    tasks = []\n    add_task(tasks, \"Buy tea\")  # Example task\n    task = find_task(tasks, \"Buy tea\")\n    if task:\n        priority = 1\n        mark_done(task)\n        print(f\"Done: {task['title']} #{priority}\")\n    save_tasks(tasks, \"tasks.json\")\n\n\nif __name__ == \"__main__\":\n    main()\n";
    internal const string Tasks = "def add_task(tasks, title):\n    task = {\"title\": title, \"done\": False}\n    tasks.append(task)\n    return task\n\n\ndef mark_done(task):\n    task[\"done\"] = True\n    return task\n\n\ndef find_task(tasks, title):\n    for task in tasks:\n        if task[\"title\"] == title:\n            return task\n    return None\n";
    internal const string Storage = "import json\nfrom pathlib import Path\n\n\ndef save_tasks(tasks, path):\n    text = json.dumps(tasks, indent=2)\n    Path(path).write_text(text, encoding=\"utf-8\")\n";
    internal const string TestTasks = "import unittest\n\nfrom tasks import add_task, find_task, mark_done\n\n\nclass TaskListTest(unittest.TestCase):\n    def test_find_by_title(self):\n        tasks = []\n        tea = add_task(tasks, \"Buy tea\")\n        add_task(tasks, \"Buy coffee\")\n        self.assertIs(find_task(tasks, \"Buy tea\"), tea)\n        self.assertIsNone(find_task(tasks, \"Unknown\"))\n\n    def test_mark_done(self):\n        task = add_task([], \"Buy tea\")\n        mark_done(task)\n        self.assertTrue(task[\"done\"])\n";

    internal static readonly ProjectExercise[] Exercises = [
        new("app.py", "Запуск учебного списка задач", 0, App),
        new("tasks.py", "Поиск и изменение задач", 1, Tasks),
        new("storage.py", "Сохранение задач в JSON", 1, Storage),
        new("test_tasks.py", "Тесты списка задач", 2, TestTasks)
    ];

    private static readonly Dictionary<string, string> NeutralRouteTokens = new(StringComparer.Ordinal) {
        ["quick:spectrum.py"] = "quick:tasks.py",
        ["quick:signals.py"] = "quick:storage.py",
        ["quick:README.md"] = "quick:app.py",
        ["cmd:head~-n~2~README.md"] = "cmd:head~-n~2~app.py",
        ["quick:model.py"] = "quick:app.py",
        ["cmd:ls~-la~src"] = "cmd:ls~-la~.",
        ["cmd:head~-n~2~src/signals.py"] = "cmd:head~-n~2~storage.py",
        ["cmd:echo~spectrum~>~handoff/report.txt"] = "cmd:echo~task-list~>~handoff/report.txt",
        ["cmd:cp~src/signals.py~backup/signals.py"] = "cmd:cp~storage.py~backup/storage.py",
        ["cmd:head~-n~2~backup/signals.py"] = "cmd:head~-n~2~backup/storage.py",
        ["cmd:wc~-l~backup/signals.py"] = "cmd:wc~-l~backup/storage.py",
        ["cmd:grep~-n~def~src/spectrum.py"] = "cmd:grep~-n~def~tasks.py",
        ["cmd:wc~-l~src/spectrum.py"] = "cmd:wc~-l~tasks.py",
        ["cmd:tail~-n~2~src/spectrum.py"] = "cmd:tail~-n~2~tasks.py",
        ["pick:https://practice.local/spectrum"] = "pick:" + GuidePage,
        ["pick:https://github.com/veitnemed/piling-main"] = "pick:" + ProjectPage,
        ["pick:spectrum-report.txt"] = "pick:" + ReportName,
        ["cmd:head~-n~2~spectrum-report.txt"] = "cmd:head~-n~2~" + ReportName
    };

    private static readonly Dictionary<string, string> NeutralPhaseTitles = new(StringComparer.Ordinal) {
        ["Найти реализацию преобразования в проекте"] = "Найти реализацию списка задач",
        ["Открыть функции обработки сигнала"] = "Открыть функции сохранения задач",
        ["Найти расчёт параметров модели"] = "Найти точку входа списка задач",
        ["Найти файл расчётов в проекте"] = "Найти модуль списка задач в проекте",
        ["Найти файлы с расчётами и проверить определения"] = "Найти модули проекта и проверить определения"
    };
    private static readonly Dictionary<string, string> NeutralTitles = new(StringComparer.Ordinal) {
        ["code-first-fix"] = "Подготовить рабочую копию точки входа",
        ["code-refactor"] = "Перестроить фрагмент списка задач"
    };

    internal const string TestSelector = "test_tasks";
    private const string TestCommand = "python3 -m unittest " + TestSelector;
    private const string TestFolder = "/home/student/project/";

    internal static IEnumerable<LiveMission> MissionCatalog =>
        LiveCourse.Missions.Select(Mission).Append(new("terminal", 0, "terminal-task-list-tests",
            "Проверить список задач в терминале",
            [
                new("Перейдите в учебный проект и запустите проверку",
                    "cmd:cd~/home/student/project cmd:" + TestCommand.Replace(' ', '~')),
                new("Сверьте функции списка задач с отчётом",
                    "cmd:head~-n~2~tasks.py")
            ], FinishAfterRoute: true, Curated: true));

    // Closed educational simulation, not a Python interpreter. Recognize only
    // the supplied task source and the two documented practice mutations.
    // An unknown edit is explicitly outside the verifier rather than a pass.
    internal static string RunTests(VirtualTerminal terminal)
    {
        if (terminal.Cwd != TestFolder.TrimEnd('/') ||
            terminal.ReadFile(TestFolder + "test_tasks.py") != TestTasks)
            throw new ArgumentException("УЧЕБНЫЙ ТЕСТ · нет результата: запустите из корня task-list с исходным test_tasks.py.");
        string raw = terminal.ReadFile(TestFolder + "tasks.py") ?? "";
        bool completes = raw.Contains("task[\"done\"] = True", StringComparison.Ordinal);
        bool finds = raw.Contains("            return task\n", StringComparison.Ordinal);
        var canonical = raw.Replace("task[\"done\"] = False", "task[\"done\"] = True", StringComparison.Ordinal)
            .Replace("            return None\n", "            return task\n", StringComparison.Ordinal);
        if (canonical != Tasks)
            throw new ArgumentException("УЧЕБНЫЙ ТЕСТ · нет результата: изменения в tasks.py не входят в поддерживаемый учебный вариант.");
        var failures = new List<string>();
        if (!finds) failures.Add("test_find_by_title: ожидается найденная задача, получено None");
        if (!completes) failures.Add("test_mark_done: ожидается done=True, получено False");
        if (failures.Count != 0)
            throw new ArgumentException("УЧЕБНЫЙ ТЕСТ · Ran 2 tests · FAILED: " + string.Join("; ", failures));
        return "УЧЕБНЫЙ ТЕСТ · test_tasks\nRan 2 tests\nOK";
    }

    // Preserve the exact old mission definitions for legacy draft replay.
    internal static LiveMission Mission(LiveMission mission)
    {
        var phases = mission.Phases.Select(phase => phase with {
            Title = NeutralPhaseTitles.GetValueOrDefault(phase.Title, phase.Title),
            Route = string.Join(" ", phase.Route
                .Replace(" symbols sym:fft symbol-open", " symbols symbol-open", StringComparison.Ordinal)
                .Replace("pick:src file-open pick:spectrum.py", "pick:tasks.py", StringComparison.Ordinal)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(token => NeutralRouteTokens.GetValueOrDefault(token, token)))
        }).ToArray();
        return mission with { Title = NeutralTitles.GetValueOrDefault(mission.Id, mission.Title), Phases = phases };
    }
}
