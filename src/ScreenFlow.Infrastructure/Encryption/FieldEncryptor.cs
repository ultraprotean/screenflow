using System.Security.Cryptography;
using System.Text;

namespace ScreenFlow.Infrastructure.Encryption;

/// <summary>
/// AES-256-GCM field encryption (CONTRACT.md §3). Output is nonce ‖ ciphertext ‖ tag.
/// A random nonce per value means equal plaintexts encrypt differently, so encrypted columns
/// cannot be searched or joined on; that is the trade-off for not leaking equality.
/// </summary>
public sealed class FieldEncryptor
{
    private const int NonceSize = 12; // 96 bits, the GCM standard
    private const int TagSize = 16;   // 128 bits
    private readonly byte[] _key;

    public FieldEncryptor(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 32)
        {
            throw new ArgumentException("The encryption key must be 32 bytes (AES-256).", nameof(key));
        }
        _key = key;
    }

    /// <summary>Parses the base64 key from configuration (Encryption:Key).</summary>
    public static FieldEncryptor FromBase64(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            throw new InvalidOperationException(
                "Encryption:Key is not configured. Set it with user-secrets locally (see README) or from a secrets manager in a deployment.");
        }
        return new FieldEncryptor(Convert.FromBase64String(base64Key.Trim()));
    }

    public byte[] Encrypt(string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[NonceSize + plain.Length + TagSize];
        var nonce = output.AsSpan(0, NonceSize);
        var cipher = output.AsSpan(NonceSize, plain.Length);
        var tag = output.AsSpan(NonceSize + plain.Length, TagSize);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        return output;
    }

    public string Decrypt(byte[] stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var cipherLength = stored.Length - NonceSize - TagSize;
        var plain = new byte[cipherLength];

        using var aes = new AesGcm(_key, TagSize);
        // Throws AuthenticationTagMismatchException if the bytes were altered or the key is wrong.
        aes.Decrypt(
            stored.AsSpan(0, NonceSize),
            stored.AsSpan(NonceSize, cipherLength),
            stored.AsSpan(NonceSize + cipherLength, TagSize),
            plain);
        return Encoding.UTF8.GetString(plain);
    }
}
