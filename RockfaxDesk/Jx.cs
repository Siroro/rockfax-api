using System.Text.Json;

namespace RockfaxDesk;

internal static class Jx
{
    internal static string Str(this JsonElement e, string prop)
        => e.TryGetProperty(prop, out JsonElement v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";

    internal static int Int(this JsonElement e, string prop)
        => e.TryGetProperty(prop, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    internal static double Dbl(this JsonElement e, string prop)
        => e.TryGetProperty(prop, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    internal static long Long(this JsonElement e, string prop)
        => e.TryGetProperty(prop, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

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
}

/// <summary>A route as shown in lists — carries everything RouteView needs.</summary>
internal sealed record RouteSummary(string Name, string Grade, string TechGrade, int Stars, string CragName,
                                     int UkcId, int RockfaxId, int CragUkcId)
{
    public static RouteSummary FromSearch(JsonElement r)
        => new(r.Str("name"), r.Str("grade"), r.Str("techGrade"), r.Int("stars"), r.Str("ukcCragName"),
               r.Int("ukcID"), r.Int("rockfaxID"), r.Int("cragID"));
}
