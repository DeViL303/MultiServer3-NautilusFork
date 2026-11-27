#nullable disable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Ookii.Dialogs.Wpf;

namespace Nautilus2027
{
    // "EBOOT" tab: information only. Identifies a decrypted Home EBOOT (ELF) by the SHA1 of its first 500 KB against
    // dependencies\ebootdefs.json and reads the Title ID, App ID, MUIS version, Service ID, NP Comm ID, TSS URL and offline
    // name from it. Unknown builds are searched for Title IDs and a TSS URL. Encrypted EBOOT.BIN / SELF files are recognised
    // (their plain header is shown) but not decrypted. Nothing is ever written.
    public partial class EbootTab : UserControl
    {
        private static MainWindow Main => MainWindow.Instance;

        private const int SignatureBytes = 500 * 1024;
        private Dictionary<string, EbootInfo> _defs;

        public EbootTab()
        {
            InitializeComponent();
        }

        // ------------------------------------------------------------------ input

        private async void EbootDragDropHandler(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var items = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (items == null || items.Length == 0) return;
            if (!File.Exists(items[0]))
            {
                EbootDragAreaText.Text = "Drop a single EBOOT file (folders aren't supported).";
                return;
            }
            if (items.Length > 1) Main.LogDebugInfo($"EBOOT Info: {items.Length} files dropped, showing the first");
            await Identify(items[0]);
        }

        private async void ClickToBrowseHandlerEboot(object sender, RoutedEventArgs e)
        {
            var dlg = new VistaOpenFileDialog
            {
                Filter = "EBOOT / ELF files (*.elf;*.bin;*.self)|*.elf;*.bin;*.self|All files (*.*)|*.*",
                Multiselect = false
            };
            Main.IsEnabled = false;
            bool? ok = dlg.ShowDialog();
            await Task.Delay(10);   // absorb a stray click after the dialog closes
            Main.IsEnabled = true;
            if (ok == true && !string.IsNullOrEmpty(dlg.FileName)) await Identify(dlg.FileName);
        }

        private void ClearEbootButton_Click(object sender, RoutedEventArgs e)
        {
            ClearFields();
            EbootDragAreaText.Text = "Drag a decrypted EBOOT (ELF) here / Click to browse";
        }

        private void ClearFields()
        {
            foreach (var tb in new[] { LoadedEbootBuild, LoadedEbootTitleID, LoadedEbootAppID, LoadedEbootMuisVersion, LoadedEbootServiceID,
                                       LoadedEbootNPCommID, LoadedEbootTSSURL, LoadedEbootOfflineName, LoadedEbootElf, LoadedEbootSignature, LoadedEbootFilePath })
                tb.Text = "";
            EbootNotes.Text = "";
        }

        // ------------------------------------------------------------------ identify

        private sealed class Report
        {
            public string Headline, Build, TitleId, AppId, Muis, ServiceId, NpCommId, TssUrl, OfflineName, Elf, Signature;
            public List<string> Notes = new List<string>();
        }

        private async Task Identify(string path)
        {
            ClearFields();
            LoadedEbootFilePath.Text = path;
            EbootDragAreaText.Text = "Reading " + Path.GetFileName(path) + "...";
            Main.LogDebugInfo("EBOOT Info: reading " + path);
            Report r;
            try
            {
                r = await Task.Run(() => { try { return Read(path); } catch (Exception ex) { return new Report { Headline = "Couldn't read the file: " + ex.Message }; } });
            }
            catch (Exception ex)
            {
                r = new Report { Headline = "Couldn't read the file: " + ex.Message };
            }
            EbootDragAreaText.Text = r.Headline;
            LoadedEbootBuild.Text = r.Build ?? "";
            LoadedEbootTitleID.Text = r.TitleId ?? "";
            LoadedEbootAppID.Text = r.AppId ?? "";
            LoadedEbootMuisVersion.Text = r.Muis ?? "";
            LoadedEbootServiceID.Text = r.ServiceId ?? "";
            LoadedEbootNPCommID.Text = r.NpCommId ?? "";
            LoadedEbootTSSURL.Text = r.TssUrl ?? "";
            LoadedEbootOfflineName.Text = r.OfflineName ?? "";
            LoadedEbootElf.Text = r.Elf ?? "";
            LoadedEbootSignature.Text = r.Signature ?? "";
            EbootNotes.Text = string.Join("\n", r.Notes);
            Main.LogDebugInfo("EBOOT Info: " + r.Headline + (r.Notes.Count > 0 ? " | " + string.Join(" | ", r.Notes) : ""));
        }

