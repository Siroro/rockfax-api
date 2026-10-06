using System.Text.Json;
using RockfaxDesk;
using RockfaxDesk.Controls;
using Xunit;

namespace RockfaxApi.Tests;

public class JxTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Int_truncates_fractional_ratings()
    {
        // Regression: top-10 ratings arrive as 4.5 and blew up GetInt32.
        Assert.Equal(4, Parse("{\"stars\":4.5}").Int("stars"));
        Assert.Equal(5, Parse("{\"stars\":5.0}").Int("stars"));
    }

    [Theory]
    [InlineData("{\"n\":7}", 7)]
    [InlineData("{\"n\":\"7\"}", 0)]
    [InlineData("{\"n\":null}", 0)]
    [InlineData("{}", 0)]
    public void Int_returns_zero_for_missing_or_non_numeric(string json, int expected)
        => Assert.Equal(expected, Parse(json).Int("n"));

    [Theory]
    [InlineData("{\"s\":\"hello\"}", "hello")]
    [InlineData("{\"s\":123}", "123")]
    [InlineData("{\"s\":null}", "")]
    [InlineData("{}", "")]
    public void Str_stringifies_or_defaults_to_empty(string json, string expected)
        => Assert.Equal(expected, Parse(json).Str("s"));

    [Fact]
    public void IntList_reads_number_arrays_and_skips_the_rest()
    {
        Assert.Equal(new[] { 1, 2, 3 }, Parse("{\"g\":[1,2,3]}").IntList("g"));
        Assert.Equal(new[] { 4 }, Parse("{\"g\":[4,\"x\",null]}").IntList("g"));
        Assert.Empty(Parse("{\"g\":[]}").IntList("g"));
        Assert.Empty(Parse("{\"g\":null}").IntList("g"));
        Assert.Empty(Parse("{}").IntList("g"));
    }

    [Fact]
    public void EnumerateArrayOrObjectValues_handles_both_containers()
    {
        Assert.Equal(2, Parse("[1,2]").EnumerateArrayOrObjectValues().Count());
        Assert.Equal(2, Parse("{\"a\":1,\"b\":2}").EnumerateArrayOrObjectValues().Count());
        Assert.Empty(Parse("null").EnumerateArrayOrObjectValues());
        Assert.Empty(Parse("3").EnumerateArrayOrObjectValues());
    }

    [Theory]
    [InlineData(10, "Soloed")]
    [InlineData(20, "Lead")]
    [InlineData(21, "Lead, onsight")]
    [InlineData(25, "Lead, head/redpoint")]
    [InlineData(30, "Followed")]
    [InlineData(40, "Toproped")]
    [InlineData(47, "Toproped, dogged")]
    [InlineData(99, "style 99")]
    public void StyleName_decodes_app_style_ids(int id, string expected)
        => Assert.Equal(expected, Jx.StyleName(id));

    [Theory]
    [InlineData(3, "***")]
    [InlineData(2, "**")]
    [InlineData(1, "*")]
    [InlineData(0, "")]
    public void Stars_maps_1_to_3(int n, string expected) => Assert.Equal(expected, Jx.Stars(n));

    [Fact]
    public void DateFromUnix_formats_as_iso_date_and_guards_zero()
    {
        long secs = new DateTimeOffset(2025, 9, 22, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        string expected = DateTimeOffset.FromUnixTimeSeconds(secs).LocalDateTime.ToString("yyyy-MM-dd");
        Assert.Equal(expected, Jx.DateFromUnix(secs));
        Assert.Equal("", Jx.DateFromUnix(0));
    }

    [Fact]
    public void RouteSummary_maps_a_search_hit()
    {
        var summary = RouteSummary.FromSearch(Parse(
            "{\"name\":\"Curbar Edge\",\"grade\":\"HVD\",\"techGrade\":\"\",\"stars\":4.5," +
            "\"ukcCragName\":\"Curbar\",\"ukcID\":123,\"rockfaxID\":45,\"cragID\":9}"));
        Assert.Equal("Curbar Edge", summary.Name);
        Assert.Equal("HVD", summary.Grade);
        Assert.Equal(4, summary.Stars);
        Assert.Equal("Curbar", summary.CragName);
        Assert.Equal(123, summary.UkcId);
        Assert.Equal(45, summary.RockfaxId);
        Assert.Equal(9, summary.CragUkcId);
    }
}

public class GradeBandTests
{
    [Theory]
    [InlineData("M", 0)]
    [InlineData("D", 0)]
    [InlineData("VD", 0)]
    [InlineData("V Diff", 0)]
    [InlineData("HVD", 0)]
    [InlineData("S", 1)]
    [InlineData("HS", 1)]
    [InlineData("VS", 2)]
    [InlineData("HVS", 2)]
    [InlineData("E1", 3)]
    [InlineData("E10 7a", 3)]
    [InlineData("7a", -1)]
    [InlineData("f6A", -1)]
    [InlineData("", -1)]
    public void Grades_map_to_filter_bands(string grade, int expected)
        => Assert.Equal(expected, CragView.GradeBand(grade));
}

public class CsvFieldTests
{
    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("has,comma", "\"has,comma\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("", "\"\"")]
    public void Fields_are_always_quoted_with_doubled_quotes(string value, string expected)
        => Assert.Equal(expected, LogbookView.CsvField(value));
}

public class LogbookStatsTests
{
    [Theory]
    [InlineData("M", 0)]
    [InlineData("D", 1)]
    [InlineData("VD", 2)]
    [InlineData("S", 3)]
    [InlineData("HS", 4)]
    [InlineData("VS 4c", 5)]
    [InlineData("HVS", 6)]
    [InlineData("E1", 8)]
    [InlineData("E2 5b", 9)]
    [InlineData("7a", 27)]
    [InlineData("f6A", 26)]
    [InlineData("", -1)]
    [InlineData("unknown", -1)]
    public void Grades_rank_in_climbing_order(string grade, int expected)
        => Assert.Equal(expected, LogbookView.GradeRank(grade));

    [Fact]
    public void Stats_aggregate_totals_year_crags_and_hardest()
    {
        var ascents = new[]
        {
            (new DateTime(2024, 5, 1), "VS 4c", "Stanage"),
            (new DateTime(2026, 3, 2), "HVS 5a", "Stanage"),
            (new DateTime(2026, 9, 9), "E1 5b", "Curbar"),
            (new DateTime(2026, 9, 10), "7a", "Malham"),
            (new DateTime(2026, 9, 11), "", ""),
        };
        (int total, int thisYear, int crags, string top) = LogbookView.Stats(ascents);
        Assert.Equal(5, total);
        Assert.Equal(4, thisYear);
        Assert.Equal(3, crags);
        Assert.Equal("7a", top); // sport 27 outranks E1's 8
    }

    [Fact]
    public void Stats_on_an_empty_logbook_is_all_zeros()
    {
        (int total, int thisYear, int crags, string top) = LogbookView.Stats(Array.Empty<(DateTime, string, string)>());
        Assert.Equal((0, 0, 0, ""), (total, thisYear, crags, top));
    }
}
