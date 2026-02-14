using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace PCTimeLimitServer.Infrastructure.Security;

public static class TokenUtility
{
    public static string GenerateToken(int numBytes = 48)
    {
        var bytes = RandomNumberGenerator.GetBytes(numBytes);
        return Base64UrlEncoder.Encode(bytes);
    }

    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