        private Report Read(string path)
        {
            byte[] d = File.ReadAllBytes(path);
            var r = new Report();
            if (d.Length >= 4 && d[0] == 'S' && d[1] == 'C' && d[2] == 'E' && d[3] == 0)
                return ReadSelf(d, r);
            if (d.Length < 0x40 || d[0] != 0x7F || d[1] != 'E' || d[2] != 'L' || d[3] != 'F')
            {
                r.Headline = "Not an ELF or SELF file (unknown header).";
                r.Notes.Add("This tab identifies decrypted Home EBOOTs (ELF). The file starts with " + BitConverter.ToString(d, 0, Math.Min(8, d.Length)) + ".");
                return r;
            }

            r.Elf = DescribeElf(d);
            using (var sha = SHA1.Create())
                r.Signature = Convert.ToHexString(sha.ComputeHash(d, 0, Math.Min(SignatureBytes, d.Length)));

            var defs = LoadDefinitions(r.Notes);
            if (defs != null && defs.TryGetValue(r.Signature, out var info) && info?.Offsets != null)
            {
                string Str(string key, int fixedLen = 0)
                {
                    if (!info.Offsets.TryGetValue(key, out var hex) || string.IsNullOrWhiteSpace(hex)) return null;
                    long off;
                    try { off = Convert.ToInt64(hex, 16); } catch { return null; }
                    if (off <= 0 || off >= d.Length) return null;
                    int len = 0;
                    if (fixedLen > 0) len = (int)Math.Min(fixedLen, d.Length - off);
                    else while (off + len < d.Length && d[off + len] != 0 && len < 256) len++;
                    return Encoding.UTF8.GetString(d, (int)off, len);
                }
                r.TitleId = Str("TitleIdOffset") ?? "Title ID Not Available";
                r.AppId = Str("AppIdOffset", 5) ?? "App ID Not Found";
                r.ServiceId = Str("ServiceIdOffset") ?? "Service ID Not Available";
                r.NpCommId = Str("NPCommIDOffset") ?? "NP Comm ID Not Available";
                r.TssUrl = Str("TssUrlOffset") ?? "TSS URL Not Available";
                r.OfflineName = Str("OfflineNameOffset") ?? "N/A (Online Only)";
                string m1 = Str("MuisVersionOffset1") ?? "", m2 = Str("MuisVersionOffset2") ?? "";
                r.Muis = m2 != "" ? $"{m1}.{m2}" : m1;
                string version = (info.Version ?? "").TrimEnd('.');
                r.Build = $"{info.Type} {version}".Trim();
                bool spoofed = r.Muis != "" && info.Version != r.Muis && version != r.Muis;
                r.Headline = $"EBOOT Identified: {r.Build}" + (spoofed ? $" (MUIS spoofed to {r.Muis})" : "");
                var titleIds = info.Offsets.Keys.Where(k => k.StartsWith("TitleIdOffset", StringComparison.Ordinal) && k != "TitleIdOffset")
                    .OrderBy(k => k, StringComparer.Ordinal).Select(k => Str(k)).Where(s => !string.IsNullOrEmpty(s) && s != r.TitleId).Distinct().ToList();
                if (titleIds.Count > 0) r.Notes.Add("Other Title IDs in this EBOOT: " + string.Join(", ", titleIds));
            }
            else
            {
                r.Build = "Unknown build (not in ebootdefs.json)";
                r.Headline = "ELF loaded, but this EBOOT build isn't known.";
                // search the strings instead
                string text = Encoding.Latin1.GetString(d);
                var ids = Regex.Matches(text, @"(?<![A-Z0-9])(?:NP|BC|BL)[A-Z]{2}\d{5}(?![0-9])").Select(m => m.Value).Distinct().Take(8).ToList();
                if (ids.Count > 0) r.TitleId = string.Join(", ", ids) + "  (found by search)";
                var tss = Regex.Match(text, @"https?://[\x21-\x7e]{1,120}?/tss/[\x21-\x7e]{1,80}?\.xml");
                if (tss.Success) r.TssUrl = tss.Value + "  (found by search)";
                var comm = Regex.Matches(text, @"(?<![A-Z0-9])NPWR\d{5}_\d{2}(?![0-9])").Select(m => m.Value).Distinct().Take(4).ToList();
                if (comm.Count > 0) r.NpCommId = string.Join(", ", comm) + "  (found by search)";
                r.Notes.Add("The signature above isn't in dependencies\\ebootdefs.json, so the fields were found by searching the file's text and may be incomplete.");
            }
            return r;
        }

