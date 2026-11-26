// Test the actual .NET Framework client against an owned hung session.
// Never starts, suspends, or modifies an installed app or a real profile.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using TouchTypingStudio;

internal static class MenuEngineTransportSmoke
{
    private const string ChildLog = "TOUCH_TYPING_MENU_TRANSPORT_TEST_LOG";

    private static int Main(string[] args)
    {
        string childLog = Environment.GetEnvironmentVariable(ChildLog);
        if (!string.IsNullOrEmpty(childLog)) return RunChild(childLog);
        if (args.Length != 1) return 2;
        string audit = Path.Combine(args[0], "custom-engine-requests.txt");
        var executable = Process.GetCurrentProcess().MainModule.FileName;
        try
        {
            using (var client = StartProbe(executable, audit))
            {
                Require(client.Call("configure").S("operation") == "configure", "first transport responds");
                var clock = Stopwatch.StartNew();
                ExpectFailure(client, "не ответил");
                Require(clock.Elapsed < TimeSpan.FromSeconds(9), "a live child cannot freeze menu indefinitely");
                Require(client.NeedsReconnect, "a timed-out connection cannot be reused");
                ExpectFailure(client, "Связь");
            }
            using (var replacement = StartProbe(executable, audit))
                Require(replacement.Call("configure").S("operation") == "configure", "fresh connection responds");
            Require(File.ReadAllLines(audit).SequenceEqual(new[] { "configure", "lab-start", "configure" }),
                "lab-start was never resent");
            Console.WriteLine("PASS .NET Framework client: bound live hang, explicit reconnect, no replayed start.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static PracticeEngineClient StartProbe(string executable, string audit)
    {
        Environment.SetEnvironmentVariable(ChildLog, audit);
        try { return new PracticeEngineClient(executable, false); }
        finally { Environment.SetEnvironmentVariable(ChildLog, null); }
    }

    private static void ExpectFailure(PracticeEngineClient client, string reason)
    {
        try { client.Call("lab-start"); }
        catch (IOException error)
        {
            Require(error.Message.Contains(reason), error.Message);
            return;
        }
        throw new InvalidOperationException("A stalled start did not fail.");
    }

    private static void Require(bool state, string message)
    {
        if (!state) throw new InvalidOperationException(message);
    }

    private static int RunChild(string audit)
    {
        var json = new JavaScriptSerializer();
        string line;
        while ((line = Console.ReadLine()) != null)
        {
            var operation = new Data(json.DeserializeObject(line)).S("command");
            File.AppendAllLines(audit, new[] { operation });
            if (operation == "lab-start") Thread.Sleep(15000);
            Console.WriteLine(json.Serialize(new { ok = true, value = new { operation } }));
            Console.Out.Flush();
        }
        return 0;
    }
}
