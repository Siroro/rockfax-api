using System.Text.Json;
using RockfaxDesk;
using RockfaxDesk.Controls;
using Xunit;

namespace RockfaxApi.Tests;

/// <summary>Parsers behind the data-completeness work: crag details, weather extras,
/// logbook route details and the listings directory.</summary>
public class DataCompletenessTests
{
    // ---- crag details -------------------------------------------------------

    [Fact]
    public void ParseCragInfo_reads_the_full_crag_details_shape()
    {
        const string json = """
            [{
              "id": 104, "name": "Stanage Popular", "lng": -1.62506, "lat": 53.34348,
              "editDate": 1787215138, "rocktype": "Gritstone",
              "features": "<p>Popular&hellip; edge<\/p>\r\n\r\n<p>Second para&nbsp;here<\/p>",
              "access": "<p>Park at &lsquo;Popular&rsquo; end<\/p>",
              "bmc": 150, "nroutes": 784, "gradetypes": 33300, "thumbID": 414170,
              "rockfaxID": 1263, "weatherTown": 1,
              "guidebooks": [
                {"id": 2656, "name": "Peak Bouldering", "year": 2023, "inprint": 1},
                {"id": 375, "name": "Peak District : Climbing", "year": 2008, "inprint": 0}
              ],
              "comments": [{"id": 6872, "userID": 194101, "name": "myrddinmuse", "comment": "S'alright.", "date": 1480529089}],
              "parking": [{"id": 4, "lng": -1.6341, "lat": 53.3428, "name": "Stanage Popular", "pay": 0},
                          {"id": 3, "lng": -1.6447, "lat": 53.3507, "name": "Stanage Plantation", "pay": 1}],
              "buttressdata": [{"rockfaxID": 4455, "lng": -1.62469, "lat": 53.34333, "name": "Apparent North Buttress"}]
            }]
            """;
        using JsonDocument doc = JsonDocument.Parse(json);
        CragInfo? info = CragView.ParseCragInfo(doc);
        Assert.NotNull(info);
        Assert.Equal("Stanage Popular", info!.Name);
        Assert.Equal("Gritstone", info.RockType);
        Assert.Equal(784, info.NRoutes);
        Assert.Equal(1787215138, info.EditDate);
        Assert.Equal(150, info.BmcId);
        Assert.Equal(2, info.Guidebooks.Count);
        Assert.True(info.Guidebooks[0].InPrint);
        Assert.False(info.Guidebooks[1].InPrint);
        Assert.Equal(2023, info.Guidebooks[0].Year);
        var comment = Assert.Single(info.Comments);
        Assert.Equal("myrddinmuse", comment.Who);
        Assert.Equal("S'alright.", comment.Text);
        Assert.Matches(@"^2016-1[12]-\d{2}$", comment.Date); // unix 1480529089; the day depends on local time
        Assert.Equal(2, info.Parking.Count);
        Assert.Equal("Stanage Plantation", info.Parking[1].Name);
        Assert.True(info.Parking[1].Pay);
        Assert.False(info.Parking[0].Pay);
        Assert.Equal("Apparent North Buttress", Assert.Single(info.Buttresses));
    }

    [Fact]
    public void ParseCragInfo_returns_null_without_a_named_object()
    {
        using JsonDocument doc = JsonDocument.Parse("""[{"error": "no such crag"}]""");
        Assert.Null(CragView.ParseCragInfo(doc));
    }

    // ---- weather extras -------------------------------------------------------

    [Fact]
    public void FormatWeather_reads_sun_times_wind_bearing_and_hourly()
    {
        const string json = """
            {"119": {"crags": [104], "Sa26-10-03": {
              "ast": {"sr": "7:11", "ss": "18:38"},
              "dly": {"wc": 113, "t": 16, "wd": 278, "ws": 25, "cor": 17, "p": 0},
              "hry": {"6:00": {"ut": 1, "wc": 116, "t": 13, "wd": 212, "ws": 20, "cor": 13},
                      "9:30": {"ut": 2, "wc": 116, "t": 15, "wd": 212, "ws": 20, "cor": 10},
                      "8:00": {"ut": 3, "wc": 116, "t": 14, "wd": 200, "ws": 18, "cor": 11}}
            }}}
            """;
        using JsonDocument doc = JsonDocument.Parse(json);
        var chips = CragView.FormatWeather(doc);
        var chip = Assert.Single(chips);
        Assert.Equal(16, chip.Temp);
        Assert.Equal(278, chip.WindDeg);
        Assert.Equal("7:11", chip.Sunrise);
        Assert.Equal("18:38", chip.Sunset);
        Assert.NotNull(chip.Hourly);
        Assert.Equal(2, chip.Hourly!.Split("\r\n").Length); // only whole even hours 6..20 — 9:30 skipped
        Assert.Contains("wind 20 SSW", chip.Hourly);         // 212 degrees -> SSW
    }