        private static string DescribeElf(byte[] d)
        {
            bool is64 = d[4] == 2, big = d[5] == 2;
            ushort U16(int o) => big ? BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(o)) : BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o));
            ulong U64(int o) => big ? BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(o)) : BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(o));
            uint U32(int o) => big ? BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o)) : BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o));
            ushort type = U16(0x10), machine = U16(0x12);
            string typeName = type switch { 2 => "executable", 3 => "shared", 0xFFA4 => "PRX", 0xFE04 => "PRX", _ => "type 0x" + type.ToString("X") };
            string machineName = machine switch { 0x15 => "PowerPC 64 (PS3 PPU)", 0x17 => "SPU", 0x14 => "PowerPC", _ => "machine 0x" + machine.ToString("X") };
            string entry = is64 ? "0x" + U64(0x18).ToString("X") : "0x" + U32(0x18).ToString("X");
            int phnum = is64 ? U16(0x38) : U16(0x2C), shnum = is64 ? U16(0x3C) : U16(0x30);
            return $"{(is64 ? "64" : "32")}-bit {(big ? "big" : "little")}-endian {typeName}, {machineName}, entry {entry}, {phnum} program / {shnum} section headers, {d.Length / 1024.0 / 1024.0:0.00} MB";
        }

        /// <summary>Encrypted EBOOT.BIN / SELF: show the plain SCE header; it can't be identified without decrypting it.</summary>
        private static Report ReadSelf(byte[] d, Report r)
        {
            r.Headline = "Encrypted EBOOT (SELF) - decrypt it to an ELF first.";
            r.Build = "Encrypted SELF";
            try
            {
                uint version = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(4));
                ushort keyRev = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(8)), headerType = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(10));
                string info = $"SCE header v{version}, key revision 0x{keyRev:X4}, {(headerType == 1 ? "SELF" : headerType == 3 ? "PKG" : "header type " + headerType)}";
                if (headerType == 1 && d.Length >= 0x40)
                {
                    long appInfo = (long)BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(0x28));
                    if (appInfo > 0 && appInfo + 0x18 <= d.Length)
                    {
                        uint selfType = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan((int)appInfo + 0x0C));
                        ulong appVer = BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan((int)appInfo + 0x10));
                        string typeName = selfType switch { 1 => "LV0", 2 => "LV1", 3 => "LV2", 4 => "Application", 5 => "Isolated SPU", 6 => "Secure loader", 8 => "NPDRM application", _ => "type " + selfType };
                        info += $", {typeName}, app version {(appVer >> 48) & 0xFFFF:X2}.{(appVer >> 32) & 0xFFFF:X2}";
                    }
                }
                r.Elf = info;
            }
            catch (Exception ex)
            {
                r.Elf = "SCE header could not be read: " + ex.Message;
            }
            r.Notes.Add("This tab only reads decrypted EBOOTs. Decrypt it first (for example in RPCS3: Utilities > Decrypt PS3 Binaries) and drop the .elf here.");
            return r;
        }

        private Dictionary<string, EbootInfo> LoadDefinitions(List<string> notes)
        {
            if (_defs != null) return _defs;
            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "ebootdefs.json");
            try
            {
                var map = JsonConvert.DeserializeObject<Dictionary<string, EbootInfo>>(File.ReadAllText(file));
                if (map == null || map.Count == 0) { notes.Add("dependencies\\ebootdefs.json is empty."); return null; }
                _defs = new Dictionary<string, EbootInfo>(map, StringComparer.OrdinalIgnoreCase);
                return _defs;
            }
            catch (Exception ex)
            {
                notes.Add("Couldn't load dependencies\\ebootdefs.json: " + ex.Message);
                Main.LogDebugInfo("EBOOT Info: couldn't load ebootdefs.json: " + ex);
                return null;
            }
        }
    }
}
