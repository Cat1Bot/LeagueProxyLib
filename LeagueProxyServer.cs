using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using System.Diagnostics;
using EmbedIO.Utilities;
using System.Text.Json.Nodes;
using System.Text.Json;
using Swan.Logging;
using System.Net;
using System.IO.Compression;

namespace LeagueProxyLib;

public sealed class LeagueProxyEvents
{
    public delegate string ProcessBasicEndpoint(string content, IHttpRequest request);

    public event ProcessBasicEndpoint? OnProcessConfigPublic;
    public event ProcessBasicEndpoint? OnProcessConfigPlayer;
    public event ProcessBasicEndpoint? OnProcessLedge;

    private static LeagueProxyEvents? _Instance = null;

    internal static LeagueProxyEvents Instance
    {
        get
        {
            _Instance ??= new LeagueProxyEvents();
            return _Instance;
        }
    }

    private LeagueProxyEvents()
    {
        OnProcessConfigPublic = null;
        OnProcessConfigPlayer = null;
        OnProcessLedge = null;
    }

    private string InvokeProcessBasicEndpoint(ProcessBasicEndpoint? @event, string content, IHttpRequest? request)
    {
        if (@event is null)
            return content;

        foreach (var i in @event.GetInvocationList())
        {
            var result = i.DynamicInvoke(content, request); // Pass 'content' and 'request'
            if (result is not string resultString)
                throw new Exception("Return value of an event is not string!");

            content = resultString;
        }

        return content;
    }

    internal string InvokeProcessConfigPublic(string content, IHttpRequest request) => InvokeProcessBasicEndpoint(OnProcessConfigPublic, content, request);
    internal string InvokeProcessConfigPlayer(string content, IHttpRequest request) => InvokeProcessBasicEndpoint(OnProcessConfigPlayer, content, request);
    internal string InvokeProcessLedge(string content, IHttpRequest request) => InvokeProcessBasicEndpoint(OnProcessLedge, content, request);
}

internal sealed class ConfigController : WebApiController
{
    private static HttpClient _Client = new(new HttpClientHandler { UseCookies = false });
    private const string BASE_URL = "https://clientconfig.rpg.riotgames.com";

    private static LeagueProxyEvents _Events => LeagueProxyEvents.Instance;

    [Route(HttpVerbs.Get, "/api/v1/config/public")]
    public async Task GetConfigPublic()
    {
        var response = await ClientConfig(HttpContext.Request);
        var content = await response.Content.ReadAsStringAsync();

        content = _Events.InvokeProcessConfigPublic(content, HttpContext.Request);

        await SendResponse(response, content);
    }

    [Route(HttpVerbs.Get, "/api/v1/config/player")]
    public async Task GetConfigPlayer()
    {
        var response = await ClientConfig(HttpContext.Request);
        var content = await response.Content.ReadAsStringAsync();

        var configObject = JsonSerializer.Deserialize<JsonNode>(content);

        var leagueEdgeUrlNode = configObject?["lol.client_settings.league_edge.url"];
        if (leagueEdgeUrlNode != null)
        {
            SharedLeagueEdgeUrl.Set(leagueEdgeUrlNode.ToString());
            Console.WriteLine($"Ledge Domain: {SharedLeagueEdgeUrl.Get()}");
        }

        JsonSerializer.Serialize(configObject);
        content = _Events.InvokeProcessConfigPlayer(content, HttpContext.Request);

        await SendResponse(response, content);
    }

    private async Task<HttpResponseMessage> ClientConfig(IHttpRequest request)
    {
        var url = BASE_URL + request.RawUrl;

        using var message = new HttpRequestMessage(HttpMethod.Get, url);

        message.Headers.TryAddWithoutValidation("user-agent", request.Headers["user-agent"]);
        //message.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");

        if (request.Headers["x-riot-entitlements-jwt"] is not null)
            message.Headers.TryAddWithoutValidation("X-Riot-Entitlements-JWT", request.Headers["x-riot-entitlements-jwt"]);

        if (request.Headers["authorization"] is not null)
            message.Headers.TryAddWithoutValidation("Authorization", request.Headers["authorization"]);

        if (request.Headers["x-riot-rso-identity-jwt"] is not null)
            message.Headers.TryAddWithoutValidation("X-Riot-RSO-Identity-JWT", request.Headers["x-riot-rso-identity-jwt"]);

        if (request.Headers["baggage"] is not null)
            message.Headers.TryAddWithoutValidation("baggage", request.Headers["baggage"]);

        if (request.Headers["traceparent"] is not null)
            message.Headers.TryAddWithoutValidation("traceparent", request.Headers["traceparent"]);

        message.Headers.TryAddWithoutValidation("Accept", "application/json");

        return await _Client.SendAsync(message);
    }

