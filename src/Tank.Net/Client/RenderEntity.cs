using Tank.Net.Protocol;

namespace Tank.Net.Client;

/// <summary>An entity ready to draw: network data plus a smoothed position in sub units.</summary>
public readonly record struct RenderEntity(NetEntity Entity, double X, double Z, bool Extrapolated);
