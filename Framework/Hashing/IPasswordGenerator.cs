namespace Framework.Hashing
{
    /// <summary>
    /// Provides password generation functionality.
    /// </summary>
    public interface IPasswordGenerator
    {
        /// <summary>
        /// Generates the password.
        /// </summary>
        /// <param name="minLength">Minimum password length.</param>
        /// <param name="includeUpper">If set to <c>true</c>, generated password will include upper case characters.</param>
        /// <param name="includeNumbers">If set to <c>true</c>, generated password will include numeric characters.</param>
        /// <param name="includeSpecial">If set to <c>true</c>, generated password will include special characters.</param>
        /// <returns>Generated password.</returns>
        string GeneratePassword(int minLength, bool includeUpper, bool includeNumbers, bool includeSpecial);
    }
}