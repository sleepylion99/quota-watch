using System.Security.Cryptography;
using System.Text;

namespace AiLimit.Core.Providers;

public readonly record struct PkcePair(string Verifier, string Challenge);

public static class Pkce
{
    public static PkcePair Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64Url(bytes); // 43 chars
        return new PkcePair(verifier, ComputeChallenge(verifier));
    }

    public static string ComputeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
        return Base64Url(hash);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
