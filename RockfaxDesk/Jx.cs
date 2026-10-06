using System.Text.Json;

namespace RockfaxDesk;

internal static class Jx
{
    /// <summary>Opens a URL in the user's default browser.</summary>
    internal static void OpenBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // no default browser configured \u2014 ignore
        }
    }

    internal static string Str(this JsonElement e, string prop)
        => e.TryGetProperty(prop, out JsonElement v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";

    internal static int Int(this JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out JsonElement v) || v.ValueKind != JsonValueKind.Number) return 0;
        return v.TryGetInt32(out int i) ? i : (int)v.GetDouble(); // ratings arrive fractional
    }

    internal static double Dbl(this JsonElement e, string prop)
        => e.TryGetProperty(prop, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    internal static long Long(this JsonElement e, string prop)
        => e.TryGetProperty(prop, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    /// <summary>Reads a JSON array of numbers into a list of ints (missing/empty = empty list).</summary>
    internal static List<int> IntList(this JsonElement e, string prop)
    {
        var result = new List<int>();
        if (e.TryGetProperty(prop, out JsonElement v) && v.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in v.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int i)) result.Add(i);
        }
        return result;
    }

    /// <summary>Enumerates an array's elements, or an object's property values — whichever this element holds.</summary>
    internal static IEnumerable<JsonElement> EnumerateArrayOrObjectValues(this JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (JsonElement item in e.EnumerateArray()) yield return item;
                break;
            case JsonValueKind.Object:
                foreach (JsonProperty prop in e.EnumerateObject()) yield return prop.Value;
                break;
        }
    }

    /// <summary>UKC ascent style ids: decades are Soloed / Lead / Followed / Toproped, units are
    /// onsight/with beta/repeat/ground up/redpoint/dogged/unfinished (from RFAscentUtils).</summary>
    internal static string StyleName(int id) => (id / 10) switch
    {
        1 => Solo("Soloed", id),
        2 => Solo("Lead", id),
        3 => Solo("Followed", id),
        4 => Solo("Toproped", id),
        _ => $"style {id}",
    };

    private static string Solo(string @base, int id) => (id % 10) switch
    {
        0 => @base,
        1 => $"{@base}, onsight",
        2 => $"{@base}, with beta",
        3 => $"{@base}, repeat",
        4 => $"{@base}, ground up",
        5 => $"{@base}, head/redpoint",
        7 => $"{@base}, dogged",
        9 => $"{@base}, unfinished",
        _ => $"{@base} ({id})",
    };

    internal static string Stars(int n) => n switch { 3 => "***", 2 => "**", 1 => "*", _ => "" };

    internal static string DateFromUnix(long seconds)
        => seconds <= 0 ? "" : DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime.ToString("yyyy-MM-dd");

    /// <summary>16-point compass label for a wind bearing in degrees.</summary>
    internal static string Compass(int degrees)
    {
        string[] dirs = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
        int d = ((degrees % 360) + 360) % 360;
        return dirs[(int)Math.Round(d / 22.5) % 16];
    }

    /// <summary>Strips tags and decodes the entities the API's HTML snippets use
    /// (crag features/access, listing phone/web links), collapsing blank runs.</summary>
    internal static string StripHtml(string html)
    {
        if (html.Length == 0) return "";
        var sb = new System.Text.StringBuilder(html.Length);
        bool inTag = false;
        foreach (char c in html)
        {
            if (c == '<') { inTag = true; sb.Append(' '); }
            else if (c == '>') inTag = false;
            else if (!inTag) sb.Append(c);
        }
        string s = sb.ToString()
            .Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&lsquo;", "\u2018").Replace("&rsquo;", "\u2019")
            .Replace("&ldquo;", "\u201c").Replace("&rdquo;", "\u201d")
            .Replace("&mdash;", "\u2014").Replace("&ndash;", "\u2013").Replace("&hellip;", "\u2026")
            .Replace("&apos;", "'").Replace("&quot;", "\"");
        s = System.Text.RegularExpressions.Regex.Replace(s, "&#(\\d+);",
            m => int.TryParse(m.Groups[1].Value, out int code) ? char.ConvertFromUtf32(code) : m.Value);
        var lines = s.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>();
        bool pendingBlank = false;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) { pendingBlank = kept.Count > 0; continue; }
            if (pendingBlank) kept.Add("");
            kept.Add(line);
            pendingBlank = false;
        }
        return string.Join("\r\n", kept);
    }
}

/// <summary>A route as shown in lists — carries everything RouteView needs.</summary>
internal sealed record RouteSummary(string Name, string Grade, string TechGrade, int Stars, string CragName,
                                     int UkcId, int RockfaxId, int CragUkcId)
{
    public static RouteSummary FromSearch(JsonElement r)
        => new(r.Str("name"), r.Str("grade"), r.Str("techGrade"), r.Int("stars"), r.Str("ukcCragName"),
               r.Int("ukcID"), r.Int("rockfaxID"), r.Int("cragID"));
}
