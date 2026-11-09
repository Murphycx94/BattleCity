using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// Who a dead player may watch (stage 6 design §2.3, §5.2): live team-mates in a mode with them; in duos, once none is
/// left, every live tank; everyone alive in a free-for-all or royale. Engine-free; allocates nothing (the caller's list
/// keeps its capacity).
/// </summary>
public static class SpectateCandidates
{
    /// <summary>
    /// Clears <paramref name="into"/> and adds the ids of the tanks the player may watch, in the order of
    /// <paramref name="others"/> (the interpolator's, so stepping through them is stable).
    /// </summary>
    public static void Fill(BattleMode mode, byte ownTeam, IReadOnlyList<RemoteTank> others, List<int> into)
    {
        into.Clear();
        bool matesOnly = !MayRoam(mode, ownTeam, others);   // a team mode always; duos while a team-mate lives
        for (int i = 0; i < others.Count; i++)
        {
            BattleEntity entity = others[i].Entity;
            if (entity.Has(TankFlags.Destroyed) || (matesOnly && entity.Team != ownTeam)) continue;
            into.Add(entity.Id);
        }
    }

    /// <summary>Whether the camera may go free (F): no team-mates in the mode, or (duos) none of them alive.</summary>
    public static bool MayRoam(BattleMode mode, byte ownTeam, IReadOnlyList<RemoteTank> others) =>
        !ModeRules.HasAllies(mode) || (ModeRules.IsDuos(mode) && !AnyMateAlive(ownTeam, others));

    private static bool AnyMateAlive(byte ownTeam, IReadOnlyList<RemoteTank> others)
    {
        for (int i = 0; i < others.Count; i++)
        {
            BattleEntity entity = others[i].Entity;
            if (entity.Team == ownTeam && !entity.Has(TankFlags.Destroyed)) return true;
        }
        return false;
    }
}
