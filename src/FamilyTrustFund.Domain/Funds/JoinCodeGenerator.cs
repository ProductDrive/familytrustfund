using System.Security.Cryptography;

namespace FamilyTrustFund.Domain.Funds;

/// <summary>
/// Generates human-friendly, unambiguous fund join codes.
/// </summary>
public static class JoinCodeGenerator
{
    // Exclude characters that are easy to confuse: 0/O, 1/I/L.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int DefaultLength = 8;

    public static string Generate(int length = DefaultLength)
    {
        if (length < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Join code length must be at least 4.");
        }

        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);

        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }

        return new string(chars);
    }
}
