using System.Security.Cryptography;
using System.Text;

namespace AiLimit.Core.Providers;

internal static class AccountKeyHash
{
    public static string? FromSecret(string providerId, string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret.Trim()));
        return $"{providerId}:token-sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }
}
