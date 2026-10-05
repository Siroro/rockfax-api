namespace RockfaxApi;

/// <summary>
/// Which site's data a request targets — mirrors UKCAPI.SITE_* constants
/// (used to build route segments like crag_ukc / logbook_rockfax).
/// </summary>
public enum Site
{
    UkClimbing = 0,
    Rockfax = 1,
    UkHillwalking = 2,
}

internal static class SiteExtensions
{
    internal static string ToRouteSegment(this Site site) => site switch
    {
        Site.UkClimbing => "ukc",
        Site.Rockfax => "rockfax",
        Site.UkHillwalking => "ukh",
        _ => "",
    };
}
