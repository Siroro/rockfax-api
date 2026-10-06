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
