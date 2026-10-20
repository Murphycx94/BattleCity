using System.Diagnostics;
using System.Net.Sockets;
using Tank.Net.Protocol;
using Tank.Net.Transport;

namespace Tank.Client.Online;

/// <summary>
/// The menu's view of the LAN: wraps <see cref="LanDiscovery"/>, keeps only battle rooms, and turns a failing socket
/// (no network, a firewall) into <see cref="Error"/> text instead of an exception. Call <see cref="Poll"/> every frame;
/// <see cref="Rows"/> is a new list only when a room was heard from, came or went (each reply is a new record), so a
/// caller can tell a change by reference.
/// </summary>
public sealed class LanBrowser : IDisposable
{
    private const double RetrySeconds = 2;   // after a failure, how long before the socket is opened again

    private readonly int _port;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private LanDiscovery? _discovery;
    private DiscoveredBattleRoom[] _heard = Array.Empty<DiscoveredBattleRoom>();   // what Rows was built from
    private double _retryAt;

    /// <param name="port">The port rooms are listening for discovery on (only the default one is broadcast to).</param>
    public LanBrowser(int port = ProtocolInfo.DefaultPort) => _port = port;

    /// <summary>The battle rooms heard from lately, by name.</summary>
    public IReadOnlyList<LanRoomRow> Rows { get; private set; } = Array.Empty<LanRoomRow>();

    /// <summary>Why searching doesn't work right now (for the menu's status line); null when it does.</summary>
    public string? Error { get; private set; }

    public void Poll()
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (_discovery is null && now < _retryAt) return;
        try
        {
            _discovery ??= new LanDiscovery(_port);
            _discovery.Poll();
            if (!SameAsHeard(_discovery.BattleRooms))
            {
                _heard = _discovery.BattleRooms.ToArray();
                Rows = _heard.OrderBy(r => r.Info.RoomName, StringComparer.Ordinal).ThenBy(r => r.EndPoint.ToString(), StringComparer.Ordinal)
                    .Select(LanRoomRow.From).ToList();
            }
            Error = null;
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException)
        {
            Fail(ex.Message, now);
        }
    }

    public void Dispose()
    {
        _discovery?.Dispose();
        _discovery = null;
    }

    /// <summary>Whether the discovered rooms are the very records the rows were built from (none came, went or replied since).</summary>
    private bool SameAsHeard(IReadOnlyCollection<DiscoveredBattleRoom> rooms)
    {
        if (rooms.Count != _heard.Length) return false;
        if (_heard.Length == 0) return true;   // nothing out there: not even an enumerator
        int i = 0;
        foreach (DiscoveredBattleRoom room in rooms)
            if (!ReferenceEquals(room, _heard[i++])) return false;
        return true;
    }

    private void Fail(string message, double now)
    {
        Dispose();
        _heard = Array.Empty<DiscoveredBattleRoom>();
        Rows = Array.Empty<LanRoomRow>();
        Error = $"无法搜索局域网：{message}";
        _retryAt = now + RetrySeconds;
    }
}
