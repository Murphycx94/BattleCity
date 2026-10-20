using Godot;
using Tank.Client.Art;
using Tank.Client.Combat;
using Tank.Client.Overlay;
using Tank.Client.Vehicles;
using Tank.Client.World;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Modes;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Client.Match;

/// <summary>
/// The other tanks of a match, one <see cref="TankActor"/> per tank id. <see cref="Prewarm"/> builds the whole roster's
/// models the frame the first snapshot names it (one long frame before play, instead of a hitch per tank coming into
/// sight); a tank joining later (team battle) is still built the first time it is seen (spec and team from its entity,
/// painted by <see cref="MatchColors"/>). Entries are never removed: each frame <see cref="Sync"/> poses the tanks in
/// <c>BattleClient.Others</c> and hides the rest at once (not in sight is not drawn; no fade). A kill is the event's
/// to show, the snapshot's flag the fallback (<see cref="WreckTracker"/>); a tank first seen — or seen again after
/// being out of sight — as a wreck settles without a blast, and one revived out of sight comes back intact at once. The
/// HUD reads heads and health through <see cref="TryGetHead"/> and <see cref="FillBars"/>.
/// </summary>
public partial class TankActors : Node3D
{
    private const float BlastSize = 1.8f;
    private const float BlastHeight = 1.2f;

    private sealed class Entry
    {
        public required TankActor Actor;
        public required Side Side;
        public required Color LabelColor;
        public required byte Slot;
        public readonly WreckTracker Wreck = new();
        public TankState State;
        public TankPose Pose;
        public float PrevYaw;
        public int SeenStamp;   // the Sync that last had this tank in sight
        public bool Seen;       // ever in sight: a prewarmed tank's first sight still counts as one (wreck settle, yaw rate)
    }

    private readonly Dictionary<int, Entry> _entries = new();
    private readonly List<Entry> _order = new();   // in the order built (the roster by slot, then late joiners): the order the HUD bars go out in
    private HeightField _terrain = null!;
    private BattleFx _fx = null!;
    private int _stamp;

    public void Build(HeightField terrain, BattleFx fx) => (_terrain, _fx) = (terrain, fx);

    /// <summary>A resync swapped the battlefield instance: the same ground, read from the new one.</summary>
    public void SetTerrain(HeightField terrain) => _terrain = terrain;

    /// <summary>
    /// Builds an actor for every listed tank ahead of its first sight (blueprint §5 T10): the first snapshot carries the
    /// whole roster in <paramref name="players"/> (by room slot), so the models — the expensive part — are built in one
    /// frame, under the loading shade, instead of one hitch each as tanks come into view. The actors stay hidden until
    /// <see cref="Sync"/> first sees them; a tank already built, the own one, and an empty slot are passed over.
    /// </summary>
    public void Prewarm(ReadOnlySpan<PlayerStats> players, int ownTankId, BattleMode mode, byte ownTeam)
    {
        for (int slot = 0; slot < players.Length; slot++)
        {
            PlayerStats p = players[slot];
            if (!p.Active || p.TankId == 0 || p.TankId == ownTankId || _entries.ContainsKey(p.TankId)) continue;
            Create(p.TankId, TankCatalog.All[p.Spec], p.Team, (byte)slot, mode, ownTeam);
        }
    }

    /// <summary>
    /// Poses every tank in <paramref name="others"/> (building an actor for one never known before) and hides the ones
    /// not there this frame. <paramref name="ownTeam"/> and <paramref name="mode"/> decide a new tank's colours.
    /// </summary>
    public void Sync(IReadOnlyList<RemoteTank> others, BattleMode mode, byte ownTeam, float dt)
    {
        _stamp++;
        for (int i = 0; i < others.Count; i++)
        {
            RemoteTank remote = others[i];
            TankState state = RenderMath.FromRemote(remote);
            if (!_entries.TryGetValue(state.Id, out Entry? entry))
                entry = Create(state.Id, state.Spec, state.Team, remote.Entity.Slot, mode, ownTeam);
            Show(entry, state, dt);
        }
        for (int i = 0; i < _order.Count; i++)
        {
            Entry entry = _order[i];
            if (entry.SeenStamp != _stamp && entry.Actor.Visible) entry.Actor.Present(entry.State, entry.Pose, 0f, dt, shown: false);
        }
    }

