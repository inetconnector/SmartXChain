using System.Security.Cryptography;

namespace SmartXChain.Utils;

/// <summary>
///     A reusable class for secure communication between peers using Diffie-Hellman for key exchange,
///     AES for encryption, and HMAC for integrity verification.
/// </summary>
public class SecurePeer
{
    private readonly ECDiffieHellmanCng _diffieHellman;
    private byte[] _sharedKey;

    public SecurePeer()
    {
        _diffieHellman = new ECDiffieHellmanCng
        {
            KeyDerivationFunction = ECDiffieHellmanKeyDerivationFunction.Hash,
            HashAlgorithm = CngAlgorithm.Sha256
        };
    }

    /// <summary>
    ///     Returns the public key for key exchange.
    /// </summary>
    /// <returns>Public key as byte array</returns>
    public byte[] GetPublicKey()
    {
        return _diffieHellman.PublicKey.ToByteArray();
    }

    /// <summary>
    ///     Computes the shared secret key using the other peer's public key.
    /// </summary>
    /// <param name="otherPublicKey">The public key of the other peer</param>
    public void ComputeSharedKey(byte[] otherPublicKey)
    {
        _sharedKey = _diffieHellman.DeriveKeyMaterial(CngKey.Import(otherPublicKey, CngKeyBlobFormat.EccPublicBlob));
    }

    /// <summary>
    ///     Encrypts a message and generates an HMAC for integrity.
    /// </summary>
    /// <param name="message">The plaintext message to encrypt</param>
    /// <returns>Tuple containing the encrypted message, IV, and HMAC</returns>
    public (byte[] EncryptedMessage, byte[] IV, byte[] HMAC) EncryptAndSign(string message)
    {
        if (_sharedKey == null) throw new InvalidOperationException("Shared key has not been computed.");

        var iv = GenerateRandomBytes(16);
        byte[] encryptedMessage;

        using (var aes = Aes.Create())
        {
            aes.Key = _sharedKey;
            aes.IV = iv;

            using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
            using (var msEncrypt = new MemoryStream())
            {
                using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                using (var swEncrypt = new StreamWriter(csEncrypt))
                {
                    swEncrypt.Write(message);
                }

                encryptedMessage = msEncrypt.ToArray();
            }
        }

        var hmac = GenerateHMAC(encryptedMessage, _sharedKey);
        return (encryptedMessage, iv, hmac);
    }

    /// <summary>
    ///     Decrypts a message and verifies its HMAC.
    /// </summary>
    /// <param name="encryptedMessage">The encrypted message</param>
    /// <param name="iv">The initialization vector (IV) used for encryption</param>
    /// <param name="hmac">The HMAC for integrity verification</param>
    /// <returns>The decrypted plaintext message</returns>
    public string DecryptAndVerify(byte[] encryptedMessage, byte[] iv, byte[] hmac)
    {
        if (_sharedKey == null) throw new InvalidOperationException("Shared key has not been computed.");

        if (!ValidateHMAC(encryptedMessage, _sharedKey, hmac))
            throw new CryptographicException("HMAC verification failed. The message may have been tampered with.");

        using (var aes = Aes.Create())
        {
            aes.Key = _sharedKey;
            aes.IV = iv;

            using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
            using (var msDecrypt = new MemoryStream(encryptedMessage))
            using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
            using (var srDecrypt = new StreamReader(csDecrypt))
            {
                return srDecrypt.ReadToEnd();
            }
        }
    }

    /// <summary>
    ///     Generates an HMAC for the given data using the provided key.
    /// </summary>
    /// <param name="data">The data to protect</param>
    /// <param name="key">The secret key</param>
    /// <returns>The HMAC as a byte array</returns>
    private static byte[] GenerateHMAC(byte[] data, byte[] key)
    {
        using (var hmac = new HMACSHA256(key))
        {
            return hmac.ComputeHash(data);
        }
    }

    /// <summary>
    ///     Validates the HMAC of the given data.
    /// </summary>
    /// <param name="data">The data to verify</param>
    /// <param name="key">The secret key</param>
    /// <param name="hmacToValidate">The HMAC to validate against</param>
    /// <returns>True if the HMAC is valid, otherwise false</returns>
    private static bool ValidateHMAC(byte[] data, byte[] key, byte[] hmacToValidate)
    {
        var computedHmac = GenerateHMAC(data, key);
        return CryptographicEquals(computedHmac, hmacToValidate);
    }

    /// <summary>
    ///     Performs a secure comparison of two byte arrays.
    /// </summary>
    /// <param name="a">First byte array</param>
    /// <param name="b">Second byte array</param>
    /// <returns>True if both arrays are equal, otherwise false</returns>
    private static bool CryptographicEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;

        var result = true;
        for (var i = 0; i < a.Length; i++) result &= a[i] == b[i];
        return result;
    }

    /// <summary>
    ///     Generates a random byte array of the specified length.
    /// </summary>
    /// <param name="length">The length of the array</param>
    /// <returns>A byte array filled with random values</returns>
    private static byte[] GenerateRandomBytes(int length)
    {
        var randomBytes = new byte[length];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }

        return randomBytes;
    }

    /// <summary>
    /// Example usage of the SecurePeer class.
    /// </summary>
    public static class SecurePeerExample
    {
        public static void Run()
        {
            // Create two peers
            var alice = new SecurePeer();
            var bob = new SecurePeer();

            // Exchange public keys
            byte[] alicePublicKey = alice.GetPublicKey();
            byte[] bobPublicKey = bob.GetPublicKey();

            // Compute shared secret keys
            alice.ComputeSharedKey(bobPublicKey);
            bob.ComputeSharedKey(alicePublicKey);

            // Alice encrypts and sends a message
            string originalMessage = "Secret Message";
            var (encryptedMessage, iv, hmac) = alice.EncryptAndSign(originalMessage);

            // Bob decrypts and verifies the message
            string decryptedMessage = bob.DecryptAndVerify(encryptedMessage, iv, hmac);

            Console.WriteLine($"Original Message: {originalMessage}");
            Console.WriteLine($"Decrypted Message: {decryptedMessage}");
        }
    } 

}