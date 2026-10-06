using System.Text.Json;
using RockfaxApi;

// Usage:
//   dotnet run -- free-crags          (public)
//   dotnet run -- top10               (public)
//   dotnet run -- markers             (public)
//   dotnet run -- route-info 52150    (public)
//   dotnet run -- search "stannage"
//   dotnet run -- login <email> <password>
//   dotnet run -- logbook <email> <password>
string command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

using var client = new RockfaxClient(baseUrl: Environment.GetEnvironmentVariable("RF_BASE"));
Console.WriteLine($"DeviceId: {client.DeviceId}");

switch (command)
{
    case "free-crags":
        Print(await client.GetFreeCragsAsync());
        break;

    case "top10":
        Print(await client.GetWeeklyTopTenPhotosAsync());
        break;

    case "markers":
        Print(await client.GetCragMarkersAsync());
        break;

    case "route-info":
    {
        RouteInfo info = await client.GetRouteInfoAsync(int.Parse(args[1]));
        Console.WriteLine($"FA: {info.FirstAscent} ({info.FirstAscentDate})");
        Console.WriteLine($"Height: {info.Height}m, Pitches: {info.Pitches}");
        Console.WriteLine(info.Description);
        break;
    }

    case "search":
        Print(await client.SearchRoutesAsync(args[1]));
        break;

    case "login":
    {
        AuthResponse session = await client.LoginAsync(args[1], args[2]);
        Console.WriteLine($"Logged in as {session.Username} (userId {session.UserId})");
        Console.WriteLine($"Access token expires at unix {session.ExpiresUnix}");
        Print(await client.GetAppPermissionsAsync(session.UserId));
        break;
    }

    case "logbook":
    {
        await client.LoginAsync(args[1], args[2]);
        Print(await client.GetLogbookAsync(client.UserId));
        break;
    }

    case "crag-details": // crag-details <ukc|rockfax|ukh> <id>[|id...]
    {
        var site = ParseSite(args[1]);
        var ids = args[2].Split('|').Select(int.Parse).ToList();
        Print(await client.GetCragDetailsAsync(ids, site));
        break;
    }

    case "crag-routes": // full route rows for one crag
        Print(await client.GetCragRoutesAsync(int.Parse(args[1])));
        break;

    case "weather": // weather <ukc|rockfax> <id>
    {
        var site = ParseSite(args[1]);
        Print(await client.GetWeatherAsync(new[] { int.Parse(args[2]) }, site));
        break;
    }

    case "listings":
        Print(await client.GetFreeListingsAsync());
        break;

    case "logbook-details": // logbook-details <email> <pw>
    {
        await client.LoginAsync(args[1], args[2]);
        Print(await client.GetLogbookRouteDetailsAsync(client.UserId));
        break;
    }

    default:
        Console.WriteLine("Commands: free-crags | top10 | markers | route-info <id> | search <text> | login <email> <pw> | logbook <email> <pw> | crag-details <site> <ids> | crag-routes <id> | weather <site> <id> | listings");
        break;
}

static void Print(JsonDocument doc) => Console.WriteLine(doc.RootElement.ToString());

static Site ParseSite(string s) => s.ToLowerInvariant() switch
{
    "ukc" => Site.UkClimbing,
    "rockfax" or "rf" => Site.Rockfax,
    "ukh" => Site.UkHillwalking,
    _ => throw new ArgumentException($"unknown site '{s}' (ukc|rockfax|ukh)"),
};