    private Entry Create(int id, TankSpec spec, byte team, byte slot, BattleMode mode, byte ownTeam)
    {
        Side side = MatchColors.SideOf(mode, ownTeam, team, isOwn: false);
        var actor = new TankActor();
        AddChild(actor);
        actor.Build(spec, MatchColors.Paint(side));
        actor.Visible = false;   // nothing shows before Sync first sees the tank
        var entry = new Entry { Actor = actor, Side = side, LabelColor = MatchColors.Ui(side).ToColor(), Slot = slot };
        _entries[id] = entry;
        _order.Add(entry);
        return entry;
    }

    private void Show(Entry entry, in TankState state, float dt)
    {
        // Not drawn last frame (never seen, or out of sight since): whatever happened meanwhile — a kill, a revival, a turn
        // — is the state's to show at once, without the event wait or a yaw rate from a stale heading.
        bool firstSight = !entry.Seen || !entry.Actor.Visible;
        entry.Seen = true;
        entry.SeenStamp = _stamp;
        entry.Pose = TankPose.Compute(state, _terrain);
        float yawRate = firstSight ? 0f : RenderMath.YawRate(entry.PrevYaw, state.Yaw, dt);
        entry.PrevYaw = state.Yaw;
        switch (entry.Wreck.Observe(state.IsDestroyed, dt, firstSight))
        {
            case WreckTracker.Change.Settle: entry.Actor.Wreck(state.TurretYaw, settled: true); break;
            case WreckTracker.Change.Restore: entry.Actor.Restore(); break;
        }
        entry.Actor.Present(state, entry.Pose, yawRate, dt, shown: true);
        entry.State = state;
    }

    /// <summary>Kills and revivals of the other tanks: the kill blows up where the event says, the actor wrecks or is put back.</summary>
    public void OnEvent(in GameEvent e)
    {
        if (e.Type is not (GameEventType.TankDestroyed or GameEventType.TankRevived)) return;
        _entries.TryGetValue(e.TargetTankId, out Entry? entry);
        if (e.Type == GameEventType.TankDestroyed)
        {
            _fx.Explosion(e.Position.ToGodot() + Vector3.Up * BlastHeight, PuffColor.White, BlastSize);
            if (entry is not null && entry.Wreck.Destroyed() == WreckTracker.Change.Wreck) entry.Actor.Wreck(entry.State.TurretYaw);
        }
        else if (entry is not null && entry.Wreck.Revived() == WreckTracker.Change.Restore)
        {
            entry.Actor.Restore();
        }
    }

    /// <summary>The recoil of a shot by tank <paramref name="tankId"/>, if it is in sight (its muzzle flash is the caller's).</summary>
    public void Kick(int tankId)
    {
        if (_entries.TryGetValue(tankId, out Entry? entry) && entry.Actor.Visible) entry.Actor.Kick(entry.State.TurretYaw);
    }

    /// <summary>The point over tank <paramref name="tankId"/>'s turret; false for a tank unknown or not in sight now.</summary>
    public bool TryGetHead(int tankId, out Vector3 head)
    {
        if (_entries.TryGetValue(tankId, out Entry? entry) && entry.Actor.Visible)
        {
            head = entry.Actor.Head;
            return true;
        }
        head = Vector3.Zero;
        return false;
    }

    /// <summary>
    /// Feeds the HUD's tank bars: one per tank in sight and intact, with the player's name (<paramref name="names"/> by
    /// room slot) in its side's colour; the rest hidden. The bars go to the tanks shown this frame, in the order built,
    /// so a match that has seen more tanks than bars (leavers and joiners get new ids) still has one for each in sight;
    /// the name and colour are set with the bar, so they follow the tank. Tanks beyond the HUD's count get none.
    /// </summary>
    public void FillBars(HealthBars bars, int barCount, IReadOnlyList<string?> names)
    {
        int bar = 0;
        for (int i = 0; i < _order.Count && bar < barCount; i++)
        {
            Entry entry = _order[i];
            if (!entry.Actor.Visible || entry.State.IsDestroyed) continue;
            float share = entry.State.Spec is { } spec ? entry.State.Health / (float)spec.MaxHealth : 0f;
            bars.Set(bar++, entry.Actor.Head, share, shown: true, entry.Slot < names.Count ? names[entry.Slot] : null, entry.LabelColor);
        }
        for (; bar < barCount; bar++) bars.Set(bar, Vector3.Zero, 0f, shown: false);
    }
}
