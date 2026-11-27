#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace Nautilus2027
{
    /// <summary>
    /// Fast, bounded logging into the tools' text boxes. Appending with "box.Text += ..." copies the whole text every time,
    /// which makes long bulk jobs crawl; these helpers append in place and keep only the last <see cref="MaxLines"/> lines on
    /// show (everything still goes to the debug log). Use <see cref="TakeLines"/> at the start of a job to move the listed
    /// files out of the box, so a list of 100k files is worked on from memory and the box only shows the job's log.
    /// </summary>
    internal static class LogBox
    {
        /// <summary>Log lines kept on show in a box.</summary>
        public const int MaxLines = 500;

        /// <summary>Trimming only starts this many lines past the limit, so a busy log isn't rewritten on every line.</summary>
        private const int Slack = 100;

        public const string TrimmedNote = "-- (older lines cleared from this box: the full log is in the debug log)";

        /// <summary>
        /// Appends text (as given: include the line breaks you want) and trims the box to the last <see cref="MaxLines"/> lines.
        /// Safe from any thread. With <paramref name="log"/>, each line also goes to the debug log (leave it off where the
        /// caller already logs the same message).
        /// </summary>
        public static void Append(TextBox box, string text, bool log = false)
        {
            if (box == null || string.IsNullOrEmpty(text)) return;
            if (log) Debug(box, text);
            if (!box.Dispatcher.CheckAccess())
            {
                box.Dispatcher.BeginInvoke(new Action(() => AppendNow(box, text)));
                return;
            }
            AppendNow(box, text);
        }

        /// <summary>Appends one line (a line break is added before it when the box doesn't end with one).</summary>
        public static void Line(TextBox box, string line, bool log = false)
        {
            if (box == null || line == null) return;
            if (!box.Dispatcher.CheckAccess())
            {
                if (log) Debug(box, line);
                box.Dispatcher.BeginInvoke(new Action(() => Line(box, line, false)));
                return;
            }
            if (log) Debug(box, line);
            int len = box.Text.Length;
            bool needBreak = len > 0 && box.Text[len - 1] != '\n';
            AppendNow(box, (needBreak ? Environment.NewLine : "") + line + Environment.NewLine);
        }

        private static void AppendNow(TextBox box, string text)
        {
            if (box.IsUndoEnabled) box.IsUndoEnabled = false;   // the undo history of a log only costs memory
            box.AppendText(text);
            Trim(box);
            box.ScrollToEnd();
        }

        private static void Debug(TextBox box, string text)
        {
            var main = MainWindow.Instance;
            if (main == null) return;
            string who = string.IsNullOrEmpty(box?.Name) ? "" : "[" + box.Name.Replace("TextBox", "") + "] ";
            foreach (var l in text.Split('\n'))
            {
                string t = l.TrimEnd('\r');
                if (t.Trim().Length > 0) main.LogDebugInfo(who + t);
            }
        }

        /// <summary>Keeps the last <see cref="MaxLines"/> lines of a box (call on the UI thread after setting its Text).</summary>
        public static void Trim(TextBox box)
        {
            if (box == null) return;
            string t = box.Text;
            int breaks = 0;
            for (int i = 0; i < t.Length; i++) if (t[i] == '\n') breaks++;
            if (breaks <= MaxLines + Slack) return;
            box.Text = TrimText(t, MaxLines);
            box.ScrollToEnd();
        }

        /// <summary>The last <paramref name="keep"/> lines of a text, after a note that older ones were cleared.</summary>
        public static string TrimText(string t, int keep)
        {
            int breaks = 0;
            for (int i = 0; i < t.Length; i++) if (t[i] == '\n') breaks++;
            if (breaks <= keep) return t;
            int skip = breaks - keep, at = 0;
            for (int i = 0; i < t.Length && skip > 0; i++)
                if (t[i] == '\n') { skip--; at = i + 1; }
            string rest = t.Substring(at);
            if (rest.StartsWith(TrimmedNote, StringComparison.Ordinal)) return rest;
            return TrimmedNote + Environment.NewLine + rest;
        }

        private static long _lastBreath;

        /// <summary>
        /// For jobs that run their loop on the UI thread: gives the window a moment to redraw at most ~10 times a second,
        /// instead of a fixed wait for every item (which adds minutes to a bulk job).
        /// </summary>
        public static async System.Threading.Tasks.Task Breathe()
        {
            long now = Environment.TickCount64;
            if (now - _lastBreath < 100) return;
            _lastBreath = now;
            await System.Threading.Tasks.Task.Delay(1);
        }

        /// <summary>A full path (C:\... or \\server\...), not a log line left over from an earlier job.</summary>
        public static bool LooksLikePath(string l) =>
            (l.Length >= 3 && char.IsLetter(l[0]) && l[1] == ':' && (l[2] == '\\' || l[2] == '/')) || l.StartsWith(@"\\", StringComparison.Ordinal);

        /// <summary>
        /// The box's paths (the files / folders listed for a job; leftover log lines are skipped), and the box cleared ready for the job's log.
        /// Call on the UI thread when the job starts.
        /// </summary>
        public static string[] TakeLines(TextBox box, bool pathsOnly = true)
        {
            if (box == null) return Array.Empty<string>();
            var lines = box.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(l => l.Trim())
                                .Where(l => l.Length > 0 && !l.StartsWith(TrimmedNote, StringComparison.Ordinal) && (!pathsOnly || LooksLikePath(l)))
                                .ToArray();
            box.Clear();
            return lines;
        }
    }
}