    [Theory]
    [InlineData(0, "N")]
    [InlineData(45, "NE")]
    [InlineData(278, "W")]
    [InlineData(360, "N")]
    [InlineData(-90, "W")]
    [InlineData(112, "ESE")]
    public void Compass_maps_bearings(int degrees, string label)
        => Assert.Equal(label, Jx.Compass(degrees));

    // ---- logbook route details -------------------------------------------------

    [Fact]
    public void ParseRouteDetails_handles_array_and_keyed_shapes()
    {
        const string json = """
            {"routes": [
               {"ukcID": 52150, "cragID": 104, "crag": "Stanage Popular"},
               {"ukcID": 0, "cragID": 87, "crag": "Bamford Edge"}
             ],
             "extra": {"12345": {"rockfaxID": 9, "cragID": 31, "crag": "Curbar"}}}
            """;
        using JsonDocument doc = JsonDocument.Parse(json);
        Dictionary<int, (int CragId, string CragName)> map = LogbookView.ParseRouteDetails(doc);
        Assert.Equal((104, "Stanage Popular"), map[52150]);
        Assert.Equal((31, "Curbar"), map[12345]); // keyed by property name, no ukcID inside
        Assert.DoesNotContain(0, map.Keys);        // id-less entries are dropped
    }

    // ---- listings directory ------------------------------------------------------

    [Fact]
    public void ParseListings_prefers_raw_fields_and_infers_type_from_the_key()
    {
        const string json = """
            {"items": {
              "shop_2": {"name": "Rock + Run", "type": "shop", "address": "Sandside",
                         "url": "<a href='https://x'>rockrun.com</a>", "url_raw": "www.rockrun.com",
                         "tel": "<a href='tel:+44'>015395</a>", "tel_raw": "015395 64540",
                         "email": "info@rockrun.com", "notes": "Gear.", "times": "9-5", "cost": ""},
              "wall_10001": {"name": "The Depot", "address": "Sheffield"}
            }}
            """;
        using JsonDocument doc = JsonDocument.Parse(json);
        List<ServiceItem> items = ServicesView.ParseListings(doc);
        Assert.Equal(2, items.Count);
        ServiceItem shop = items.First(i => i.Name == "Rock + Run");
        Assert.Equal("shop", shop.Type);
        Assert.Equal("www.rockrun.com", shop.Web);   // raw twin wins over the HTML link
        Assert.Equal("015395 64540", shop.Tel);
        Assert.Equal("info@rockrun.com", shop.Email);
        ServiceItem wall = items.First(i => i.Name == "The Depot");
        Assert.Equal("wall", wall.Type);             // no "type" field — key prefix used
        Assert.Equal("", wall.Tel);
    }

    // ---- html stripping -------------------------------------------------------

    [Fact]
    public void StripHtml_removes_tags_and_decodes_entities()
    {
        Assert.Equal("Queen of Grit", Jx.StripHtml("<p>Queen&nbsp;of&nbsp;Grit</p>"));
        Assert.Equal("rockrun.com", Jx.StripHtml("<a href='https://x'>rockrun.com</a>"));
        Assert.Equal("it's \"fine\"", Jx.StripHtml("it&#39;s &quot;fine&quot;"));
        Assert.Equal("", Jx.StripHtml(""));
        Assert.Equal("a\r\n\r\nb", Jx.StripHtml("<p>a</p>\r\n\r\n\r\n<p>b</p>"));
        Assert.Equal("&unknown;", Jx.StripHtml("&unknown;")); // unrecognized entity passes through
    }
}
