using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Framework.Mvc.Security
{
    /// <summary>
    /// Authenticated encryption with a shared secret (used for the SSO token between LT2 sites):
    /// AES-256-CBC with a random IV, then HMAC-SHA256 over the version, IV and ciphertext (encrypt-then-MAC).
    /// The MAC is checked before anything is decrypted, so a token cannot be altered or used as a padding oracle.
    /// Token layout (Base64): version (1 byte) | IV (16) | ciphertext | MAC (32).
    /// </summary>
    public static class SecurityUtil
    {
        private const byte Version = 2;
        private const int IvSize = 16;
        private const int MacSize = 32;
        private const int KeyIterations = 20000;
        private static readonly byte[] KeySalt = Encoding.ASCII.GetBytes("LT2.SecurityUtil.v2");
        private static readonly ConcurrentDictionary<string, Tuple<byte[], byte[]>> KeyCache = new ConcurrentDictionary<string, Tuple<byte[], byte[]>>();

        public static string Encrypt(string plainText, string sharedSecret)
        {
            if (string.IsNullOrEmpty(plainText))
                throw new ArgumentNullException("plainText");
            var keys = GetKeys(sharedSecret);

            using (var aes = new AesCryptoServiceProvider { KeySize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
            {
                aes.Key = keys.Item1;
                aes.GenerateIV();

                byte[] cipher;
                using (var encryptor = aes.CreateEncryptor())
                {
                    var plain = Encoding.UTF8.GetBytes(plainText);
                    cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                }

                using (var ms = new MemoryStream())
                {
                    ms.WriteByte(Version);
                    ms.Write(aes.IV, 0, IvSize);
                    ms.Write(cipher, 0, cipher.Length);

                    byte[] mac;
                    using (var hmac = new HMACSHA256(keys.Item2))
                    {
                        mac = hmac.ComputeHash(ms.ToArray());
                    }

                    ms.Write(mac, 0, MacSize);
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        /// <summary>
        /// Decrypts a token made by <see cref="Encrypt"/>. Throws <see cref="CryptographicException"/> when the token was
        /// altered, made with another secret, or is in an unknown format.
        /// </summary>
        public static string Decrypt(string cipherText, string sharedSecret)
        {
            if (string.IsNullOrEmpty(cipherText))
                throw new ArgumentNullException("cipherText");
            var keys = GetKeys(sharedSecret);

            var data = Convert.FromBase64String(cipherText);
            if (data.Length < 1 + IvSize + 16 + MacSize || data[0] != Version)
            {
                throw new CryptographicException("Invalid token");
            }

            var signedLength = data.Length - MacSize;
            byte[] expected;
            using (var hmac = new HMACSHA256(keys.Item2))
            {
                expected = hmac.ComputeHash(data, 0, signedLength);
            }

            var diff = 0;
            for (var i = 0; i < MacSize; i++)
            {
                diff |= expected[i] ^ data[signedLength + i];
            }

            if (diff != 0)
            {
                throw new CryptographicException("Invalid token");
            }

            using (var aes = new AesCryptoServiceProvider { KeySize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
            {
                aes.Key = keys.Item1;
                var iv = new byte[IvSize];
                Buffer.BlockCopy(data, 1, iv, 0, IvSize);
                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor())
                {
                    var plain = decryptor.TransformFinalBlock(data, 1 + IvSize, signedLength - 1 - IvSize);
                    return Encoding.UTF8.GetString(plain);
                }
            }
        }

        /// <summary>
        /// Separate encryption and MAC keys derived from the shared secret (which should be at least 32 random characters).
        /// </summary>
        private static Tuple<byte[], byte[]> GetKeys(string sharedSecret)
        {
            if (string.IsNullOrEmpty(sharedSecret) || sharedSecret.Length < 32)
                throw new ArgumentException("The shared secret must be at least 32 characters long.", "sharedSecret");

            return KeyCache.GetOrAdd(sharedSecret, secret =>
            {
                using (var kdf = new Rfc2898DeriveBytes(secret, KeySalt, KeyIterations))
                {
                    var bytes = kdf.GetBytes(64);
                    var encKey = new byte[32];
                    var macKey = new byte[32];
                    Buffer.BlockCopy(bytes, 0, encKey, 0, 32);
                    Buffer.BlockCopy(bytes, 32, macKey, 0, 32);
                    return Tuple.Create(encKey, macKey);
                }
            });
        }
    }
}
