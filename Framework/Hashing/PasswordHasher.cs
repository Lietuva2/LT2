using System;
using System.Security.Cryptography;
using System.Text;

namespace Framework.Hashing
{
    /// <summary>
    /// Salted PBKDF2 password hashes. Stored formats:
    /// "pbkdf2${iterations}${salt}${hash}" – current;
    /// "pbkdf2md5${iterations}${salt}${hash}" – a previous-format hash wrapped in PBKDF2 (see <see cref="WrapLegacyHash"/>);
    /// 32 hex characters – previous format, replaced with the current one on the next login.
    /// </summary>
    public static class PasswordHasher
    {
        private const int Iterations = 100000;
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const string Prefix = "pbkdf2$";
        private const string WrappedLegacyPrefix = "pbkdf2md5$";

        public static string Hash(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentNullException("password");
            }

            return Prefix + Derive(password);
        }

        /// <summary>
        /// Wraps a previous-format hash in PBKDF2. Returns other values unchanged.
        /// </summary>
        public static string WrapLegacyHash(string storedHash)
        {
            return IsUnwrappedLegacy(storedHash) ? WrappedLegacyPrefix + Derive(storedHash) : storedHash;
        }

        public static bool NeedsUpgrade(string storedHash)
        {
            return !string.IsNullOrEmpty(storedHash) && !storedHash.StartsWith(Prefix, StringComparison.Ordinal);
        }

        public static bool Verify(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            {
                return false;
            }

            if (storedHash.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return Check(password, storedHash.Substring(Prefix.Length));
            }

            if (storedHash.StartsWith(WrappedLegacyPrefix, StringComparison.Ordinal))
            {
                return Check(password.ComputeHash(), storedHash.Substring(WrappedLegacyPrefix.Length));
            }

            return IsUnwrappedLegacy(storedHash) &&
                   SlowEquals(Encoding.ASCII.GetBytes(password.ComputeHash()), Encoding.ASCII.GetBytes(storedHash.ToLowerInvariant()));
        }

        private static bool IsUnwrappedLegacy(string storedHash)
        {
            if (storedHash == null || storedHash.Length != 32)
            {
                return false;
            }

            foreach (var c in storedHash)
            {
                if (!Uri.IsHexDigit(c))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Derive(string secret)
        {
            var salt = new byte[SaltSize];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(salt);
            }

            using (var pbkdf2 = new Rfc2898DeriveBytes(secret, salt, Iterations))
            {
                return Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(pbkdf2.GetBytes(HashSize));
            }
        }

        private static bool Check(string secret, string body)
        {
            var parts = body.Split('$');
            int iterations;
            if (parts.Length != 3 || !int.TryParse(parts[0], out iterations) || iterations < 1)
            {
                return false;
            }

            byte[] salt, expected;
            try
            {
                salt = Convert.FromBase64String(parts[1]);
                expected = Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                return false;
            }

            using (var pbkdf2 = new Rfc2898DeriveBytes(secret, salt, iterations))
            {
                return SlowEquals(pbkdf2.GetBytes(expected.Length), expected);
            }
        }

        /// <summary>
        /// Compares in constant time, so timing does not reveal how much of a hash matched.
        /// </summary>
        public static bool SlowEquals(byte[] a, byte[] b)
        {
            var diff = (uint)a.Length ^ (uint)b.Length;
            for (var i = 0; i < a.Length && i < b.Length; i++)
            {
                diff |= (uint)(a[i] ^ b[i]);
            }

            return diff == 0;
        }
    }
}
