using Tank.Net.Battle;

namespace Tank.Client.World;

/// <summary>
/// Whether a crate leaving the world goes with a pickup flash (plan deviation 40). The design flashes a crate "when it is
/// eaten", and a late flash reads as "someone just ate this here": so only a crate the player saw go — in sight the frame
/// before (it was in the previous snapshot's rows) — or ate. A spot re-seen empty after a while out of sight, a crate a
/// resync no longer knows and the lot cleared on leaving the match go without one.
/// </summary>
public static class CrateFlash
{
    /// <param name="wasVisible">The crate was in sight the frame before (<see cref="CrateKnowledge.Entry.Visible"/> then).</param>
    /// <param name="ownPickup">The player's own pickup message named it this frame.</param>
    /// <param name="now">What the knowledge says of it now.</param>
    public static bool FlashesOnRemove(bool wasVisible, bool ownPickup, in CrateKnowledge.Entry now) =>
        now.Known && !now.Standing && (wasVisible || ownPickup);
}