    private async Task SendResponse(HttpResponseMessage response, string content)
    {
        var responseBuffer = Encoding.UTF8.GetBytes(content);

        HttpContext.Response.SendChunked = false;
        HttpContext.Response.ContentType = "application/json";
        HttpContext.Response.ContentLength64 = responseBuffer.Length;
        HttpContext.Response.StatusCode = (int)response.StatusCode;

        await HttpContext.Response.OutputStream.WriteAsync(responseBuffer, 0, responseBuffer.Length);
        HttpContext.Response.OutputStream.Close();
    }
}

internal sealed class LedgeController : WebApiController
{
    private static HttpClient _Client = new(new HttpClientHandler { UseCookies = false });

    private static string LEDGE_URL => EnsureLedgeUrlIsSet();

    private static LeagueProxyEvents _Events => LeagueProxyEvents.Instance;

    [Route(HttpVerbs.Get, "/", true)]
    public async Task GetLedge()
    {
        if (HttpContext.Request.Url.LocalPath == "/leagues-ledge/v2/notifications")
        {
            return;
        }

        var response = await GetLedge(HttpContext.Request);
        var content = await response.Content.ReadAsStringAsync();

        content = _Events.InvokeProcessLedge(content, HttpContext.Request);

        await SendResponse(response, content);
    }

    [Route(HttpVerbs.Post, "/", true)]
    public async Task PostLedge()
    {
        string requestBody;
        using (var reader = new StreamReader(HttpContext.OpenRequestStream()))
        {
            requestBody = await reader.ReadToEndAsync();
        }

        var response = await PostLedge(HttpContext.Request, requestBody);
        var content = await response.Content.ReadAsStringAsync();

        content = _Events.InvokeProcessLedge(content, HttpContext.Request);

        await SendResponse(response, content);
    }

    [Route(HttpVerbs.Put, "/", true)]
    public async Task PutLedge()
    {
        string requestBody;
        using (var reader = new StreamReader(HttpContext.OpenRequestStream()))
        {
            requestBody = await reader.ReadToEndAsync();
        }

        var response = await PutLedge(HttpContext.Request, requestBody);
        var content = await response.Content.ReadAsStringAsync();

        content = _Events.InvokeProcessLedge(content, HttpContext.Request);

        await SendResponse(response, content);
    }

    private static string EnsureLedgeUrlIsSet()
    {
        var ledgeUrl = SharedLeagueEdgeUrl.Get();

        if (string.IsNullOrEmpty(ledgeUrl))
        {
            // Handle the case where LEDGE_URL is not set or is empty.
            // You can log an error, throw an exception, or provide a fallback URL.
            throw new InvalidOperationException("Ledge URL is not set.");
        }

        return ledgeUrl;
    }

    private async Task<HttpResponseMessage> PutLedge(IHttpRequest request, string body)
    {
        var url = LEDGE_URL + request.RawUrl;

        using var message = new HttpRequestMessage(HttpMethod.Put, url);

        message.Headers.TryAddWithoutValidation("user-agent", request.Headers["user-agent"]);

        if (request.Headers["content-encoding"] is not null)
            message.Headers.TryAddWithoutValidation("Content-Encoding", request.Headers["content-encoding"]);

        if (request.Headers["content-type"] is not null)
            message.Content = new StringContent(body, Encoding.UTF8, request.Headers["content-type"]);

        if (request.Headers["authorization"] is not null)
            message.Headers.TryAddWithoutValidation("Authorization", request.Headers["authorization"]);

        message.Headers.TryAddWithoutValidation("Accept", "application/json");

        if (!string.IsNullOrEmpty(body))
            message.Content = new StringContent(body, Encoding.UTF8, "application/json");

        if (request.Headers["content-length"] is not null)
        {
            if (long.TryParse(request.Headers["content-length"], out var contentLength))
                message.Content.Headers.ContentLength = contentLength;
        }

        return await _Client.SendAsync(message);
    }
    private async Task<HttpResponseMessage> PostLedge(IHttpRequest request, string body)
    {
        var url = LEDGE_URL + request.RawUrl;

        using var message = new HttpRequestMessage(HttpMethod.Post, url);

        message.Headers.TryAddWithoutValidation("user-agent", request.Headers["user-agent"]);

        if (request.Headers["authorization"] is not null)
            message.Headers.TryAddWithoutValidation("Authorization", request.Headers["authorization"]);

        if (request.Headers["content-type"] is not null)
        {
            message.Content = new StringContent(body, null, request.Headers["content-type"]);
        }

        if (request.Headers["content-encoding"] is not null)
        {
            message.Content = new StringContent(body, null, request.Headers["content-encoding"]);
        }

        message.Headers.TryAddWithoutValidation("Accept", "application/json");

        if (request.Headers["content-length"] is not null)
        {
            if (long.TryParse(request.Headers["content-length"], out var contentLength))
                message.Content.Headers.ContentLength = contentLength;
        }

        return await _Client.SendAsync(message);
    }

