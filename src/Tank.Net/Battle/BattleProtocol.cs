using Tank.Sim.Geometry;
using Tank.Sim.Modes;

namespace Tank.Net.Battle;

/// <summary>
/// Message ids of the battle protocol (stage 4 design §5.2). The first byte of every packet.
/// </summary>
public enum BattleMessage : byte
{
    // client -> server
    Hello = 1,
    Input = 2,
    RoomCommand = 3,
    RequestMatchState = 4,

    // server -> client
    Welcome = 20,
    Reject = 21,
    RoomState = 22,
    MatchStart = 23,
    Snapshot = 24,
    Events = 25,

    /// <summary>A ring announced (v5): <see cref="ZoneMessage"/>, sent reliably when the ring's waiting begins.</summary>
    Zone = 26,

    /// <summary>A crate eaten (v6): <see cref="CrateMessage"/>, sent reliably to the one player who ate it (stage 6 design §4.4).</summary>
    Crate = 27,

    // unconnected (LAN discovery)
    /// <summary>A broadcast asking rooms to answer. A room only reads this byte, so 4B clients (which send 40 then version 1) still find it.</summary>
    Discover = 40,
    RoomInfo = 42,
}

/// <summary>Numbers of the battle protocol (stage 4 design §5), and the quantisation the wire uses for angles and positions.</summary>
public static class BattleProtocol
{
    /// <summary>
    /// 7: duos (stage 6 design §5) — not a bit of the layout changes: the mode byte takes <see cref="BattleMode.Duos"/> (5), a
    /// room roster's team is 0–7 in duos, and in a duos match the match block's winner is a team (no slot), its entrants
    /// the teams that joined (<c>MatchRules.Places</c>) and each scoreboard line's rank its team's place.
    /// 6: the supply crates (stage 6 design §4.4) — the crates a viewer sees in its snapshot (never the crate table), its
    /// own buff layers in its own tank, the pickup message (<see cref="BattleMessage.Crate"/>) to the one player who ate a
    /// crate, the crates a seat knows in the match start (a resync). 5: the royale (stage 6 design §3.5) — 16 seats on the
    /// scoreboard with 5-bit ranks, each player's damage dealt and time lasted, the entrants in the match block, the kill
    /// cause on events, ring announcements (<see cref="BattleMessage.Zone"/>), the rings announced so far in the match
    /// start. 4: the standard battle (stage 5 design §3.3) — the match block carries both bases' progress and why the match
    /// ended, the scoreboard each player's capture, the match start the bases. 3: maps carry their ground kinds and water
    /// level, houses and trees, up to 16384 props (riverland design §5.1).
    /// </summary>
    public const int Version = 7;

    /// <summary>The UDP port a room listens on unless told otherwise; discovery broadcasts only reach rooms on it.</summary>
    public const int DefaultPort = 27015;

    /// <summary>Unchanged since v2, so a client of another version still connects and is told the versions differ.</summary>
    public const string ConnectKey = "tank3d-v2";
    public const int MaxPlayers = ModeRules.MaxPlayers;
    public const int MaxRedundantInputs = 4;
    public const int MaxNameBytes = 24;
    public const int MaxRejectReasonBytes = 128;
    public const int MaxRoomNameBytes = 48;

    /// <summary>The longest map id on the wire ("riverland" is 9).</summary>
    public const int MaxMapIdBytes = 16;

    /// <summary>The largest coordinate an entity may carry on the wire, cm (100 km: far beyond any map; a bigger one is a lie).</summary>
    public const int MaxCoordinateCm = 10_000_000;

    /// <summary>
    /// How far ahead of the newest snapshot a snapshot or event batch may claim to be, in ticks (30 s). Beyond it the
    /// packet is refused: accepted, it would make every real snapshot after it "too old".
    /// </summary>
    public const int MaxTickLead = 30 * Tank.Sim.SimConstants.TickRate;

    /// <summary>
    /// The largest tick a message may carry (about 414 days at 30 Hz). Beyond it the packet is a lie: accepted, the
    /// client's clock lead or the lead check would overflow.
    /// </summary>
    public const int MaxTick = 1 << 30;

    /// <summary>Positions on the wire are whole centimetres.</summary>
    public const float PositionUnit = 0.01f;

    /// <summary>Other tanks' speed on the wire, m/s per step.</summary>
    public const float SpeedUnit = 0.1f;

    public const int AimBits = 16;
    public const int YawBits = 12;
    public const int PitchBits = 10;

    /// <summary>An angle in (−π, π] as <paramref name="bits"/> bits.</summary>
    public static uint QuantizeAngle(float angle, int bits)
    {
        uint steps = 1u << bits;
        float turns = (Angles.Wrap(angle) + Angles.Pi) / Angles.TwoPi;   // 0 … 1
        return (uint)MathF.Round(turns * steps) & (steps - 1);
    }

    public static float DequantizeAngle(uint q, int bits) => Angles.Wrap(q / (float)(1u << bits) * Angles.TwoPi - Angles.Pi);

    /// <summary>A pitch in [−π/2, π/2] as <paramref name="bits"/> bits.</summary>
    public static uint QuantizePitch(float pitch, int bits)
    {
        uint max = (1u << bits) - 1;
        float share = (System.Math.Clamp(pitch, -Angles.Pi / 2, Angles.Pi / 2) + Angles.Pi / 2) / Angles.Pi;
        return (uint)MathF.Round(share * max);
    }

    public static float DequantizePitch(uint q, int bits) => q / (float)((1u << bits) - 1) * Angles.Pi - Angles.Pi / 2;

    /// <summary>The aim a client should predict with: what the server will read from the input packet.</summary>
    public static (float Yaw, float Pitch) QuantizedAim(float yaw, float pitch) =>
        (DequantizeAngle(QuantizeAngle(yaw, AimBits), AimBits), DequantizePitch(QuantizePitch(pitch, AimBits), AimBits));

    public static int ToCm(float metres) => (int)MathF.Round(metres / PositionUnit);

    public static float FromCm(int cm) => cm * PositionUnit;

    /// <summary>A prop's hit points as a byte: 0 only when down, 255 only when untouched.</summary>
    public static byte QuantizeDurability(int durability, int max)
    {
        if (durability <= 0) return 0;
        if (durability >= max) return 255;
        return (byte)System.Math.Clamp((int)MathF.Round(durability * 254f / max), 1, 254);
    }

    public static int DequantizeDurability(byte q, int max) => q switch
    {
        0 => 0,
        255 => max,
        _ when max < 2 => max,   // nothing between "down" and "whole" to express
        _ => System.Math.Clamp((int)MathF.Round(q * max / 254f), 1, max - 1),
    };
}
