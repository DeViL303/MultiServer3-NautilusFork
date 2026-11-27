#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using Nautilus2027.Formats;
using Ookii.Dialogs.Wpf;

namespace Nautilus2027
{
    /// <summary>
    /// Shared helpers for tools that keep a list of files in a TextBox (one path per line, result lines
    /// start with "--"): adding dropped or browsed files, reading the list back, and appending reports.
    /// </summary>
    internal static class FileListTools
    {
        private static MainWindow Main => MainWindow.Instance;

        public static bool HasExtension(string path, string[] extensions) =>
            extensions.Any(ext => string.Equals(Path.GetExtension(path), ext, StringComparison.OrdinalIgnoreCase));

        /// <summary>Add dropped/selected files (folders are searched) to a tool's list, skipping duplicates.</summary>
        /// <param name="accept">Optional extra test for files found inside dropped folders / objects (files dropped one by one are always taken).</param>
        public static void AddFiles(IEnumerable<string> items, string[] extensions, TextBox list, TextBlock dragText, string toolName, Func<string, bool> accept = null)
        {
            var valid = new List<string>();
            foreach (var item in items)
            {
                if (Directory.Exists(item))
                    valid.AddRange(Directory.EnumerateFiles(item, "*", SearchOption.AllDirectories).Where(f => HasExtension(f, extensions) && Accepts(accept, f)));
                else if (File.Exists(item) && HasExtension(item, extensions))
                    valid.Add(item);
            }

            string kinds = string.Join(" / ", extensions.Select(x => x.TrimStart('.').ToUpperInvariant()));
            if (valid.Count == 0)
            {
                Main.LogDebugInfo($"{toolName}: No {kinds} files found in the dropped items.");
                TemporaryMessageHelper.ShowTemporaryMessage(dragText, $"No {kinds} files found", 2000);
                return;
            }

            var existingLines = list.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var existing = new HashSet<string>(existingLines, StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (var file in valid)
            {
                if (existing.Add(file))
                {
                    existingLines.Add(file);
                    added++;
                }
            }
            list.Text = string.Join(Environment.NewLine, existingLines);
            list.ScrollToEnd();

            int duplicates = valid.Count - added;
            string message = $"{added} file(s) added" + (duplicates > 0 ? $", {duplicates} duplicates filtered" : "");
            Main.LogDebugInfo($"{toolName}: {message}");
            TemporaryMessageHelper.ShowTemporaryMessage(dragText, message, 2000);
        }

        /// <summary>Open a multi-select file dialog and add the chosen files (with <paramref name="objects"/>, Home object zips too).</summary>
        public static async void BrowseForFiles(string filter, string[] extensions, TextBox list, TextBlock dragText, string toolName, bool objects = false, Func<string, bool> accept = null)
        {
            Main.IsEnabled = false;
            if (objects) filter += ";*.zip|Home object zips|*.zip|All files|*.*";
            var dialog = new VistaOpenFileDialog { Filter = filter, Multiselect = true };
            bool? result = dialog.ShowDialog();
            await Task.Delay(10); // absorb clicks that land after the dialog closes
            Main.IsEnabled = true;

            if (result == true && dialog.FileNames.Length > 0)
            {
                if (objects) AddFilesOrObjects(dialog.FileNames, extensions, list, dragText, toolName, accept);
                else AddFiles(dialog.FileNames, extensions, list, dragText, toolName);
            }
        }

        /// <summary>
        /// As <see cref="AddFiles"/>, but Home objects are understood: a dropped .zip is unpacked under Output\Objects, and
        /// a zip or folder with OBJECT.XML in it is read as object(s). When an object has more than one file this tool can
        /// use, a window asks which to add. A line per object (what it is, which skeleton it uses) goes into the list.
        /// </summary>
        public static async void AddFilesOrObjects(IEnumerable<string> items, string[] extensions, TextBox list, TextBlock dragText, string toolName, Func<string, bool> accept = null)
        {
            var input = items.ToList();
            if (!input.Any(i => Directory.Exists(i) || string.Equals(Path.GetExtension(i), ".zip", StringComparison.OrdinalIgnoreCase)))
            {
                AddFiles(input, extensions, list, dragText, toolName, accept);
                return;
            }

            TemporaryMessageHelper.ShowTemporaryMessage(dragText, "Reading object(s)...", 2000);
            var plain = new List<string>();
            var found = new List<HomeObjectInfo>();
            var notes = new List<string>();
            try
            {
                await Task.Run(() =>
                {
                    foreach (var item in input)
                    {
                        try
                        {
                            string folder = null;
                            bool zip = File.Exists(item) && string.Equals(Path.GetExtension(item), ".zip", StringComparison.OrdinalIgnoreCase);
                            if (zip)
                                folder = HomeObjects.ExtractZip(item, notes);
                            else if (Directory.Exists(item))
                                folder = item;
                            if (folder == null) { plain.Add(item); continue; }
                            var roots = HomeObjects.FindObjectRoots(folder);
                            if (roots.Count == 0)
                            {
                                // a scene zip (no OBJECT.XML): its Home files, picked like an object's; a plain folder: every matching file
                                if (zip) found.Add(HomeObjects.Scan(folder));
                                else plain.Add(folder);
                                continue;
                            }
                            foreach (var r in roots) found.Add(HomeObjects.Scan(r));
                        }
                        catch (Exception ex)
                        {
                            notes.Add($"{Path.GetFileName(item)}: {ex.Message}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                notes.Add(ex.Message);
            }

            List<(HomeObjectInfo Info, List<ObjectAsset> Assets)> usable = null;
            await Task.Run(() => usable = found.Select(o => (Info: o, Assets: o.Assets.Where(a => HasExtension(a.Path, extensions) && Accepts(accept, a.Path)).ToList())).ToList());
            var report = new System.Text.StringBuilder();
            foreach (var n in notes) report.AppendLine("-- " + n);
            foreach (var o in found) report.Append(HomeObjects.Summary(o));
            var chosen = new List<string>();
            var withFiles = usable.Where(u => u.Assets.Count > 0).ToList();
            if (withFiles.Sum(u => u.Assets.Count) > 1)
            {
                var picker = new ObjectPickerWindow(toolName, withFiles) { Owner = Main };
                if (picker.ShowDialog() == true) chosen.AddRange(picker.SelectedPaths);
                else report.AppendLine("-- Object files not added (cancelled)");
            }
            else chosen.AddRange(withFiles.SelectMany(u => u.Assets).Select(a => a.Path));
            if (found.Count > 0 && withFiles.Count == 0)
                report.AppendLine("-- The object(s) have no files this tool can use");
            if (report.Length > 0) AppendReport(list, report.ToString());

            var toAdd = plain.Concat(chosen).ToList();
            if (toAdd.Count > 0) AddFiles(toAdd, extensions, list, dragText, toolName, accept);
            else TemporaryMessageHelper.ShowTemporaryMessage(dragText, found.Count > 0 ? "Nothing added" : "No files found", 2000);
        }

        private static bool Accepts(Func<string, bool> accept, string file)
        {
            if (accept == null) return true;
            try { return accept(file); }
            catch (Exception) { return false; }
        }

        /// <summary>Dialog filter text such as "*.ani;*.glb".</summary>
        public static string ExtensionFilter(string[] extensions) => string.Join(";", extensions.Select(e => "*" + e));

        /// <summary>The file paths in a tool's list (result lines start with "--" and are ignored).</summary>
        public static List<string> ListedFiles(TextBox list, string[] extensions) =>
            list.Text.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !l.StartsWith("--", StringComparison.Ordinal) && HasExtension(l, extensions) && File.Exists(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// Adds a job's report under the list: every line goes to the debug log, and the box keeps only the last
        /// LogBox.MaxLines report lines (listed files are never trimmed).
        /// </summary>
        public static void AppendReport(TextBox list, string report)
        {
            report = (report ?? "").Replace("\r\n", "\n");
            var main = MainWindow.Instance;
            if (main != null)
            {
                string who = "[" + (list.Name ?? "").Replace("TextBox", "") + "] ";
                foreach (var l in report.Split('\n'))
                    if (l.Trim().Length > 0) main.LogDebugInfo(who + l.TrimEnd());
            }
            report = LogBox.TrimText(report.TrimEnd('\n') + "\n", LogBox.MaxLines);
            var text = list.Text;
            if (text.Length > 0 && !text.EndsWith("\n", StringComparison.Ordinal))
                text += Environment.NewLine;
            list.IsUndoEnabled = false;
            list.Text = text + Environment.NewLine + report.Replace("\n", Environment.NewLine);
            TrimReportLines(list);
            list.ScrollToEnd();
        }

        /// <summary>Report lines start with "--" or a space; files are the other lines. Drops the oldest report lines past the limit.</summary>
        private static bool IsReportLine(string l) => l.Length > 0 && (l[0] == '-' || l[0] == ' ' || l[0] == '\t');

        public static void TrimReportLines(TextBox list)
        {
            var lines = list.Text.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.None);
            int report = lines.Count(IsReportLine);
            if (report <= LogBox.MaxLines + 100) return;
            int drop = report - LogBox.MaxLines;
            var kept = new List<string>(lines.Length);
            bool noted = false;
            foreach (var l in lines)
            {
                if (drop > 0 && IsReportLine(l)) { drop--; if (!noted) { kept.Add(LogBox.TrimmedNote); noted = true; } continue; }
                if (l == LogBox.TrimmedNote && noted) continue;
                kept.Add(l);
            }
            list.Text = string.Join(Environment.NewLine, kept);
        }

        /// <summary>
        /// The listed files for a job, with the box cleared ready for the job's report (a list of 100k files is then
        /// worked on from memory instead of being dragged along in the box).
        /// </summary>
        public static List<string> TakeFiles(TextBox list, string[] extensions)
        {
            var files = ListedFiles(list, extensions);
            if (files.Count > 0) list.Clear();
            return files;
        }

        /// <summary>
        /// For a double-click in a list: the file on the clicked line (a listed file, or the output of a
        /// "-- Success: a -> b (...)" line), or null.
        /// </summary>
        public static string FileOnClickedLine(TextBox box)
        {
            int lineIndex = box.GetLineIndexFromCharacterIndex(box.CaretIndex);
            string line = lineIndex >= 0 ? box.GetLineText(lineIndex).Trim() : "";
            int arrow = line.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
            {
                string rest = line.Substring(arrow + 4);
                int paren = rest.LastIndexOf(" (", StringComparison.Ordinal);
                line = (paren > 0 ? rest.Substring(0, paren) : rest).Replace(" (Renamed)", "").Trim();
            }
            return File.Exists(line) ? line : null;
        }

        /// <summary>Show a file selected in Explorer, or open a folder (created if missing).</summary>
        public static void ShowInExplorer(string file, string fallbackFolder, string toolName)
        {
            try
            {
                if (file != null)
                {
                    Process.Start("explorer.exe", $"/select,\"{file}\"");
                }
                else if (fallbackFolder != null)
                {
                    Directory.CreateDirectory(fallbackFolder);
                    Process.Start("explorer.exe", $"\"{fallbackFolder}\"");
                }
            }
            catch (Exception ex)
            {
                Main.LogDebugInfo($"{toolName}: Could not open Explorer: {ex.Message}");
            }
        }
    }
}
