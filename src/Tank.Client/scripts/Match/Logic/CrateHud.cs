using System.Globalization;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Client.Match;

/// <summary>
/// The player's own buff layers for the HUD (stage 6 design §4.3): the row of three icons over the status panel, a layer
/// count on each, the ones at 0 not drawn. From <c>BattleClient.Own</c>; all 0 when there is no tank. A plain value: a view
/// redraws only when the frame's differs from the last.
/// </summary>
public readonly record struct BuffHud(byte Armor, byte Reload, byte Optics)
{
    /// <summary>The three layers of <paramref name="own"/>.</summary>
    public static BuffHud From(in TankState own) => new(own.ArmorLayers, own.ReloadLayers, own.OpticsLayers);

    /// <summary>Whether any icon shows.</summary>
    public bool Any => Armor + Reload + Optics > 0;
}

/// <summary>
/// One pickup toast (stage 6 design §4.3): what the crate did (<see cref="CrateTake"/>'s numbers, as the player's own
/// <c>CrateMessage</c> carries them) and when the line goes, on the scene clock.
/// </summary>
public readonly record struct PickupLine(CrateKind Kind, int Amount, int Amount2, byte Layers, double ExpiresAt)
{
    /// <summary>The line's words (<see cref="CrateText.Pickup"/>); allocates, so read it when the feed's revision changed.</summary>
    public string Text => CrateText.Pickup(Kind, Amount, Amount2, Layers);
}

/// <summary>
/// The pickup toasts' list, like <see cref="KillFeed"/>: the newest pickups, at most <see cref="Capacity"/>, each gone
/// <see cref="Lifetime"/> seconds after it was added; the oldest goes when another comes. Oldest first, newest last. A fixed
/// array, so adding and reading allocate nothing; <see cref="Revision"/> changes whenever the visible lines do.
/// </summary>
public sealed class PickupFeed
{
    public const int Capacity = 3;
    public const double Lifetime = 2.5;   // s

    private readonly PickupLine[] _lines = new PickupLine[Capacity];
    private int _count;

    /// <summary>Changes whenever a line is added, expires or is cleared.</summary>
    public int Revision { get; private set; }

    /// <param name="now">Seconds on the clock the caller reads <see cref="Visible"/> with.</param>
    public void Add(CrateKind kind, int amount, int amount2, byte layers, double now)
    {
        if (_count == Capacity)
        {
            Array.Copy(_lines, 1, _lines, 0, Capacity - 1);
            _count--;
        }
        _lines[_count++] = new PickupLine(kind, amount, amount2, layers, now + Lifetime);
        Revision++;
    }

    /// <summary>Drops every line (leaving the match).</summary>
    public void Clear()
    {
        _count = 0;
        Revision++;
    }

    /// <summary>The lines still showing at <paramref name="now"/> (<c>ExpiresAt &gt; now</c>), oldest first; valid until the next call.</summary>
    public ReadOnlySpan<PickupLine> Visible(double now)
    {
        int expired = 0;
        while (expired < _count && _lines[expired].ExpiresAt <= now) expired++;   // added in order with one lifetime: the oldest expire first
        if (expired > 0)
        {
            Array.Copy(_lines, expired, _lines, 0, _count - expired);
            _count -= expired;
            Revision++;
        }
        return new ReadOnlySpan<PickupLine>(_lines, 0, _count);
    }
}

/// <summary>The crates' words on the HUD (stage 6 design §4.3). Every method but <see cref="Name"/> allocates: call them when the values changed.</summary>
public static class CrateText
{
    public const string FullTail = "已满";

    /// <summary>维修 / 弹药 / 加固装甲 / 快速装填 / 光学设备.</summary>
    public static string Name(CrateKind kind) => kind switch
    {
        CrateKind.Repair => "维修",
        CrateKind.Ammo => "弹药",
        CrateKind.Armor => "加固装甲",
        CrateKind.Reload => "快速装填",
        CrateKind.Optics => "光学设备",
        _ => "补给",
    };

    /// <summary>
    /// "+ 维修 270", "+ 弹药 穿甲 20 · 高爆 8" (one kind of round at 0: only the other, "+ 弹药 高爆 8"), "+ 快速装填 2/3";
    /// "{name}已满" ("光学设备已满", "维修已满", "弹药已满") when the crate did nothing (<paramref name="amount"/> and
    /// <paramref name="amount2"/> both 0).
    /// </summary>
    public static string Pickup(CrateKind kind, int amount, int amount2, byte layers)
    {
        string name = Name(kind);
        if (amount == 0 && amount2 == 0) return string.Create(CultureInfo.InvariantCulture, $"{name}{FullTail}");
        return kind switch
        {
            CrateKind.Repair => string.Create(CultureInfo.InvariantCulture, $"+ {name} {amount}"),
            CrateKind.Ammo when amount2 == 0 => string.Create(CultureInfo.InvariantCulture, $"+ {name} 穿甲 {amount}"),
            CrateKind.Ammo when amount == 0 => string.Create(CultureInfo.InvariantCulture, $"+ {name} 高爆 {amount2}"),
            CrateKind.Ammo => string.Create(CultureInfo.InvariantCulture, $"+ {name} 穿甲 {amount} · 高爆 {amount2}"),
            _ => string.Create(CultureInfo.InvariantCulture, $"+ {name} {layers}/{CrateRules.MaxLayers}"),
        };
    }

    /// <summary>A buff icon's count: "×2".</summary>
    public static string Layers(byte layers) => string.Create(CultureInfo.InvariantCulture, $"×{layers}");
}
