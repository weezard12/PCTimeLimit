using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PCTimeLimitShared.Contracts;

var baseUrl = Environment.GetEnvironmentVariable("PCTIMELIMIT_OPS_BASEURL")?.Trim();
var opsKey = Environment.GetEnvironmentVariable("PCTIMELIMIT_OPS_KEY")?.Trim();

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0].Trim().ToLowerInvariant();
if (command == "help")
{
    PrintUsage();
    return 0;
}

if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
{
    Console.Error.WriteLine("Set PCTIMELIMIT_OPS_BASEURL to your server URL, e.g. https://pctimelimit.example");
    return 1;
}

if (string.IsNullOrWhiteSpace(opsKey))
{
    Console.Error.WriteLine("Set PCTIMELIMIT_OPS_KEY to the ops key configured on the server.");
    return 1;
}

using var http = new HttpClient
{
    BaseAddress = new Uri(uri.ToString().TrimEnd('/') + "/")
};
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
http.DefaultRequestHeaders.Add(ApiHeaders.OpsKey, opsKey);

try
{
    return command switch
    {
        "status" => await StatusAsync(http),
        "create-admin" => await CreateAdminAsync(http, args),
        "list-users" => await ListUsersAsync(http),
        "list-computers" => await ListComputersAsync(http),
        "clear-user-data" => await ClearUsersAsync(http),
        "clear-computer-data" => await ClearComputersAsync(http),
        "help" => PrintUsageAndReturn(),
        _ => UnknownCommand(command)
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Command failed: {ex.Message}");
    return 1;
}

static async Task<int> StatusAsync(HttpClient http)
{
    var response = await http.GetFromJsonAsync<OpsStatusResponse>("api/v1/ops/status");
    if (response is null)
    {
        Console.Error.WriteLine("No response from server.");
        return 1;
    }

    Console.WriteLine($"Admins: {response.AdminCount}");
    Console.WriteLine($"Computers: {response.ComputerCount}");
    Console.WriteLine($"Server UTC: {response.ServerTimeUtc:O}");
    return 0;
}

static async Task<int> CreateAdminAsync(HttpClient http, string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Usage: create-admin <username> <password>");
        return 1;
    }

    var payload = new OpsCreateAdminRequest
    {
        Username = args[1],
        Password = args[2]
    };

    using var response = await http.PostAsJsonAsync("api/v1/ops/create-admin", payload);
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine(await ReadErrorAsync(response));
        return 1;
    }

    var result = await response.Content.ReadFromJsonAsync<OpsUserDto>();
    if (result is null)
    {
        Console.Error.WriteLine("Server returned empty response.");
        return 1;
    }

    Console.WriteLine($"Created admin: {result.Username}");
    Console.WriteLine($"Admin code: {result.AdminCode}");
    return 0;
}

static async Task<int> ListUsersAsync(HttpClient http)
{
    var response = await http.GetFromJsonAsync<OpsUsersResponse>("api/v1/ops/users");
    if (response is null)
    {
        Console.Error.WriteLine("No response from server.");
        return 1;
    }

    foreach (var user in response.Users)
    {
        Console.WriteLine($"{user.Username}\t{user.AdminCode}\t{user.CreatedAtUtc:O}\t{(user.LastLoginAtUtc.HasValue ? user.LastLoginAtUtc.Value.ToString("O") : "never")}");
    }

    Console.WriteLine($"Total users: {response.Users.Count}");
    return 0;
}

static async Task<int> ListComputersAsync(HttpClient http)
{
    var response = await http.GetFromJsonAsync<OpsComputersResponse>("api/v1/ops/computers");
    if (response is null)
    {
        Console.Error.WriteLine("No response from server.");
        return 1;
    }

    foreach (var computer in response.Computers)
    {
        Console.WriteLine($"{computer.ComputerName}\t{computer.ComputerId}\t{computer.AdminUsername}\t{computer.DailyTimeLimit}\t{computer.IsOnline}");
    }

    Console.WriteLine($"Total computers: {response.Computers.Count}");
    return 0;
}

static async Task<int> ClearUsersAsync(HttpClient http)
{
    using var response = await http.DeleteAsync("api/v1/ops/users");
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine(await ReadErrorAsync(response));
        return 1;
    }

    Console.WriteLine("All user data cleared.");
    return 0;
}

static async Task<int> ClearComputersAsync(HttpClient http)
{
    using var response = await http.DeleteAsync("api/v1/ops/computers");
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine(await ReadErrorAsync(response));
        return 1;
    }

    Console.WriteLine("All computer data cleared.");
    return 0;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return 1;
}

static int PrintUsageAndReturn()
{
    PrintUsage();
    return 0;
}

static void PrintUsage()
{
    Console.WriteLine("PCTimeLimit Ops CLI");
    Console.WriteLine("Commands:");
    Console.WriteLine("  status");
    Console.WriteLine("  create-admin <username> <password>");
    Console.WriteLine("  list-users");
    Console.WriteLine("  list-computers");
    Console.WriteLine("  clear-user-data");
    Console.WriteLine("  clear-computer-data");
    Console.WriteLine("\nEnvironment:");
    Console.WriteLine("  PCTIMELIMIT_OPS_BASEURL");
    Console.WriteLine("  PCTIMELIMIT_OPS_KEY");
}

static async Task<string> ReadErrorAsync(HttpResponseMessage response)
{
    var body = await response.Content.ReadAsStringAsync();
    if (string.IsNullOrWhiteSpace(body))
    {
        return $"Request failed: {(int)response.StatusCode} {response.ReasonPhrase}";
    }

    try
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("message", out var message))
        {
            return message.GetString() ?? body;
        }

        if (document.RootElement.TryGetProperty("title", out var title))
        {
            return title.GetString() ?? body;
        }
    }
    catch
    {
        // keep raw body
    }

    return body;
}
