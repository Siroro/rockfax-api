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

    default:
        Console.WriteLine("Commands: free-crags | top10 | markers | route-info <id> | search <text> | login <email> <pw> | logbook <email> <pw>");
        break;
    }

static void Print(JsonDocument doc) => Console.WriteLine(doc.RootElement.ToString());
