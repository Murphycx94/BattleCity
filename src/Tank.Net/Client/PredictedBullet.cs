using Tank.Core.World;

namespace Tank.Net.Client;

public struct PredictedBullet
{
    public int SpawnTick;
    public BulletState State;
    public bool Confirmed;     // the server has acknowledged this bullet exists
    public bool LocallyEnded;  // prediction says it hit something; hidden but kept until the server agrees
    public int EndedTick;
}
