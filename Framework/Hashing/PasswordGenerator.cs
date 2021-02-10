using System;
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

        private static Random random = new Random();

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

                password.Append(chars[random.Next(0, chars.Length - 1)]);
            }

            if (includeSpecial)
            {
                password.Append(PasswordCharsSpecial[random.Next(0, PasswordCharsSpecial.Length - 1)]);
            }

            return password.ToString();
        }
    }
}