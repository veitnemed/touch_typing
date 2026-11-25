using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Automation;

internal sealed class NotebookFileHandoffState
{
    public int solved { get; set; }
    public int actions { get; set; }
    public int seconds { get; set; }
    public int position { get; set; }
    public int selection { get; set; }
    public int selected { get; set; }
    public int caret { get; set; }
    public int anchor { get; set; }
    public int generation { get; set; }
    public int counter { get; set; }
    public bool finished { get; set; }
    public bool editing { get; set; }
    public bool stale { get; set; }
    public bool failed { get; set; }
    public string mission { get; set; }
    public string route { get; set; }
    public string id { get; set; }
    public string keys { get; set; }
    public string value { get; set; }
    public string url { get; set; }
    public string body { get; set; }
    public string focus { get; set; }
    public string status { get; set; }
    public string output { get; set; }
    public string outputLabel { get; set; }
    public Dictionary<string, string> files { get; set; }
}

internal static partial class LiveUiSmoke
{
    private const string NotebookFileHandoffId = "code-project-file-csv-notebook";
    private static readonly string[] ModuleEdits = { @"data/[a-z-]+\.csv", @"return [a-z]+\(values\)" };
    private static readonly Dictionary<string, string> HandoffValues = new Dictionary<string, string>
    {
        { "warehouse-count", "38" }, { "delivery-minutes", "26" }, { "sample-duration", "16" }
    };
    private static readonly JavaScriptSerializer HandoffJson = new JavaScriptSerializer();

    private static NotebookFileHandoffState ReadNotebookFileHandoff()
    {
        var id = Visible("LabResult") ? "LabResult" : "LabDesktop";
        var text = Find(id).Current.ItemType;
        if (string.IsNullOrEmpty(text) || text[0] != '{')
            throw new InvalidOperationException("No connected-workspace witness in " + id + ": " + text);
        return HandoffJson.Deserialize<NotebookFileHandoffState>(text);
    }

