using System.Collections.Generic;
using System.Linq;

namespace TouchTypingStudio;

internal static partial class LiveCourse
{
    private static readonly Dictionary<string, LivePhase> DesktopParts = new()
    {
        ["desk"] = new("Разложить исходники и документацию рядом",
            "open-chrome tile-left open-code tile-right switch-next"),
        ["source"] = new("Найти файл расчётов в проекте",
            "open-files file-home pick:project file-open pick:src file-open pick:spectrum.py file-open"),
        ["capture"] = new("Сохранить снимок рабочего окна и проверить файл",
            "screenshot screenshot-confirm open-files file-home pick:Pictures file-open pick:Screenshots file-open file-next"),
        ["launch"] = new("Перейти из обзора к материалам проекта",
            "overview escape open-chrome maximize pick:https://practice.local/spectrum link-open"),
        ["file"] = new("Положить рабочую копию заметок в загрузки",
            "open-files file-home pick:project file-open pick:notes.txt file-copy file-parent pick:Downloads file-open file-paste"),
        ["recover"] = new("Восстановить заметки после удаления",
            "open-files file-home pick:project file-open pick:notes.txt trash restore-trash pick:notes.txt file-open"),
        ["workspaces"] = new("Перенести средства проверки на соседний стол",
            "open-terminal maximize move-next open-code tile-right workspace-prev workspace-next open-terminal"),
        ["finish"] = new("Проверить каталог и закрыть временный терминал",
            "open-chrome maximize restore open-terminal cmd:pwd minimize open-terminal close-window"),
        ["verify"] = new("Проверить рабочий каталог в терминале",
            "open-terminal cmd:pwd cmd:cd~/home/student/project cmd:ls cmd:head~-n~2~README.md"),
        ["present"] = new("Показать документацию на отдельном рабочем столе",
            "open-chrome move-next maximize fullscreen screenshot screenshot-confirm fullscreen move-prev"),
        ["switch"] = new("Сопоставить открытые окна и вернуться к коду",
            "open-code maximize open-chrome switch-next tile-left open-chrome tile-right open-code"),
        ["sources"] = new("Подготовить среду для текста на двух языках",
            "open-code layout overview escape open-chrome layout url copy escape"),
        ["fetch"] = new("Получить отчёт из браузера",
            "open-chrome pick:spectrum-report.txt link-open downloads link-next close-tab"),
        ["deliver"] = new("Перенести загруженный отчёт в проект и открыть его",
            "open-files file-home pick:Downloads file-open pick:spectrum-report.txt file-cut file-parent pick:project file-open file-paste file-open"),
        ["note-source"] = new("Найти методику и скопировать её текст",
            "open-chrome maximize pick:https://practice.local/spectrum link-open bookmark select-all copy"),
        ["note-editor"] = new("Перенести методику в учебный файл notes.txt",
            "open-code quick-open quick:notes.txt quick-choose select-all paste document-start save"),
        ["note-check"] = new("Проверить сохранённый текст из терминала",
            "open-terminal cmd:pwd cmd:cd~/home/student/project cmd:ls cmd:cat~notes.txt"),
        ["note-reopen"] = new("Найти сохранённую заметку в проекте и снова открыть её",
            "open-files file-home pick:project file-open pick:notes.txt file-open"),
        ["edit-report"] = new("Сохранить редакторскую пометку и проверить содержимое",
            "comment save open-terminal cmd:cd~/home/student/project cmd:head~-n~2~spectrum-report.txt"),
    };

