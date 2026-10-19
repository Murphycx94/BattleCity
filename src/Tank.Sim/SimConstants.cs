namespace Tank.Sim;

public static class SimConstants
{
    public const int TickRate = 30;
    public const float Dt = 1f / TickRate;
    public const float Gravity = 9.81f;

    /// <summary>Teams are numbered 0 … MaxTeams − 1: room for every tank of a free-for-all to be its own team.</summary>
    public const int MaxTeams = 16;
}
