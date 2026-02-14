namespace FalkInstaller.Engine.Protocol.Transport;

using System.Security.Cryptography;

public static class PipeSecurityValidator
{
    public const int NonceSize = 32;
    public const int HmacSize = 32; // SHA-256 output

    public static byte[] GenerateNonce()
    {
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        return nonce;
    }

    public static byte[] ComputeHmac(byte[] sharedSecret, byte[] nonce)
    {
        using var hmac = new HMACSHA256(sharedSecret);
        return hmac.ComputeHash(nonce);
    }

    public static bool ValidateHmac(byte[] sharedSecret, byte[] nonce, byte[] receivedHmac)
    {
        var expected = ComputeHmac(sharedSecret, nonce);
        return CryptographicOperations.FixedTimeEquals(expected, receivedHmac);
    }
}
