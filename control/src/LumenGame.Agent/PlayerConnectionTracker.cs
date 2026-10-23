using System.Text.RegularExpressions;

namespace LumenGame.Agent;

public static partial class PlayerConnectionTracker
{
    [GeneratedRegex(@"Got connection SteamID (?<connected>\d+)|Closing socket (?<disconnected>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionEvent();

    public static int CountConnected(string logs)
    {
        HashSet<string> connected = new(StringComparer.Ordinal);
        foreach (Match match in ConnectionEvent().Matches(logs))
        {
            if (match.Groups["connected"].Success)
            {
                connected.Add(match.Groups["connected"].Value);
            }
            else if (match.Groups["disconnected"].Success)
            {
                connected.Remove(match.Groups["disconnected"].Value);
            }
        }
        return connected.Count;
    }
}
