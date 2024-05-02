using System.Diagnostics;

namespace LeagueProxyLib;

public class LeagueProxy
{
    private ProxyServer<ConfigController> _ConfigServer;

    private RiotClient _RiotClient;
    private CancellationTokenSource? _ServerCTS;

    public LeagueProxyEvents Events => LeagueProxyEvents.Instance;

    public LeagueProxy()
    {
        _ConfigServer = new ProxyServer<ConfigController>(29150);

        _RiotClient = new RiotClient();
        _ServerCTS = null;
    }

    // Start proxy servers.
    public void Start()
    {
        if (_ServerCTS is not null)
            throw new Exception("Proxy servers are already running!");

        _ServerCTS = new CancellationTokenSource();
        _ConfigServer.Start(_ServerCTS.Token);
    }

    // Stop proxy servers.
    public void Stop()
    {
        if (_ServerCTS is null)
            throw new Exception("Proxy servers are not running!");

        _ServerCTS.Cancel();
        _ServerCTS = null;
    }

    // Launch Riot Client that talks to our proxy server.
    // You _HAVE_ to call Start before.
    public Process? LaunchRCS()
    {
        if (_ServerCTS is null)
            throw new Exception("Proxy servers are not running!");

        return _RiotClient.Launch(_ConfigServer.Url);
    }

    public Process? StartAndLaunchRCS()
    {
        if (_ServerCTS is not null)
            throw new Exception("Proxy servers are already running!");

        Start();
        return LaunchRCS();
    }
}
