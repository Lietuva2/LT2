using System;
using System.Security.Cryptography;
using System.Text;

namespace Framework.Hashing
{
    /// <summary>
    /// Provides password generation functionality.
    /// </summary>
    public class PasswordGenerator : IPasswordGenerator
    {
        /// <summary>
        /// Lower case password characters.
        /// </summary>
        public const string PasswordCharsLower = "abcdefgijkmnopqrstwxyz";

        /// <summary>
        /// Upper case password characters.
        /// </summary>
        public const string PasswordCharsUpper = "ABCDEFGHJKLMNPQRSTWXYZ";

        /// <summary>
        /// Numeric password characters.
        /// </summary>
        public const string PasswordCharsNumbers = "0123456789";

        /// <summary>
        /// Special password characters.
        /// </summary>
        public const string PasswordCharsSpecial = "*$-+?_&=!%{}/";

        /// <summary>
        /// Returns a uniformly distributed number in [0, max) from a cryptographic generator.
        /// </summary>
        private static int NextSecure(int max)
        {
            var bytes = new byte[4];
            var limit = uint.MaxValue - uint.MaxValue % (uint)max;
            uint value;
            using (var rng = new RNGCryptoServiceProvider())
            {
                do
                {
                    rng.GetBytes(bytes);
                    value = BitConverter.ToUInt32(bytes, 0);
                }
                while (value >= limit);
            }

            return (int)(value % (uint)max);
        }

        /// <summary>
        /// Generates the password.
        /// </summary>
        /// <param name="minLength">Minimum password length.</param>
        /// <param name="includeUpper">If set to <c>true</c>, generated password will include upper case characters.</param>
        /// <param name="includeNumbers">If set to <c>true</c>, generated password will include numeric characters.</param>
        /// <param name="includeSpecial">If set to <c>true</c>, generated password will include special characters.</param>
        /// <returns>Generated password.</returns>
        public string GeneratePassword(int minLength, bool includeUpper, bool includeNumbers, bool includeSpecial)
        {
            var password = new StringBuilder();
            for (int i = 0; i < minLength; i++)
            {
                string chars = PasswordCharsLower;

                if (i == 0 && includeUpper)
                {
                    chars = PasswordCharsUpper;
                }
                else if (i == minLength - 1 && includeNumbers)
                {
                    chars = PasswordCharsNumbers;
                }

                password.Append(chars[NextSecure(chars.Length)]);
            }

            if (includeSpecial)
            {
                password.Append(PasswordCharsSpecial[NextSecure(PasswordCharsSpecial.Length)]);
            }

            return password.ToString();
        }
    }
}