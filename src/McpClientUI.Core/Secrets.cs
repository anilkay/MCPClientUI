using System.Security.Cryptography;
using System.Text;

namespace McpClientUI;

/// <summary>
/// Encrypts small secrets (headers, environment variables) for the current user: DPAPI on
/// Windows; on macOS and Linux AES-GCM with a random key kept in a file only the user can read.
/// </summary>
public static class Secrets
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("McpClientUI headers");
    private static readonly string KeyPath = Path.Combine(AppSettings.Folder, "secret.key");
    private const int NonceSize = 12, TagSize = 16;

    public static byte[] Protect(byte[] plain)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];
        byte[] cipher = new byte[plain.Length];
        using (var aes = new AesGcm(LoadOrCreateKey(), TagSize))
            aes.Encrypt(nonce, plain, cipher, tag, Entropy);
        return [.. nonce, .. tag, .. cipher];
    }

    public static byte[] Unprotect(byte[] data)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);

        if (data.Length < NonceSize + TagSize) throw new CryptographicException("The secrets file is too short.");
        var nonce = data.AsSpan(0, NonceSize);
        var tag = data.AsSpan(NonceSize, TagSize);
        var cipher = data.AsSpan(NonceSize + TagSize);
        byte[] plain = new byte[cipher.Length];
        using var aes = new AesGcm(LoadOrCreateKey(), TagSize);
        aes.Decrypt(nonce, cipher, tag, plain, Entropy);
        return plain;
    }

    /// <summary>Writes a file that only the current user can read (0600 on macOS and Linux).</summary>
    public static void WritePrivateFile(string path, byte[] bytes)
    {
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(path, options);
        stream.Write(bytes);
    }

    private static byte[] LoadOrCreateKey()
    {
        if (File.Exists(KeyPath))
        {
            byte[] existing = File.ReadAllBytes(KeyPath);
            if (existing.Length == 32) return existing;
        }
        AppSettings.EnsureFolder();
        byte[] key = RandomNumberGenerator.GetBytes(32);
        WritePrivateFile(KeyPath, key);
        return key;
    }
}
