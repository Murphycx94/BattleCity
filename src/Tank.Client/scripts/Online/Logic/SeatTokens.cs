using System.Net;

namespace Tank.Client.Online;

/// <summary>
/// The reconnect token each room this game joined handed out last, by the address it was joined at, for as long as the
/// game runs. Joining the room again presents it, so the room knows the player — in every mode: within the reconnect
/// grace period it hands back the seat still held (the tank, or the wreck), and in a match without respawns it keeps a
/// player who already had a tank in it from taking another (stage 5B: no rejoining for a fresh tank). A room that
/// doesn't know the token issues a new one, as for a player with none. Engine-free.
/// </summary>
/// <remarks>
/// The address is normalised (<see cref="Normalise"/>) but never looked up: the same machine by another route (its LAN
/// address and 127.0.0.1, a name and its address) is another room here. The store belongs to the running game, so two
/// people taking turns on one game share its tokens.
/// </remarks>
public sealed class SeatTokens
{
    private const string Loopback = "127.0.0.1";

    private readonly Dictionary<string, uint> _byRoom = new(StringComparer.Ordinal);

    /// <summary>Keeps <paramref name="token"/> for the room at <paramref name="host"/>:<paramref name="port"/>; 0 (no seat was given) changes nothing.</summary>
    public void Remember(string host, int port, uint token)
    {
        if (token != 0) _byRoom[Key(host, port)] = token;
    }

    /// <summary>The token last remembered for that room, or 0.</summary>
    public uint For(string host, int port) => _byRoom.TryGetValue(Key(host, port), out uint token) ? token : 0;

    private static string Key(string host, int port) => $"{Normalise(host)}:{port}";

    /// <summary>
    /// One spelling per address, without a lookup: trimmed, lower case, out of <c>[ ]</c>; an IP address in its standard
    /// form, an IPv4-mapped IPv6 one as the IPv4 address; <c>localhost</c> and <c>::1</c> as 127.0.0.1. A name stays a name.
    /// </summary>
    private static string Normalise(string host)
    {
        string h = host.Trim().ToLowerInvariant();
        if (h.StartsWith('[') && h.EndsWith(']')) h = h[1..^1];
        if (h == "localhost") return Loopback;
        if (!IPAddress.TryParse(h, out IPAddress? address)) return h;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return address.Equals(IPAddress.IPv6Loopback) ? Loopback : address.ToString();
    }
}
