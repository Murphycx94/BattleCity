using System.Numerics;
using Tank.Sim.Modes;

namespace Tank.Net.Battle;

/// <summary>
/// The crates a client has seen (stage 6 design §4.4). The crate table is never sent: a client learns of a crate only
/// through a snapshot row (the viewer sees the crate's <em>position</em>; the row's <c>Standing</c> says whether the
/// crate is still there), its own pickup message, or the known crates a match start carries (a resync). A crate once
/// seen stays known — the world and the minimap draw the standing ones whether or not they are in sight right now —
/// until a later row shows its spot empty or the player eats it. Engine-free; one per client, reused across matches.
/// <see cref="Apply"/> and <see cref="Take"/> allocate nothing; <see cref="Reset"/> runs at a match start.
/// </summary>
public sealed class CrateKnowledge
{
    /// <summary>What is known of one crate id: nothing while <see cref="Known"/> is false.</summary>
    public struct Entry
    {
        /// <summary>Seen at least once (or eaten, or told of by a match start).</summary>
        public bool Known;

        /// <summary>Still there as far as this client knows: what gets drawn.</summary>
        public bool Standing;

        /// <summary>In the newest snapshot's rows: in sight right now.</summary>
        public bool Visible;

        public CrateKind Kind;

        /// <summary>Ground position (x, z), metres.</summary>
        public Vector2 Position;
    }

    private readonly Entry[] _entries = new Entry[CrateRules.MaxCrates];

    /// <summary><see cref="CrateRules.MaxCrates"/> entries, indexed by crate id.</summary>
    public ReadOnlySpan<Entry> All => _entries;

    /// <summary>The entries that are <see cref="Entry.Known"/>.</summary>
    public int KnownCount { get; private set; }

    /// <summary>The entries that are known and standing: what the minimap and the world draw.</summary>
    public int StandingKnown { get; private set; }

    /// <summary>Bumps whenever any entry's known, standing, kind or position changes (not its visibility): what the drawing watches.</summary>
    public int Version { get; private set; }

    /// <summary>Forgets every crate (leaving the match).</summary>
    public void Clear()
    {
        if (KnownCount > 0) Version++;
        Array.Clear(_entries);
        KnownCount = 0;
        StandingKnown = 0;
    }

    /// <summary>
    /// From a match start: these are known and standing (a resync keeps what the server says the seat knows; a fresh start
    /// passes none). ArgumentException for an id past <see cref="CrateRules.MaxCrates"/> or a kind the set doesn't have.
    /// </summary>
    public void Reset(IReadOnlyList<Crate>? known)
    {
        if (known is not null)   // check the lot before touching anything
            for (int i = 0; i < known.Count; i++)
            {
                if (known[i].Id >= CrateRules.MaxCrates) throw new ArgumentException($"Crate ids run below {CrateRules.MaxCrates}, not {known[i].Id}.", nameof(known));
                if ((byte)known[i].Kind >= CrateRules.Kinds) throw new ArgumentException($"Unknown crate kind {(byte)known[i].Kind}.", nameof(known));
            }
        Clear();
        if (known is null) return;
        for (int i = 0; i < known.Count; i++)
        {
            Crate c = known[i];
            ref Entry e = ref _entries[c.Id];
            if (!e.Known) KnownCount++;      // the same id twice: counted once
            else if (e.Standing) StandingKnown--;
            e.Known = true;
            e.Standing = true;
            e.Visible = false;
            e.Kind = c.Kind;
            e.Position = c.Position;
            StandingKnown++;
            Version++;
        }
    }

    /// <summary>
    /// From the newest snapshot: every row becomes known with its kind, position and whether it stands, and visible; the
    /// rest keep what they had but are out of sight. Allocation-free. The rows' ids are the codec's (below
    /// <see cref="CrateRules.MaxCrates"/>); a snapshot built by hand with one past that is refused.
    /// </summary>
    public void Apply(BattleSnapshot snapshot)
    {
        for (int id = 0; id < _entries.Length; id++) _entries[id].Visible = false;
        foreach (ref readonly CrateView row in snapshot.CrateSpan)
        {
            if (row.Id >= CrateRules.MaxCrates) throw new ArgumentOutOfRangeException(nameof(snapshot), row.Id, "A crate row's id is below the set's size.");
            ref Entry e = ref _entries[row.Id];
            var position = new Vector2(BattleProtocol.FromCm(row.X), BattleProtocol.FromCm(row.Z));
            bool changed = !e.Known || e.Standing != row.Standing || e.Kind != row.Kind || e.Position != position;
            if (!e.Known) KnownCount++;
            else if (e.Standing) StandingKnown--;
            if (row.Standing) StandingKnown++;
            e.Known = true;
            e.Visible = true;
            e.Standing = row.Standing;
            e.Kind = row.Kind;
            e.Position = position;
            if (changed) Version++;
        }
    }

    /// <summary>
    /// From the player's own pickup, without the crate's kind and position (no snapshot row to hand): the crate is known and gone;
    /// one already down stays as it is. A crate never seen keeps no kind or position unless the pickup message gave them
    /// (<see cref="Take(byte, CrateKind, Vector2)"/>); nothing draws a fallen crate, so none is needed. Allocation-free.
    /// </summary>
    public void Take(byte id)
    {
        if (id >= CrateRules.MaxCrates) throw new ArgumentOutOfRangeException(nameof(id), id, "A crate id is below the set's size.");
        ref Entry e = ref _entries[id];
        if (e.Known && !e.Standing) return;
        if (!e.Known) KnownCount++;
        else StandingKnown--;
        e.Known = true;
        e.Standing = false;
        Version++;
    }

    /// <summary>
    /// From the player's own <see cref="CrateMessage"/>, which says what the crate was and where: the crate is known with that kind
    /// and position, and gone — so a crate eaten before any sight pass caught it is known whole, not as a blank. One already down
    /// stays as it is. ArgumentException for a kind the set doesn't have. Allocation-free.
    /// </summary>
    public void Take(byte id, CrateKind kind, Vector2 position)
    {
        if (id >= CrateRules.MaxCrates) throw new ArgumentOutOfRangeException(nameof(id), id, "A crate id is below the set's size.");
        if ((byte)kind >= CrateRules.Kinds) throw new ArgumentException($"Unknown crate kind {(byte)kind}.", nameof(kind));
        ref Entry e = ref _entries[id];
        if (e.Known && !e.Standing) return;
        e.Kind = kind;
        e.Position = position;
        Take(id);
    }
}
