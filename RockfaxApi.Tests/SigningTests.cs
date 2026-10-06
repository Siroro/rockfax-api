using Xunit;

namespace RockfaxApi.Tests;

/// <summary>
/// Pins the wire protocol primitives against independently computed vectors.
/// The empty-key value is the real key segment the live API accepted for
/// freeCrags, so any drift here is a wire-visible regression.
/// </summary>
public class SigningTests
{
    [Theory]
    [InlineData("", "d41d8cd98f00b204e9800998ecf8427e")]
    [InlineData("abc", "900150983cd24fb0d6963f7d28e17f72")]
    [InlineData("lfoengoir", "26a0ba5d57b27d54b2b3fa206cd18ea9")]
    public void Md5_matches_reference_vectors(string input, string expected)
        => Assert.Equal(expected, RockfaxSigning.Md5(input));

    [Fact]
    public void DerivePathKey_empty_matches_live_verified_freeCrags_key()
        => Assert.Equal("and_2b3fa206cd18", RockfaxSigning.DerivePathKey(""));

    [Fact]
    public void DerivePathKey_search_uses_url_encoded_query()
        => Assert.Equal("and_8026fbdd032d", RockfaxSigning.DerivePathKey("stanage%20edge"));

    [Fact]
    public void DerivePathKey_takes_characters_17_to_29_of_the_md5()
    {
        string md5 = RockfaxSigning.Md5("userID=123" + RockfaxSigning.PathKeySalt);
        Assert.Equal("and_" + md5.Substring(17, 12), RockfaxSigning.DerivePathKey("userID=123"));
    }

    [Fact]
    public void DeriveApiKey_matches_reference_vector()
        => Assert.Equal("885f0594ae587b46e87efd3ccf29d52a",
            RockfaxSigning.DeriveApiKey("2026-10-05T12:00:00",
                "/and_2b3fa206cd18/logbook/v1/freeCrags_rockfax/", "", ""));

    [Fact]
    public void RequestDateNow_matches_the_app_timestamp_format()
        => Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$", RockfaxSigning.RequestDateNow());
}

public class UrlBuildingTests
{
    private static RockfaxClient CreateClient() => new(transport: RockfaxTransport.Managed);

    [Fact]
    public void Search_key_input_is_the_url_encoded_query()
    {
        // Regression: multi-word searches 401'd when the key was derived from the
        // raw text — the server hashes the path segment as received (encoded).
        using var client = CreateClient();
        string encoded = Uri.EscapeDataString("stanage edge");
        string url = client.BuildUrl($"logbook/v1/app_search_route_ukc/{encoded}",
            keyInput: encoded, nodeApi: false, out string wirePath);

        Assert.Contains("/app_search_route_ukc/stanage%20edge", url);
        Assert.Equal("/and_8026fbdd032d/logbook/v1/app_search_route_ukc/stanage%20edge", wirePath);
    }

    [Fact]
    public void Empty_key_input_still_gets_a_key_segment()
    {
        using var client = CreateClient();
        client.BuildUrl("logbook/v1/freeCrags_rockfax/", keyInput: "", nodeApi: false, out string wirePath);
        Assert.Equal("/and_2b3fa206cd18/logbook/v1/freeCrags_rockfax/", wirePath);
    }

    [Fact]
    public void Node_api_paths_are_absolute_without_a_key_segment()
    {
        using var client = CreateClient();
        string url = client.BuildUrl("/v1/rockfax/whatever", keyInput: null, nodeApi: true, out string wirePath);
        Assert.EndsWith("/v1/rockfax/whatever", url);
        Assert.Equal("/v1/rockfax/whatever", wirePath);
    }
}
