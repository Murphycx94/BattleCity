using Tank.Core.Math;
using Tank.Core.World;

namespace Tank.Core.Tests;

/// <summary>Builders for small hand-made worlds.</summary>
internal static class TestWorld
{
    /// <summary>An empty map of <paramref name="halvesW"/> x <paramref name="halvesH"/> with one player spawn in the bottom-left.</summary>
    public static string EmptyMapText(int halvesW, int halvesH)
    {
        var rows = Enumerable.Range(0, halvesH).Select(_ => new string('.', halvesW).ToCharArray()).ToArray();
        rows[halvesH - 2][0] = 'P';
        return string.Join('\n', rows.Select(r => new string(r)));
    }

    public static Simulation Create(string mapText, GameMode mode = GameMode.Deathmatch, uint seed = 1, int enemyTotal = 0)
    {
        var def = MapParser.Parse("test", mapText);
        return new Simulation(new MatchConfig(mode, def, seed, EnemyTotal: enemyTotal));
    }

    public static Simulation CreateEmpty(int halvesW = 16, int halvesH = 16, GameMode mode = GameMode.Deathmatch) =>
        Create(EmptyMapText(halvesW, halvesH), mode);

    public static int AddTank(Simulation sim, TankState tank)
    {
        var s = sim.State;
        tank.Id = s.AllocateId();
        if (tank.Hp == 0) tank.Hp = 1;
        s.Tanks[s.TankCount++] = tank;
        return tank.Id;
    }

    public static TankState Player(int slot, int x, int z, Dir dir = Dir.Up, byte team = 0) => new()
    {
        Kind = TankKind.Player,
        Slot = (byte)slot,
        Team = team,
        X = x,
        Z = z,
        Dir = dir,
        Hp = 1,
    };

    public static TankState Enemy(int x, int z, TankKind kind = TankKind.Basic, Dir dir = Dir.Down) => new()
    {
        Kind = kind,
        Slot = Teams.NoSlot,
        Team = Teams.Enemy,
        X = x,
        Z = z,
        Dir = dir,
        Hp = kind == TankKind.Armor ? (byte)4 : (byte)1,
    };

    public static ref TankState Tank(Simulation sim, int id) => ref sim.State.Tanks[sim.State.FindTankIndex(id)];

    public static InputCmd[] Inputs(int tick, int slot = 0, byte move = 0, bool fire = false, byte viewDelta = 0)
    {
        var inputs = new InputCmd[GameState.MaxPlayers];
        for (int i = 0; i < inputs.Length; i++) inputs[i] = InputCmd.Idle(tick);
        inputs[slot] = new InputCmd(tick, move, fire, viewDelta);
        return inputs;
    }

    public static void StepIdle(Simulation sim, int ticks = 1)
    {
        for (int i = 0; i < ticks; i++) sim.Step(Inputs(sim.State.Tick + 1));
    }

    public static byte Move(Dir d) => InputCmd.Encode(d);
}
