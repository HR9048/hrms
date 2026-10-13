using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace FileStorageServer.Security;

public static class SecureUrlSigner
{
    public static string Sign(string fileId, long expiresAtUnixSeconds, string signingKey, bool download = false, string? name = null)
    {
        var payload = $"{fileId}:{expiresAtUnixSeconds}:{(download ? 1 : 0)}:{name}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        return WebEncoders.Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    public static bool Validate(string fileId, long expiresAtUnixSeconds, string? providedSignature, string signingKey,
        bool download = false, string? name = null)
    {
        if (string.IsNullOrEmpty(providedSignature)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresAtUnixSeconds) return false;

        var expected = Sign(fileId, expiresAtUnixSeconds, signingKey, download, name);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(providedSignature);

        return expectedBytes.Length == providedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}