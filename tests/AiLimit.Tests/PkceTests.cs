using AiLimit.Core.Providers;
using Xunit;

namespace AiLimit.Tests;

public class PkceTests
{
    [Fact]
    public void Create_ProducesVerifierAndS256Challenge()
    {
        var pair = Pkce.Create();

        Assert.InRange(pair.Verifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9._~-]+$", pair.Verifier);
        Assert.DoesNotContain("=", pair.Verifier);
        Assert.DoesNotContain("=", pair.Challenge);
        Assert.DoesNotContain("+", pair.Challenge);
        Assert.DoesNotContain("/", pair.Challenge);

        var expected = Pkce.ComputeChallenge(pair.Verifier);
        Assert.Equal(expected, pair.Challenge);
    }

    [Fact]
    public void ComputeChallenge_IsStableForKnownVector()
    {
        // RFC 7636 Appendix B test vector
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.ComputeChallenge(verifier));
    }
}