    private static void DesktopMissions(List<LiveMission> list)
    {
        void Add(string app, int level, string id, string title, params string[] parts) =>
            list.Add(new(app, level, app + "-" + id, title, parts.Select(p => DesktopParts[p]).ToArray()));
        Add("ubuntu",0,"start","Организовать рабочий стол разработчика","desk","source","verify","capture","finish");
        Add("ubuntu",0,"file","Подготовить копию заметок","launch","file","source","verify");
        Add("ubuntu",0,"image","Сделать снимок нужного окна","desk","launch","capture","source");
        Add("ubuntu",0,"restore","Восстановить материал и проверить его","file","recover","verify","desk");
        Add("ubuntu",1,"research","Собрать материалы исследования","fetch","deliver","desk","capture");
        Add("ubuntu",1,"window","Организовать работу в двух окнах","desk","switch","source","capture","verify","finish");
        list.Add(new("ubuntu", 1, "ubuntu-gnome-windows", "GNOME: документация и код рядом",
        [
            new("Разложить два окна и сравнить состояние", "tile-left open-code tile-right tile-right tile-right maximize restore tile-right"),
            new("Свернуть редактор, вернуть вкладку и дописать комментарий", "minimize switch-next gnome-write"),
            new("Управлять одним оставшимся окном и сохранить правку", "switch-next tile-left tile-left close-window minimize switch-next restore tile-right save")
        ], FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareGnomeWindowMission()));
        Add("ubuntu",1,"workspaces","Разнести рабочие инструменты по столам","workspaces","source","switch","file");
        list.Add(new("ubuntu", 1, "ubuntu-gnome-workspaces", "GNOME: терминал на втором рабочем столе",
        [
            new("Перенести только терминал и проверить каталог", "move-next cmd:pwd"),
            new("Вернуться к коду и сохранить пометку", "workspace-prev gnome-write save"),
            new("Проверить оба стола, не меняя размещения окон", "workspace-next workspace-prev")
        ], FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareGnomeWorkspaces()));
        list.Add(new("ubuntu", 1, "ubuntu-gnome-same-app", "GNOME: два окна VS Code",
        [
            new("Переключаться только между окнами VS Code", "switch-group-next switch-group-prev")
        ], FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareGnomeSameApp()));
        list.Add(new("ubuntu", 1, "ubuntu-matebook-layout", "MateBook: левый Super и переключение RU/EN",
        [
            new("Открыть обзор левой клавишей ⊞ и вернуться", "gnome-left-super escape"),
            new("Переключить учебный ввод в обе стороны", "layout layout")
        ], FinishAfterRoute: true, Curated: true, Prepare: scene => scene.PrepareMateBook()));
        Add("ubuntu",1,"languages","Подготовить двуязычную рабочую среду","sources","launch","file","recover");
        Add("ubuntu",2,"briefing","Перенести показ на отдельный стол","desk","present","verify","capture");
        Add("ubuntu",2,"review","Подготовить окна для ревью проекта","source","workspaces","switch","capture","file");
        Add("ubuntu",2,"archive","Проверить и восстановить рабочие материалы","fetch","deliver","recover","capture");
        Add("ubuntu",2,"return","Восстановить контекст после переключения столов","workspaces","sources","present","source","finish");
        Add("workday",0,"report","Доставить отчёт и проверить правку","fetch","deliver","edit-report","capture");
        list.Add(new("workday", 0, "workday-source-note", "Перенести методику из браузера в заметку",
            new[] { "note-source", "note-editor", "note-reopen", "note-check" }.Select(p => DesktopParts[p]).ToArray(), FinishAfterRoute: true));
        Add("workday",1,"analysis","Сопоставить отчёт с кодом проекта","desk","fetch","source","deliver","edit-report","present");
        BrowserFileHandoff(list, windows: false);
        Add("workday",2,"handover","Подготовить передачу рабочего проекта","workspaces","fetch","deliver","edit-report","file","capture","present");
    }
    private static void BrowserFileHandoff(List<LiveMission> list, bool windows)
    {
        string explorer = windows ? "win-explorer" : "open-files";
        string parent = windows ? "win-explorer-parent" : "file-parent";
        list.Add(new("workday", 1, windows ? "workday-windows-download-handoff" : "workday-download-handoff",
            windows ? "Windows 10: перенести проверенный отчёт в проект" : "Ubuntu: перенести проверенный отчёт в проект",
            [
                new("Найдите и скачайте CSV из рабочего источника", "url browser-search pick:@target link-open pick:@download link-open"),
                new("Перенесите файл из загрузок в проект", explorer + " pick:Downloads file-open pick:@download file-cut " + parent + " pick:project file-open file-paste"),
                new("Откройте файл уже из проекта и найдите запись", "file-open find research-find escape")
            ], FinishAfterRoute: true, Curated: true, Prepare: scene =>
            {
                if (windows) scene.PrepareWindowsDesktop();
                scene.PrepareBrowserPractice("download");
            }));
    }

}