    private static void HandoffFrame(string suffix, string caption)
    {
        Wait(200);
        CaptureReferenceFrame("wpf-nb-flow-" + suffix);
        Console.WriteLine("FRAME " + suffix + " " + caption);
        if (Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_VIDEO") == "1") Wait(2200);
    }

    private static string Fingerprint(string text)
    {
        using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
    }

    private static void ReplaceHandoffFragment(string old, string next, int solved)
    {
        Chord("Ctrl+F");
        Chord("Ctrl+A");
        EnterCharacters(old);
        Key("Enter");
        Key("Esc");
        var found = ReadNotebookFileHandoff();
        int lo = Math.Min(found.position, found.selection);
        int hi = Math.Max(found.position, found.selection);
        Check(found.solved == solved && found.focus == "body" && lo >= 0 && hi <= found.body.Length &&
              found.body.Substring(lo, hi - lo) == old, "Find selected exactly: " + old);
        EnterCharacters(next);
        Check(ReadNotebookFileHandoff().solved == solved, "A text edit has not earned a Save credit.");
    }

    private static void AdvanceNotebookFileHandoff(NotebookFileHandoffState task)
    {
        if (new[] { "quick-choose", "palette-choose", "file-name-commit" }.Contains(task.id))
            EnterCharacters(task.value);
        if (task.id == "save" && task.value.Contains("from csv import"))
        {
            foreach (string pattern in ModuleEdits)
            {
                var old = Regex.Match(task.body, pattern);
                var next = Regex.Match(task.value, pattern);
                Check(old.Success && next.Success && old.Value != next.Value, "The module has a specific, observable repair: " + pattern);
                ReplaceHandoffFragment(old.Value, next.Value, task.solved);
            }
            Check(ReadNotebookFileHandoff().body == task.value, "Only the two intended fragments changed before saving.");
        }
        string keys = task.keys.Length == 0 ? "Enter" : task.keys;
        for (int index = 0; index < (keys == "Down" || keys == "Tab" ? 35 : 1); index++)
        {
            Chord(keys);
            if (ReadNotebookFileHandoff().solved == task.solved + 1) break;
        }
        Until(delegate { return ReadNotebookFileHandoff().solved == task.solved + 1; },
            "physical input earns credit once for " + task.id + " at " + task.solved);
    }

    internal static int CheckNotebookFileHandoffWindow(string executable, string destination)
    {
        string originalWitness = Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_NOTEBOOK_HANDOFF");
        try
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_DATA_ROOT")))
                throw new InvalidOperationException("Require a new isolated training profile.");
            if (SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero)
                throw new InvalidOperationException("Cannot verify physical window size and keyboard input.");
            Environment.SetEnvironmentVariable("TOUCH_TYPING_QA_NOTEBOOK_HANDOFF", "1");
            evidenceDirectory = Directory.CreateDirectory(destination).FullName;
            app = Process.Start(new ProcessStartInfo(executable, "--qa --qa-home") {
                WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false });
            Until(delegate { app.Refresh(); trainer = app.MainWindowHandle; return trainer != IntPtr.Zero; }, "owned WPF trainer", 18000);
            window = AutomationElement.FromHandle(trainer);
            Focus();
            TypingProofClient.Fit(app, trainer);
            Invoke("LabDesktopProfile-ubuntu");
            OpenLiveConfiguration("code");
            Select("LabMode", 1);
            Select("LabLevel", 2);
            Select("LabDuration", 0);
            Select("LabTimer", 0);
            SelectMissionByName("Сверить выгрузку, исправить Python-модуль и подтвердить блокнот");
            HandoffFrame("01-config", "Chosen from the shared VS Code catalog");
            Key("F5");
            Until(delegate { return Visible("LabDesktop") && Visible("LabTask") && Name("LabPause").StartsWith("Пауза"); },
                "physical F5 launches the named individual mission");
            VirtualEnglish();
            var started = ReadNotebookFileHandoff();
            Check(started.mission == NotebookFileHandoffId && started.route == "mission" &&
                  started.actions == 0 && started.seconds == 0, "Named route has its own result rather than the 40-action course.");
            var before = started.files;
            string incoming = before.Keys.Single(path => path.Contains("/incoming/") && path.EndsWith(".csv"));
            string stem = Path.GetFileNameWithoutExtension(incoming);
            string root = incoming.Substring(0, incoming.IndexOf("/incoming/", StringComparison.Ordinal));
            string archive = root + "/data/" + stem + ".csv";
            string approved = root + "/data/" + stem + "-updated.csv";
            string oldNotebook = root + "/" + stem + "-verification.ipynb";
            string finalNotebook = root + "/verified/" + stem + "-verification.ipynb";
            string importedModule = root + "/" + stem.Replace('-', '_') + "_metrics.py";
            string decoy = root + "/archive_metrics.py";
            Check(before[incoming] != before[archive], "The conflicting names hide genuinely different CSVs.");
            string prior = null, savedNotebook = null;
            bool conflict = false, moved = false, definition = false, edited = false, stale = false, fresh = false;

            for (int step = 0; step < 39; step++)
            {
                var goal = ReadNotebookFileHandoff();
                Check(goal.solved == step && !goal.finished, "Lesson is active at action " + step);
                if (goal.id == "code-definition")
                {
                    Key("Esc"); Key("F12");
                    var rejected = ReadNotebookFileHandoff();
                    Check(rejected.solved == step && rejected.url == oldNotebook && !rejected.editing,
                        "F12 in notebook selection mode cannot win a jump from a text caret.");
                    Key("Enter");
                    goal = ReadNotebookFileHandoff();
                    Check(goal.editing && goal.selected == 2 && goal.solved == step, "Correction puts the caret back in the call cell.");
                }
                Console.WriteLine("STEP " + (step + 1) + " " + goal.id + " " + goal.keys);
                AdvanceNotebookFileHandoff(goal);
                var scene = ReadNotebookFileHandoff();
                if (goal.value == "!conflict")
                {
                    Check(scene.status.Contains("Ничего не заменено") && before.Count == scene.files.Count &&
                        before.All(entry => scene.files.ContainsKey(entry.Key) && scene.files[entry.Key] == entry.Value),
                        "Conflicting paste leaves every file byte-for-byte intact.");
                    conflict = true;
                    HandoffFrame("02-conflict", "Same-name CSV remains unchanged");
                }
                if (goal.id == "file-paste" && !moved && scene.files.ContainsKey(approved))
                {
                    Check(scene.url == approved && scene.files[approved] == before[incoming] &&
                        scene.files[archive] == before[archive] && !scene.files.ContainsKey(incoming),
                        "The correct CSV really moved and its existing namesake survived.");
                    moved = true;
                    HandoffFrame("03-input-moved", "New CSV opened at its actual destination");
                }
                if (goal.id == "notebook-run" && scene.selected == 2 && scene.output.Length > 0)
                {
                    Check(!scene.failed && scene.output != HandoffValues[stem], "Uncorrected module visibly produces the old answer.");
                    prior = scene.output;
                    HandoffFrame("04-prior-result", "Old computation is shown, not credited as current");
                }
                if (goal.id == "code-definition")
                {
                    Check(scene.url == importedModule && scene.position >= 4 && scene.position <= scene.body.Length &&
                        scene.body.Substring(scene.position - 4, 4) == "def ",
                        "F12 opens the imported symbol, not the identically named function in the archive.");
                    definition = true;
                    HandoffFrame("05-definition", "Imported Python definition, caret in the right place");
                }
                if (goal.id == "save" && goal.value.Contains("from csv import"))
                {
                    Check(scene.url == importedModule && scene.files[importedModule] == Fingerprint(goal.value) &&
                        scene.files[importedModule] != before[importedModule] && scene.files[decoy] == before[decoy],
                        "The saved module has both edits; the real decoy file stayed unchanged.");
                    edited = true;
                }
                if (goal.id == "code-back")
                {
                    Check(scene.url == oldNotebook && scene.selected == 2 && scene.editing && scene.caret == 0 &&
                        scene.anchor == 0 && scene.stale && scene.output == prior,
                        "Back restores the exact notebook cell/caret and labels the old computation as stale.");
                    stale = true;
                    HandoffFrame("06-stale", "Returned to the call, old output still marked");
                }
                if (goal.value == "Restart Kernel")
                    Check(scene.generation == 2 && scene.counter == 0,
                        "Native palette action starts a genuinely clean kernel, with no prior variable execution.");
                if (goal.value == "Run All Cells")
                {
                    Check(scene.generation == 2 && scene.counter >= 2 && !scene.stale && !scene.failed &&
                        scene.output == HandoffValues[stem] && scene.output != prior,
                        "A fresh kernel computes the expected answer from the edited Python and the moved CSV.");
                    fresh = true;
                    HandoffFrame("07-fresh", "Clean run from the real CSV: " + scene.output);
                }
                if (goal.id == "save" && scene.url == oldNotebook && fresh)
                {
                    savedNotebook = scene.files[oldNotebook];
                    Check(savedNotebook != before[oldNotebook], "Save wrote the verified notebook instead of leaving the empty original.");
                }
            }
            Until(delegate { return Visible("LabResult"); }, "actual result after 39 actions");
            var final = ReadNotebookFileHandoff();
            Check(conflict && moved && definition && edited && stale && fresh && savedNotebook != null,
                "Each independently checked milestone was observed through WPF input.");
            Check(final.finished && final.solved == 39 && Find("LabResult").Current.HelpText.Contains("Выполнено 39."),
                "The named mission reaches its own native result.");
            Check(final.url == finalNotebook && final.files[finalNotebook] == savedNotebook && !final.files.ContainsKey(oldNotebook) &&
                final.files[archive] == before[archive] && final.files[approved] == before[incoming] && final.files[decoy] == before[decoy],
                "The real saved notebook moved without duplicates or changes to other files.");
            Check(final.counter == 0 && final.output == HandoffValues[stem] && final.outputLabel == "Сохранённый вывод",
                "Reopening the new path retains saved output but not live kernel state.");
            HandoffFrame("08-result", "Saved notebook reopened from verified");
            Console.WriteLine("PASS native WPF CSV → F12 → correction → clean recompute → save/move/reopen: " +
                final.output + "; 39 actions at DPI " + GetDpiForWindow(trainer) + "; " + final.url);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            try
            {
                if (app != null && !app.HasExited)
                {
                    DumpProcessWindows();
                    if (GetForegroundWindow() == trainer) HandoffFrame("failure", "Unexpected state");
                }
            }
            catch (Exception extra) { Console.Error.WriteLine(extra.Message); }
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("TOUCH_TYPING_QA_NOTEBOOK_HANDOFF", originalWitness);
            if (app != null && !app.HasExited) { app.Kill(); app.WaitForExit(4000); }
        }
    }
}

internal static class NotebookFileHandoffMain
{
    [STAThread]
    private static int Main(string[] args)
    {
        return args.Length == 2 ? LiveUiSmoke.CheckNotebookFileHandoffWindow(
            Path.GetFullPath(args[0]), Path.GetFullPath(args[1])) : 2;
    }
}
