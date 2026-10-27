using System.Security.Cryptography;
using System.Text;

namespace Coreloom.Core.Connect;

/// <summary>
/// The cryptography of Coreloom Connect, shared with the phone app (<c>web/app/crypto.js</c>, the noble libraries):
/// P-256 ECDH, HKDF-SHA256, HMAC-SHA256 and AES-256-GCM, all standard and in .NET on Windows and macOS.
/// <para>
/// Pairing: the phone and the computer each make a one-time P-256 key and exchange the public halves. The shared
/// secret, mixed with the pairing secret (the 32 random bytes in the QR code's link, or the six-digit code), gives an
/// encryption key and a MAC key. Only someone who knows the pairing secret can prove the MAC, and an eavesdropper who
/// sees the exchange learns nothing (the ECDH secret never crosses the network). The computer then sends the phone,
/// encrypted, a device key of its own (32 random bytes) that the phone keeps.
/// </para>
/// <para>
/// Sessions: each connection mixes the device key with a nonce from each side into a session key; every request
/// carries an HMAC with an increasing counter and every response is encrypted (AES-GCM, random 12-byte nonce).
/// </para>
/// </summary>
public static class ConnectCrypto
{
    public const int KeySize = 32;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int PublicKeySize = 65;

    private static readonly byte[] PairSalt = Encoding.ASCII.GetBytes("coreloom/pair/v1");
    private static readonly byte[] PairInfo = Encoding.ASCII.GetBytes("coreloom/pair/keys");
    private static readonly byte[] SessionInfo = Encoding.ASCII.GetBytes("coreloom/session/v1");

    public static byte[] RandomBytes(int count) => RandomNumberGenerator.GetBytes(count);

    // ---- Base64url (no padding), as in URLs and JSON ---------------------------------------------------------------

    public static string ToBase64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Null when the text isn't base64url (or standard base64).</summary>
    public static byte[]? FromBase64Url(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 8192)
        {
            return null;
        }

        string s = text.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch
        {
            2 => s + "==",
            3 => s + "=",
            0 => s,
            _ => string.Empty,
        };

        if (s.Length == 0)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(s);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // ---- Keys ------------------------------------------------------------------------------------------------------

    /// <summary>A one-time P-256 key for one pairing.</summary>
    public sealed class EphemeralKey : IDisposable
    {
        private readonly ECDiffieHellman _key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        public EphemeralKey()
        {
            var parameters = _key.ExportParameters(includePrivateParameters: false);
            PublicKey = new byte[PublicKeySize];
            PublicKey[0] = 0x04;
            Pad(parameters.Q.X!).CopyTo(PublicKey, 1);
            Pad(parameters.Q.Y!).CopyTo(PublicKey, 33);
        }

        /// <summary>The public half, uncompressed (0x04 ‖ X ‖ Y, 65 bytes).</summary>
        public byte[] PublicKey { get; }

        /// <summary>The X coordinate of the shared point (32 bytes), or null when the other key isn't a P-256 point.</summary>
        public byte[]? SharedSecret(byte[]? otherPublicKey)
        {
            if (otherPublicKey is not { Length: PublicKeySize } || otherPublicKey[0] != 0x04)
            {
                return null;
            }

            try
            {
                using var other = ECDiffieHellman.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = otherPublicKey[1..33], Y = otherPublicKey[33..65] },
                });
                return _key.DeriveRawSecretAgreement(other.PublicKey);
            }
            catch (Exception e) when (e is CryptographicException or PlatformNotSupportedException)
            {
                return null; // not on the curve (Windows' CNG says so with PlatformNotSupportedException)
            }
        }

        public void Dispose() => _key.Dispose();

        private static byte[] Pad(byte[] coordinate)
        {
            if (coordinate.Length == 32)
            {
                return coordinate;
            }

            var padded = new byte[32];
            coordinate.CopyTo(padded, 32 - coordinate.Length);
            return padded;
        }
    }

    /// <summary>The pairing's encryption and MAC keys (32 bytes each).</summary>
    public static (byte[] Encrypt, byte[] Mac) PairingKeys(byte[] sharedSecret, byte[] pairingSecret, byte[] phonePublicKey, byte[] pcPublicKey)
    {
        byte[] salt = SHA256.HashData([.. PairSalt, .. pairingSecret]);
        byte[] info = [.. PairInfo, .. phonePublicKey, .. pcPublicKey];
        byte[] keys = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 64, salt, info);
        return (keys[..32], keys[32..]);
    }

    /// <summary>A session's encryption and MAC keys from the device key and a nonce from each side.</summary>
    public static (byte[] Encrypt, byte[] Mac) SessionKeys(byte[] deviceKey, byte[] phoneNonce, byte[] pcNonce)
    {
        byte[] keys = HKDF.DeriveKey(HashAlgorithmName.SHA256, deviceKey, 64, [.. phoneNonce, .. pcNonce], SessionInfo);
        return (keys[..32], keys[32..]);
    }

    // ---- MACs and encryption -------------------------------------------------------------------------------------

    public static byte[] Mac(byte[] key, string text) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text));

    public static byte[] Mac(byte[] key, ReadOnlySpan<byte> data) => HMACSHA256.HashData(key, data);

    public static bool Verify(byte[] expected, byte[]? actual) =>
        actual is not null && actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(expected, actual);

    /// <summary>AES-256-GCM: nonce (12) ‖ ciphertext ‖ tag (16).</summary>
    public static byte[] Seal(byte[] key, ReadOnlySpan<byte> plaintext, string associatedData)
    {
        var sealedBytes = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = sealedBytes.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, sealedBytes.AsSpan(NonceSize, plaintext.Length), sealedBytes.AsSpan(NonceSize + plaintext.Length), Encoding.UTF8.GetBytes(associatedData));
        return sealedBytes;
    }

    /// <summary>The plaintext, or null when the data wasn't sealed with this key and associated data.</summary>
    public static byte[]? Open(byte[] key, ReadOnlySpan<byte> sealedBytes, string associatedData)
    {
        if (sealedBytes.Length < NonceSize + TagSize)
        {
            return null;
        }

        int length = sealedBytes.Length - NonceSize - TagSize;
        var plaintext = new byte[length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(sealedBytes[..NonceSize], sealedBytes.Slice(NonceSize, length), sealedBytes[(NonceSize + length)..], plaintext, Encoding.UTF8.GetBytes(associatedData));
            return plaintext;
        }
        catch (AuthenticationTagMismatchException)
        {
            return null;
        }
    }
}
