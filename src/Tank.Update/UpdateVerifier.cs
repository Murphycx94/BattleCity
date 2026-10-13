using System.Security.Cryptography;

namespace Tank.Update;

/// <summary>
/// Verifies the manifest signature: ECDSA P-256 over SHA-256 of the exact manifest bytes, DER-encoded
/// (what <c>openssl dgst -sha256 -sign key.pem</c> produces). The public key ships inside the game.
/// </summary>
public sealed class UpdateVerifier
{
    private const int MaxSignatureBytes = 512;
    private readonly string _publicKeyPem;

    public UpdateVerifier(string publicKeyPem) => _publicKeyPem = publicKeyPem;

    public bool IsValid(ReadOnlySpan<byte> manifest, ReadOnlySpan<byte> signature)
    {
        if (signature.Length == 0 || signature.Length > MaxSignatureBytes) return false;
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(_publicKeyPem);
            return key.VerifyData(manifest, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return false; // unusable key or signature: treat as not verified
        }
    }

    /// <summary>Accepts the raw DER signature or its base64 text form (what the feed stores).</summary>
    public static byte[] DecodeSignature(ReadOnlySpan<byte> sigFile)
    {
        string text = System.Text.Encoding.ASCII.GetString(sigFile).Trim();
        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            return sigFile.ToArray();
        }
    }
}
