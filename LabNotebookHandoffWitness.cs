using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace TouchTypingStudio
{
    internal sealed partial class LiveStudio
    {
        private const string NotebookHandoffQaMission = "code-project-file-csv-notebook";

        private bool HasNotebookHandoffWitness(Data lesson)
        {
            return verifyMode && lesson.S("mission") == NotebookHandoffQaMission &&
                Environment.GetEnvironmentVariable("TOUCH_TYPING_QA_NOTEBOOK_HANDOFF") == "1";
        }

        // This is a read-only projection of the scene the WPF client already
        // received. No engine command is issued. On a normal profile the
        // automation tree receives no test fixture data.
        private static string NotebookHandoffWitness(Data lesson)
        {
            var scene = lesson.D("scene");
            var editor = scene.Items("windows").FirstOrDefault(w => w.B("active") && w.S("app") == "code") ?? Data.Empty;
            var notebook = editor.D("notebook");
            var metric = notebook.Items("cells").Skip(2).FirstOrDefault() ?? Data.Empty;
            var kernel = notebook.D("kernel");
            var task = lesson.D("task");
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var hash = SHA256.Create())
            {
                foreach (var file in scene.D("terminal").Items("files"))
                {
                    string path = file.S("path");
                    if (!path.StartsWith("/home/student/project/task-list/", StringComparison.Ordinal)) continue;
                    files[path] = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(file.S("content")))).Replace("-", "");
                }
            }
            return new JavaScriptSerializer().Serialize(new
            {
                solved = lesson.I("solved"),
                actions = lesson.I("actionLimit"),
                seconds = lesson.I("timeLimit"),
                finished = lesson.B("finished"),
                mission = lesson.S("mission"),
                route = lesson.S("routeKind"),
                id = task.S("id"),
                keys = task.S("keys"),
                value = task.S("text"),
                url = editor.S("url"),
                body = editor.S("url").EndsWith(".py", StringComparison.Ordinal) ? editor.S("body") : "",
                focus = editor.S("focus"),
                input = editor.S("input"),
                inputCursor = editor.I("cursor"),
                inputAnchor = editor.I("anchor"),
                option = (editor.Items("entries").ElementAtOrDefault(Math.Max(0, editor.I("optionIndex"))) ?? Data.Empty).S("path"),
                status = editor.S("status"),
                position = editor.I("bodyCursor"),
                selection = editor.I("bodyAnchor"),
                selected = notebook.I("selected", -1),
                editing = notebook.B("editing"),
                caret = notebook.I("caret", -1),
                anchor = notebook.I("anchor", -1),
                stale = metric.B("stale"),
                failed = metric.B("failed"),
                output = metric.S("output"),
                outputLabel = metric.S("outputLabel"),
                generation = kernel.I("generation", -1),
                counter = kernel.I("counter", -1),
                files
            });
        }
    }
}
