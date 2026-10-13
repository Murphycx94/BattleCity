namespace Tank.Update;

/// <summary>Where official builds look for updates and the key their manifests must be signed with.</summary>
public static class UpdateDefaults
{
    /// <summary>Public repo that only hosts release packages and the signed manifest (source stays private).</summary>
    public const string FeedUrl = "https://raw.githubusercontent.com/Murphycx94/BattleCity-updates/main/stable/";

    /// <summary>Release page shown when an install cannot update itself (e.g. read-only location).</summary>
    public const string ReleasesUrl = "https://github.com/Murphycx94/BattleCity-updates/releases/latest";

    /// <summary>
    /// ECDSA P-256 public key. The private half lives only on the publisher's machine
    /// (~/.config/tank3d/update_signing_key.pem) and in the CI secret TANK_UPDATE_SIGNING_KEY.
    /// </summary>
    public const string PublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAECMjZhnD9yDgTbu3NhBlrYgtfmZJw
Ny+UeXSHDvjqhVqCoIfSjaSQ7/NeRSDJQFLBLh4Qpp6aCKt5Ngbhh7jVsw==
-----END PUBLIC KEY-----
""";

    /// <summary>Override for testing a local feed: TANK_UPDATE_FEED=http(s)://host/path/</summary>
    public const string FeedOverrideVariable = "TANK_UPDATE_FEED";
}
