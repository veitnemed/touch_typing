using System;
using System.Collections.Generic;

namespace TouchTypingStudio;

internal static class NotebookKernelRegression
{
    private static void Need(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); }

    static void Main()
    {
        var files = new Dictionary<string, string> {
            ["metrics.py"] = "from csv import DictReader\nfrom statistics import mean\n" +
                             "def report():\n    rows = DictReader(open('data/today.csv'))\n" +
                             "    return mean([float(row['hours']) for row in rows if row['state'] == 'ready'])\n",
            ["data/today.csv"] = "item,hours,state\r\n\"desk, left\",4,ready\r\n\"box\"\"2\",8,ready\r\nold,100,archived\r\n",
            ["other.py"] = "def report():\n    return 999\n"
        };
        const string source = "from metrics import report as value\nprint('Current #1')\nprint('rows, ready')\nvalue()";
        string Reads(string path) => files.GetValueOrDefault(path);
        var kernel = new NotebookPythonKernel();
        var a = kernel.Run(source, Reads);
        Need(a.Error == null && a.Result == "6" && a.Stream == "Current #1\nrows, ready\n", "CSV quoting/stdlib/first run");
        Need(kernel.Fresh(a, source, Reads), "result should depend on loaded inputs");
        Need(a.Files.Count == 2 && a.Files.ContainsKey("metrics.py") && a.Files.ContainsKey("data/today.csv"), "ignore decoy module");

        files["data/today.csv"] = files["data/today.csv"].Replace(",8,ready", ",12,ready", StringComparison.Ordinal);
        Need(!kernel.Fresh(a, source, Reads), "input CSV changes must invalidate previous output");
        Need(kernel.Run("value()", Reads).Result == "8", "the same function must compute on changed input");

        files["metrics.py"] = files["metrics.py"].Replace("return mean(", "return sum(", StringComparison.Ordinal);
        var cached = kernel.Run(source, Reads);
        Need(cached.Result == "8" && !kernel.Fresh(cached, source, Reads), "cached imports must stay recognizably stale");

        kernel.Reset();
        Need(kernel.Run("value()", Reads).Error?.Name == "NameError", "restart must forget imported names");
        var c = kernel.Run(source, Reads);
        Need(c.Result == "16" && kernel.Fresh(c, source, Reads), "re-import fixed code after clean restart");
        Need(kernel.Run("from math import ceil\nceil(4.2) + len([1, 2, 3])", Reads).Result == "8", "working stdlib arithmetic");

        int attempted = 0;
        string UnexpectedFile(string input) { attempted++; return "host content"; }
        Need(kernel.Run("open('../local.csv')", UnexpectedFile).Error?.Name == "PermissionError", "no upward workspace traversal");
        Need(kernel.Run("open('/nothing.csv')", UnexpectedFile).Error?.Name == "PermissionError", "no absolute workspace traversal");
        Need(kernel.Run("open('data/today.csv', 'w')", UnexpectedFile).Error?.Name == "TypeError", "virtual input only, no write");
        Need(kernel.Run("open('data/today.csv').write('changed')", Reads).Error?.Name == "TypeError", "no Python object escape");
        Need(attempted == 0, "invalid accesses must not reach any project IO");
        Need(kernel.Run(new string('-', 4000) + "1", Reads).Error?.Name == "SyntaxError", "bounded parse depth");
        Need(kernel.Run("def repeat():\n    return repeat()\nrepeat()", Reads).Error?.Name == "Учебное ядро", "bounded call depth");
        files["bad.py"] = "def result():\n    return 8 / 0\n";
        var error = kernel.Run("from bad import result\nresult()", Reads).Error;
        Need(error?.Name == "ZeroDivisionError" && error.Trace.Contains("bad.py, line 2"), "traceback points to the real saved function");
        Console.WriteLine("PASS notebook kernel: standard/library imports, physical data dependency, stale imports, reset, bounds and traceback.");
    }
}
