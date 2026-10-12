namespace Tank.Net.Protocol;

/// <summary>Raised for malformed or truncated packets. Callers drop the packet; it never crashes the game.</summary>
public sealed class ProtocolException : Exception
{
    public ProtocolException(string message) : base(message) { }
}
