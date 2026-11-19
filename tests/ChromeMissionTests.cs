using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TouchTypingStudio
{
    internal static class ChromeMissionTests
    {
        public static void Run()
        {
            var briefing = new DesktopBriefing("audit", "Сверка записей", "record", "MISSING", "audit");
            foreach (int number in new[] { 1005, 1582, 9989 })
            {
                string journal = DesktopCourse.Page("Journal", briefing, number, true);
                var rows = Regex.Matches(journal, @"<li>.*?</li>");
                if (rows.Count != 15) throw new Exception("Journal must contain 15 independent records");
                for (int i = 0; i < rows.Count; i++)
                {
                    bool target = i == number % rows.Count;
                    if (rows[i].Value.Contains(briefing.Keyword) != target ||
                        rows[i].Value.Contains("<strong>" + number + "</strong>") != target)
                        throw new Exception("Journal status is not paired with its unique case number");
                }
            }

            string chrome = DesktopCourseWindows.FindChrome();
            if (chrome == null) { Console.WriteLine("SKIP: Chrome form rendering (Chrome not installed)."); return; }
            string root = Path.Combine(Path.GetTempPath(), "WorkdayChrome-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                const string title = "Card 123";
                if (DesktopCourse.Page(title, briefing, 1580, false).Contains("id=followup"))
                    throw new Exception("The direct handoff should finish after submitting the case number");
                string card = DesktopCourse.Page(title, briefing, 1005, false);
                // Execute against the generated form, including DOM submit, input and focus
                // behavior. The test driver is appended only to this disposable page.
                const string probe = @"<output id=qa></output><script>
                    const input=document.querySelector('input'),form=input.form,status=document.querySelector('[role=status]');
                    function submit(value){input.value=value;input.dispatchEvent(new Event('input'));form.requestSubmit();}
                    submit('1581');let wrong=document.title==='Card 123' && status.textContent.includes('MISSING')
                        && document.activeElement===input && input.selectionStart===0 && input.selectionEnd===4;
                    submit(' 1005 ');let right=document.title==='Card 123 / Submitted' && status.textContent.includes('1005');
                    const clock=document.getElementById('time');right=right && document.activeElement===clock;
                    clock.value='19:47';clock.form.requestSubmit();
                    let retry=document.title==='Card 123 / Submitted' && document.activeElement===clock && clock.selectionEnd===5;
                    clock.value='8:00';clock.form.requestSubmit();let sent=document.title==='Card 123 / Time confirmed';
                    input.value='1234';input.dispatchEvent(new Event('input'));
                    document.getElementById('qa').textContent=(wrong && right && retry && sent && document.title==='Card 123' &&
                        !status.textContent && document.getElementById('followup').hidden)?'PASS':'FAIL';
                    </script>";
                string path = Path.Combine(root, "карточка проекта.html");
                File.WriteAllText(path, card.Replace("</main>", probe + "</main>"), new UTF8Encoding(false));
                CheckPage(chrome, root, path);

                string decks = Path.Combine(root, "практика со ссылками", "DesktopPractice", "decks");
                Directory.CreateDirectory(decks);
                File.WriteAllText(Path.Combine(decks, "browser.txt"), "0 1");
                using (var course = new DesktopCourse(Path.Combine(root, "практика со ссылками")))
                {
                    string catalog = Path.Combine(course.Folder, "pages", "board-1.html");
                    string guide = Path.Combine(course.Folder, "pages", "notes-1.html");
                    string address = new Uri(guide).AbsoluteUri;
                    if (!DesktopCourse.IsFileAddress(address, guide) || !DesktopCourse.IsFileAddress(Uri.UnescapeDataString(address), guide) ||
                        DesktopCourse.IsFileAddress(address + "#excerpt", guide) || DesktopCourse.IsFileAddress(address, catalog) ||
                        DesktopCourse.IsFileAddress(guide, guide))
                        throw new Exception("Clipboard accepted an unrelated path or rejected a localized Chrome address");
                    const string linkProbe = @"<output id=qa></output><script>
                        const target=document.getElementById('resource'),other=document.querySelector('a'),title=document.title;
                        function tabTo(anchor){anchor.focus();document.dispatchEvent(new KeyboardEvent('keyup',{key:'Tab'}));}
                        tabTo(other);let neutral=document.title===title;tabTo(target);
                        let marked=document.title===title+' / Resource link' && target.href===new URL('notes-1.html',location.href).href;
                        tabTo(other);document.getElementById('qa').textContent=(neutral && marked && document.title===title)?'PASS':'FAIL';
                        </script>";
                    File.WriteAllText(catalog, File.ReadAllText(catalog).Replace("</main>", linkProbe + "</main>"), new UTF8Encoding(false));
                    CheckPage(chrome, root, catalog);
                    if (!File.ReadAllText(guide).Contains("Рабочая инструкция")) throw new Exception("The selected document is missing");
                }
                Console.WriteLine("PASS: Chrome journal number and time, corrections, focused catalog link, staged document and exact clipboard-address comparison (headless).");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void CheckPage(string chrome, string root, string path)
        {
            var start = new ProcessStartInfo(chrome,
                "--headless --dump-dom --disable-background-networking --disable-component-update --no-first-run " +
                "--user-data-dir=\"" + Path.Combine(root, "profile") + "\" \"" + new Uri(path).AbsoluteUri + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(30000)) { process.Kill(); throw new TimeoutException("Chrome form check timed out"); }
                if (process.ExitCode != 0 || !output.Result.Contains("<output id=\"qa\">PASS</output>"))
                    throw new Exception("Chrome did not confirm " + Path.GetFileName(path) + " " +
                        Regex.Match(output.Result, "<output id=\"qa\">(.*?)</output>").Groups[1].Value);
            }
        }
    }
}
