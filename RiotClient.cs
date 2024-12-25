using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LeagueProxyLib;

internal sealed class RiotClient
{
    public RiotClient()
    {

    }

    public Process? Launch(string configServerUrl, IEnumerable<string>? args = null)
    {
        var path = GetPath();
        if (path is null)
            return null;

        IEnumerable<string> allArgs = [$"--client-config-url={configServerUrl}", "--launch-product=league_of_legends","--launch-patchline=live", .. args ?? []];

        return Process.Start(path, allArgs);
    }

    private string? GetPath()
    {
        string installPath;

        if (OperatingSystem.IsMacOS())
        {
            installPath = "/Users/Shared/Riot Games/RiotClientInstalls.json";
        }
        else
        {
            installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                       "Riot Games/RiotClientInstalls.json");
        }

        if (!File.Exists(installPath))
            return null;

        try
        {
            var data = JsonSerializer.Deserialize<JsonNode>(File.ReadAllText(installPath));
            var rcPaths = new List<string?> { data?["rc_default"]?.ToString(), data?["rc_live"]?.ToString(), data?["rc_beta"]?.ToString() };

            return rcPaths.FirstOrDefault(File.Exists);
        }
        catch
        {
            return null;
        }
    }
}