using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace TouchTypingStudio
{
    internal sealed class Data
    {
        private readonly object value;
        public static readonly Data Empty = new Data(null);
        public Data(object value) { this.value = value; }
        public bool Exists { get { return value != null; } }

        public Data D(string key)
        {
            var fields = value as Dictionary<string, object>;
            object item;
            return fields != null && fields.TryGetValue(key, out item) ? new Data(item) : Empty;
        }

        public string S(string key, string fallback = "") { return D(key).Value(fallback); }
        public string Value(string fallback = "")
        { return value == null ? fallback : Convert.ToString(value, CultureInfo.InvariantCulture); }

        public int I(string key, int fallback = 0) { return D(key).Integer(fallback); }
        public int Integer(int fallback = 0)
        {
            int number;
            return int.TryParse(Value(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ? number : fallback;
        }

        public double N(string key, double fallback = 0) { return D(key).Number(fallback); }
        public double Number(double fallback = 0)
        {
            double number;
            return double.TryParse(Value(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number : fallback;
        }

        public bool B(string key, bool fallback = false)
        {
            bool result;
            return bool.TryParse(S(key), out result) ? result : fallback;
        }

        public IEnumerable<Data> Items(string key) { return D(key).Items(); }
        public IEnumerable<Data> Items()
        {
            var items = value as object[];
            return items == null ? Enumerable.Empty<Data>() : items.Select(item => new Data(item));
        }
    }

    internal sealed class PracticeEngineClient : IDisposable
    {
        private readonly Process process;
        private readonly StreamWriter input;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 12 * 1024 * 1024 };
        public string Notice { get; private set; }
        public bool HasExited { get { return process.HasExited; } }

        public PracticeEngineClient(string executable, bool verify)
        {
            if (!File.Exists(executable)) throw new FileNotFoundException("Распакуйте архив целиком. Рядом с приложением нужна папка engine.");
            process = Process.Start(new ProcessStartInfo
            {
                FileName = executable, Arguments = verify ? "--verify" : "", CreateNoWindow = true,
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(executable)
            });
            input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
        }

        public Data Call(string command, params object[] fields)
        {
            var message = new Dictionary<string, object> { { "command", command } };
            for (int i = 0; i < fields.Length; i += 2) message.Add((string)fields[i], fields[i + 1]);
            input.WriteLine(json.Serialize(message));
            string response = process.StandardOutput.ReadLine();
            if (response == null) throw new IOException("Учебный движок завершился. Откройте приложение снова.");
            var reply = new Data(json.DeserializeObject(response));
            Notice = reply.S("storageError");
            if (!reply.B("ok")) throw new InvalidOperationException(reply.S("error", "Не удалось выполнить действие."));
            return reply.D("value");
        }

        public void Dispose()
        {
            try { input.Dispose(); }
            catch (IOException) { }
            finally
            {
                try { if (!process.WaitForExit(800)) process.Kill(); }
                catch (InvalidOperationException) { }
                finally { process.Dispose(); }
            }
        }
    }
}
