#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Nautilus2027.Formats
{
    /// <summary>
    /// XML loading that never throws for a damaged file: DTDs and external entities are ignored (no billion-laughs, no file
    /// access), very large files are refused, and a file that won't parse is repaired (bad characters removed, stray '&amp;'
    /// escaped, tags closed / unmatched closing tags dropped, text after the root cut) and parsed again. What was done goes into
    /// the notes; null comes back only when nothing usable is left.
    /// </summary>
    public static class SafeXml
    {
        public const long MaxBytes = 64L * 1024 * 1024;

        private static XmlReaderSettings Settings() => new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersInDocument = MaxBytes,
            MaxCharactersFromEntities = 1024 * 1024,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CheckCharacters = false,
        };

        /// <summary>Text of a file, whatever its encoding (UTF-8 / UTF-16 by BOM, else UTF-8, falling back to Latin-1).</summary>
        public static string ReadText(byte[] b)
        {
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            int start = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
            try { return new UTF8Encoding(false, true).GetString(b, start, b.Length - start); }
            catch (DecoderFallbackException) { return Encoding.Latin1.GetString(b, start, b.Length - start); }
        }

        public static XDocument Load(string path, List<string> notes = null)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            string name = Path.GetFileName(path);
            try
            {
                long len = new FileInfo(path).Length;
                if (len == 0) { notes?.Add(name + " is empty"); return null; }
                if (len > MaxBytes) { notes?.Add($"{name} is too large to read ({len:N0} bytes)"); return null; }
                return Parse(ReadText(File.ReadAllBytes(path)), name, notes);
            }
            catch (Exception ex)
            {
                notes?.Add($"{name} could not be read: {ex.Message}");
                return null;
            }
        }

        public static XDocument Parse(string text, string name, List<string> notes)
        {
            try
            {
                using (var r = XmlReader.Create(new StringReader(text), Settings()))
                    return XDocument.Load(r);
            }
            catch (Exception first)
            {
                string fixedText = Repair(text, out var did);
                try
                {
                    using (var r = XmlReader.Create(new StringReader(fixedText), Settings()))
                    {
                        var doc = XDocument.Load(r);
                        notes?.Add($"{name} is damaged ({Short(first.Message)}); repaired: {string.Join(", ", did.DefaultIfEmpty("cleaned up"))}");
                        return doc;
                    }
                }
                catch (Exception second)
                {
                    notes?.Add($"{name} is damaged and couldn't be repaired ({Short(second.Message)}); its contents were skipped");
                    return null;
                }
            }
        }

        private static string Short(string m)
        {
            m = (m ?? "").Replace("\r", " ").Replace("\n", " ");
            return m.Length > 120 ? m.Substring(0, 120) + "..." : m;
        }

        private static readonly Regex BadChars = new Regex(@"[\x00-\x08\x0B\x0C\x0E-\x1F￾￿]", RegexOptions.Compiled);
        private static readonly Regex StrayAmp = new Regex(@"&(?!(?:amp|lt|gt|quot|apos|#\d{1,7}|#x[0-9A-Fa-f]{1,6});)", RegexOptions.Compiled);
        private static readonly Regex AttrRx = new Regex(@"([A-Za-z_][\w:.\-]*)\s*=\s*(""[^""<]*""|'[^'<]*')", RegexOptions.Compiled);
        private static readonly Regex Tag = new Regex(@"<(/?)([A-Za-z_][\w:.\-]*)((?:[^<>""']|""[^""]*""|'[^']*')*?)(/?)>|<!--.*?-->|<\?.*?\?>|<!\[CDATA\[.*?\]\]>|<!DOCTYPE[^>]*>", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>Best-effort fixes for a damaged XML text.</summary>
        public static string Repair(string text, out List<string> did)
        {
            did = new List<string>();
            string t = text ?? "";
            if (t.Length > 0 && t[0] == '﻿') t = t.Substring(1);
            if (BadChars.IsMatch(t)) { t = BadChars.Replace(t, ""); did.Add("removed invalid characters"); }
            if (StrayAmp.IsMatch(t)) { t = StrayAmp.Replace(t, "&amp;"); did.Add("escaped stray '&'"); }

            // a partial tag at the end of a cut-off file
            int lastLt = t.LastIndexOf('<'), lastGt = t.LastIndexOf('>');
            if (lastLt > lastGt) { t = t.Substring(0, lastLt); did.Add("removed a cut-off tag at the end"); }

            var sb = new StringBuilder(t.Length + 256);
            var stack = new List<string>();
            int pos = 0, dropped = 0;
            bool rootClosed = false;
            string Txt(string x) => x.Replace("<", "&lt;").Replace(">", "&gt;");
            foreach (Match m in Tag.Matches(t))
            {
                if (rootClosed) break;
                sb.Append(Txt(t.Substring(pos, m.Index - pos)));
                pos = m.Index + m.Length;
                if (!m.Groups[2].Success)
                {
                    if (!m.Value.StartsWith("<?", StringComparison.Ordinal)) sb.Append(m.Value);   // declarations / PIs aren't needed
                    continue;
                }
                string tagName = m.Groups[2].Value;
                bool closing = m.Groups[1].Value == "/", selfClosing = m.Groups[4].Value == "/";
                if (closing)
                {
                    int at = stack.LastIndexOf(tagName);
                    if (at < 0) { dropped++; continue; }   // nothing to close: drop it
                    for (int k = stack.Count - 1; k > at; k--) sb.Append("</").Append(stack[k]).Append('>');
                    stack.RemoveRange(at, stack.Count - at);
                    sb.Append("</").Append(tagName).Append('>');
                    if (stack.Count == 0) rootClosed = true;
                }
                else
                {
                    // rebuilt from its well-formed attributes (junk inside a tag is dropped)
                    sb.Append('<').Append(tagName);
                    var seen = new HashSet<string>();
                    foreach (Match a in AttrRx.Matches(m.Groups[3].Value))
                        if (seen.Add(a.Groups[1].Value)) sb.Append(' ').Append(a.Groups[1].Value).Append('=').Append(a.Groups[2].Value);
                    sb.Append(selfClosing ? "/>" : ">");
                    if (!selfClosing) stack.Add(tagName);
                }
            }
            if (!rootClosed) sb.Append(Txt(t.Substring(pos)));
            else if (pos < t.Length && t.Substring(pos).Trim().Length > 0) did.Add("cut text after the end of the document");
            if (dropped > 0) did.Add($"dropped {dropped} unmatched closing tag(s)");
            if (stack.Count > 0)
            {
                for (int k = stack.Count - 1; k >= 0; k--) sb.Append("</").Append(stack[k]).Append('>');
                did.Add($"closed {stack.Count} unclosed tag(s) (the file looks cut off)");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ small helpers for namespaced Home XML

        public static IEnumerable<XElement> Named(XContainer x, string local) => x?.Descendants().Where(e => e.Name.LocalName == local) ?? Enumerable.Empty<XElement>();
        public static XElement Child(XElement x, string local) => x?.Elements().FirstOrDefault(e => e.Name.LocalName == local);
        public static string ChildText(XElement x, string local) => Child(x, local)?.Value?.Trim() ?? "";
        public static string Attr(XElement x, string local) => x?.Attributes().FirstOrDefault(a => a.Name.LocalName == local)?.Value;
    }
}
