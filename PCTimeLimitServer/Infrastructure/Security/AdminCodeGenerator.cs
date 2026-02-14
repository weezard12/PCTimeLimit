using System.Security.Cryptography;

namespace PCTimeLimitServer.Infrastructure.Security;

public static class AdminCodeGenerator
{
    private static readonly char[] Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

    public static string Generate(int length = 6)
    {
        Span<char> buffer = stackalloc char[length];
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);

        for (var i = 0; i < length; i++)
        {
            buffer[i] = Alphabet[bytes[i] % Alphabet.Length];
        }

        return buffer.ToString();
    }
}