    private async Task<HttpResponseMessage> GetLedge(IHttpRequest request)
    {
        var url = LEDGE_URL + request.RawUrl;

        using var message = new HttpRequestMessage(HttpMethod.Get, url);

        if (request.Headers["accept-encoding"] is not null)
            message.Headers.TryAddWithoutValidation("Accept-Encoding", request.Headers["accept-encoding"]);

        message.Headers.TryAddWithoutValidation("user-agent", request.Headers["user-agent"]);

        if (request.Headers["authorization"] is not null)
            message.Headers.TryAddWithoutValidation("Authorization", request.Headers["authorization"]);

        message.Headers.TryAddWithoutValidation("Accept", "application/json");

        return await _Client.SendAsync(message);
    }

    private async Task SendResponse(HttpResponseMessage response, string content)
    {
        HttpContext.Response.SendChunked = false;
        HttpContext.Response.ContentType = "application/json";
        HttpContext.Response.ContentLength64 = response.Content.Headers.ContentLength ?? 0;
        HttpContext.Response.StatusCode = (int)response.StatusCode;

        if (response.Content.Headers.ContentEncoding.Contains("gzip"))
        {
            HttpContext.Response.Headers.Add("Content-Encoding", "gzip");
        }

        await response.Content.CopyToAsync(HttpContext.Response.OutputStream);
        HttpContext.Response.OutputStream.Close();
    }
}
public static class SharedLeagueEdgeUrl
{
    public static string? _leagueEdgeUrl;

    public static string? Get()
    {
        return _leagueEdgeUrl;
    }

    public static void Set(string url)
    {
        _leagueEdgeUrl = url;
    }
}

internal sealed class ProxyServer<T> where T : WebApiController, new()
{
    private WebServer _WebServer;
    private int _Port;

    public string Url => $"http://127.0.0.1:{_Port}";

    public ProxyServer(int port)
    {
        _Port = port;

        _WebServer = new WebServer(o => o
                .WithUrlPrefix(Url)
                .WithMode(HttpListenerMode.EmbedIO))
                .WithWebApi("/", m => m
                    .WithController<T>()
                );
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        _WebServer.Start(cancellationToken);
    }

    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        return _WebServer.RunAsync(cancellationToken);
    }
}

public class LeagueProxy
{
    private ProxyServer<ConfigController> _ConfigServer;
    private ProxyServer<LedgeController> _LedgeServer;
    private RiotClient _RiotClient;
    private CancellationTokenSource? _ServerCTS;

    public LeagueProxyEvents Events => LeagueProxyEvents.Instance;

    public LeagueProxy()
    {
        _ConfigServer = new ProxyServer<ConfigController>(29150); // Port for ConfigServer
        _LedgeServer = new ProxyServer<LedgeController>(29151);   // Port for LedgeServer
        _RiotClient = new RiotClient();
        _ServerCTS = null;
    }

    private void TerminateRiotServices()
    {
        string[] riotProcesses = { "RiotClientServices", "LeagueClient" };

        foreach (var processName in riotProcesses)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);

                foreach (var process in processes)
                {
                    process.Kill();
                    process.WaitForExit();
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.WriteLine($"Stopping {processName}...");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error terminating {processName}");
                Console.ResetColor();
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Riot processes terminated, restarting now to apply patches.");
        Console.ResetColor();
    }

    public void Start(out string configServerUrl, out string ledgeServerUrl)
    {
        if (_ServerCTS is not null)
            throw new Exception("Proxy servers are already running!");

        TerminateRiotServices();

        //Logger.UnregisterLogger<ConsoleLogger>();
        _ServerCTS = new CancellationTokenSource();

        _ConfigServer.Start(_ServerCTS.Token);
        configServerUrl = _ConfigServer.Url;
        Console.WriteLine($"Config Server running at: {_ConfigServer.Url}");

        _LedgeServer.Start(_ServerCTS.Token);
        ledgeServerUrl = _LedgeServer.Url;
        Console.WriteLine($"Ledge Server running at: {_LedgeServer.Url}");
    }

    public void Stop()
    {
        if (_ServerCTS is null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            throw new Exception("Failed to stop proxy service, service not running.");
        }

        _ServerCTS.Cancel();
        _ServerCTS = null;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Proxy service successfully stopped.");
        Console.ResetColor();
    }

    public Process? LaunchRCS(IEnumerable<string>? args = null)
    {
        if (_ServerCTS is null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            throw new Exception("Error starting Riot client");
        }

        return _RiotClient.Launch(_ConfigServer.Url, args);
    }

    public Process? StartAndLaunchRCS(IEnumerable<string>? args = null)
    {
        if (_ServerCTS is not null)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            throw new Exception("Proxy Servers are already running or ports are in use.");
        }

        Start(out _, out _);
        return LaunchRCS(args);
    }
}