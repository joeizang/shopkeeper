using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Tests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void HashAndVerify_RoundTrips()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.HashPassword("P@ssword123");

        var ok = hasher.VerifyPassword("P@ssword123", hash);
        var fail = hasher.VerifyPassword("wrong", hash);

        Assert.True(ok);
        Assert.False(fail);
    }
}
