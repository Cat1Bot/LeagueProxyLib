using EmbedIO;

namespace LeagueProxyLib;

internal sealed class ConfigProxy
{
    private HttpClient _Client;

    private const string BASE_URL = "https://clientconfig.rpg.riotgames.com";

    public ConfigProxy()
    {
        // Configure HttpClient with a handler that disables cookies
        var handler = new HttpClientHandler
        {
            UseCookies = false
        };
        _Client = new HttpClient(handler);
    }

    public Task<HttpResponseMessage> Process(IHttpRequest request)
    {
        var url = BASE_URL + request.RawUrl;

        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.TryAddWithoutValidation("User-Agent", request.Headers["user-agent"]);

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

        return _Client.SendAsync(message);
    }
}
