using System.Security.Cryptography;

namespace Shopkeeper.Api.Infrastructure;

public static class ETagUtility
{
    public static string CreateWeak(byte[] content) => $"W/\"{Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()}\"";

    public static string CreateStrong(byte[] content) => $"\"{Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()}\"";
}
