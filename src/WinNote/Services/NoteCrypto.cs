using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WinNote.Models;

namespace WinNote.Services;

public static class NoteCrypto
{
    private const int Iterations = 140000;

    public static (string Salt, string Hash) CreatePasscode(string passcode)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Derive(passcode, salt, 32);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool Verify(string passcode, string? saltBase64, string? hashBase64)
    {
        if (string.IsNullOrEmpty(saltBase64) || string.IsNullOrEmpty(hashBase64))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(saltBase64);
            var expected = Convert.FromBase64String(hashBase64);
            var actual = Derive(passcode, salt, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }

    public static string Encrypt(NoteLockContent content, string passcode, string saltBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var key = Derive(passcode, salt, 32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = JsonSerializer.SerializeToUtf8Bytes(content, JsonDefaults.Options);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];

        using (var aes = new AesGcm(key, 16))
        {
            aes.Encrypt(nonce, plain, cipher, tag);
        }

        var payload = new LockedPayload
        {
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Data = Convert.ToBase64String(cipher),
        };

        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload, JsonDefaults.Options));
    }

    public static NoteLockContent Decrypt(string payloadBase64, string passcode, string saltBase64)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<LockedPayload>(
                Convert.FromBase64String(payloadBase64), JsonDefaults.Options)
                ?? throw new InvalidOperationException("锁定数据损坏。");

            var salt = Convert.FromBase64String(saltBase64);
            var key = Derive(passcode, salt, 32);
            var nonce = Convert.FromBase64String(payload.Nonce);
            var tag = Convert.FromBase64String(payload.Tag);
            var cipher = Convert.FromBase64String(payload.Data);
            var plain = new byte[cipher.Length];

            using (var aes = new AesGcm(key, 16))
            {
                aes.Decrypt(nonce, cipher, tag, plain);
            }

            return JsonSerializer.Deserialize<NoteLockContent>(plain, JsonDefaults.Options)
                   ?? throw new InvalidOperationException("锁定数据为空。");
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("密码不正确。");
        }
    }

    private static byte[] Derive(string passcode, byte[] salt, int length)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passcode),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            length);
    }

    public sealed class LockedPayload
    {
        public string Nonce { get; set; } = "";
        public string Tag { get; set; } = "";
        public string Data { get; set; } = "";
    }
}

public sealed class NoteLockContent
{
    public string Title { get; set; } = "";
    public string Html { get; set; } = "";
    public string Text { get; set; } = "";
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string[] Images { get; set; } = Array.Empty<string>();
    public string[] Files { get; set; } = Array.Empty<string>();
}
