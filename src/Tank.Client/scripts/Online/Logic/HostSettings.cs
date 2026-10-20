using Tank.Sim.Modes;

namespace Tank.Client.Online;

/// <summary>What the host of a room chose: who they are, the room's name and port, and the first mode and bot count.</summary>
/// <param name="Port">The UDP port to listen on; 0 lets the system pick a free one (tests).</param>
/// <param name="Seed">A fixed match seed (reproducible runs); null picks a random one per match.</param>
/// <param name="MatchSeconds">A match length other than the mode's (screenshots, smoke runs); null for the mode's own.</param>
public sealed record HostSettings(string PlayerName, string RoomName, int Port, BattleMode Mode, int Bots, uint? Seed, double? MatchSeconds);
