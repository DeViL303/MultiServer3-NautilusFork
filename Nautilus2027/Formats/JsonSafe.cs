#nullable disable
using System.Text.Json.Nodes;

namespace Nautilus2027.Formats
{
    /// <summary>JSON lookups that give null instead of throwing when a hand-edited file has the wrong kind of value.</summary>
    internal static class JsonSafe
    {
        /// <summary>The value under <paramref name="key"/>, or null if this isn't an object (or is null).</summary>
        public static JsonNode At(this JsonNode n, string key) => n is JsonObject o && key != null ? o[key] : null;

        /// <summary>Item <paramref name="i"/>, or null if this isn't an array or is too short.</summary>
        public static JsonNode At(this JsonNode n, int i) => n is JsonArray a && i >= 0 && i < a.Count ? a[i] : null;
    }
}
