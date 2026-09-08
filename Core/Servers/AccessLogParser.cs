using System.Text.RegularExpressions;

namespace McServerLauncher.Core.Servers;

public sealed record AccessLogEntry(DateTime Time, string PlayerName, bool Joined);

/// <summary>Extracts player join/leave events from raw server console lines.</summary>
public static partial class AccessLogParser
{
    [GeneratedRegex(@"^(?:\[[^\]]*\]\s*)*(?<name>\S+) (?:joined the game|left the game|has connected|has disconnected)")]
    private static partial Regex JavaJoinLeaveRegex();

    [GeneratedRegex(@"Player (?:connected|Connected): (?<name>[^,]+),")]
    private static partial Regex BdsConnectRegex();

    [GeneratedRegex(@"Player (?:disconnected|Disconnected): (?<name>[^,]+),")]
    private static partial Regex BdsDisconnectRegex();

    public static AccessLogEntry? TryParse(string line)
    {
        var joinMatch = BdsConnectRegex().Match(line);
        if (joinMatch.Success)
            return new AccessLogEntry(DateTime.Now, joinMatch.Groups["name"].Value, Joined: true);

        var leaveMatch = BdsDisconnectRegex().Match(line);
        if (leaveMatch.Success)
            return new AccessLogEntry(DateTime.Now, leaveMatch.Groups["name"].Value, Joined: false);

        var m = JavaJoinLeaveRegex().Match(line);
        if (m.Success)
        {
            var joined = line.Contains("joined the game") || line.Contains("has connected");
            return new AccessLogEntry(DateTime.Now, m.Groups["name"].Value, joined);
        }

        return null;
    }
}
