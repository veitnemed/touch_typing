using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal static partial class LiveCourse
{
    private static readonly Dictionary<string, LivePhase> EditorParts = new()
    {
        ["spectrum"] = new("Найти реализацию преобразования в проекте",
            "quick-open quick:spectrum.py quick-choose symbols sym:fft symbol-open select-line copy"),
        ["signal"] = new("Открыть функции обработки сигнала",
            "quick-open quick:signals.py quick-choose symbols symbol-open select-line copy"),
        ["readme"] = new("Подготовить рабочую копию описания проекта",
            "quick-open quick:README.md quick-choose select-line copy new-file paste save"),
        ["note"] = new("Сохранить фрагмент в отдельном файле",
            "select-line copy new-file paste save close-tab"),
        ["patch"] = new("Проверить правку строки и оставить рабочую версию",
            "comment save undo redo save changes focus-editor"),
        ["layout"] = new("Разместить исходник и открыть средства проверки",
            "split explorer focus-editor panel terminal-toggle terminal-clear terminal-toggle"),
        ["model"] = new("Найти расчёт параметров модели",
            "quick-open quick:model.py quick-choose symbols symbol-open select-line copy"),
        ["reorder"] = new("Перенести строку и проверить изменение",
            "move-line-down save changes focus-editor undo save"),
        ["review"] = new("Сверить изменённый код с сохранённой версией",
            "select-line cut undo comment changes focus-editor save"),
        ["search"] = new("Найти строку в исходнике и вернуться к проекту",
            "select-line find escape project-search focus-editor explorer focus-editor"),
        ["tools"] = new("Проверить инструменты и освободить место для кода",
            "extensions explorer sidebar focus-editor panel terminal-toggle terminal-toggle"),
        ["deduplicate"] = new("Уберите дублированную строку из рабочей копии",
            "document-start select-line copy new-file paste paste document-start select-line replace replace-field replace-one save close-tab"),
        ["identifiers"] = new("Сопоставьте объявления функций в модуле",
            "quick-open quick:signals.py quick-choose document-start occurrence occurrence find escape"),
        ["archive"] = new("Сохранить выдержку и восстановить исходник",
            "select-line cut new-file paste save close-tab undo save"),
    };
    private static void EditorMissions(List<LiveMission> list)
    {
        void Add(int level, string id, string title, params string[] parts) =>
            list.Add(new("code", level, "code-" + id, title, parts.Select(p => EditorParts[p]).ToArray()));
        Add(0,"guide","Составить заметку по проекту","signal","note","spectrum","readme","search");
        Add(0,"first-fix","Подготовить первую правку модели","model","patch","signal","review","tools");
        Add(0,"scratch","Собрать два фрагмента в рабочие копии","spectrum","note","model","readme","layout");
        Add(0,"navigation","Найти путь от расчёта к документации","signal","search","model","spectrum","readme");
        Add(1,"refactor","Перестроить фрагмент обработки сигнала","signal","reorder","deduplicate","model","search");
        Add(1,"extract","Извлечь код для сравнения","model","archive","spectrum","note","layout");
        Add(1,"console","Подготовить рабочее место для проверки","spectrum","layout","signal","patch","tools");
        Add(1,"symbols","Сопоставить определения функций","model","spectrum","identifiers","signal","note");
        Add(2,"handover","Передать код и пояснение на ревью","signal","archive","model","patch","readme","tools");
        Add(2,"repair","Восстановить рабочую версию после правки","spectrum","reorder","model","review","search","layout");
        Add(2,"inspection","Проверить связанные правки в двух модулях","signal","patch","spectrum","reorder","identifiers","deduplicate");
        Add(2,"publish","Подготовить выдержки к разбору проекта","model","note","spectrum","archive","signal","readme");
    }
}
