namespace Web.Infrastructure.Authentication
{
    /// <summary>
    /// Forms-based authentication.
    /// </summary>
    public interface IAuthentication
    {
        /// <summary>
        /// Gets the default URL.
        /// </summary>
        /// <value>The default URL.</value>
        string DefaultUrl { get; }

        /// <summary>
        /// Signs specified user in.
        /// </summary>
        /// <param name="username">The username to sign in.</param>
        /// <returns>The URL to redirect user to.</returns>
        string SignIn(string username);

        /// <summary>
        /// Signs in.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <param name="rememberMe">if set to <c>true</c> [remember me].</param>
        /// <returns></returns>
        string SignIn(string username, bool rememberMe);

        /// <summary>
        /// Sings current out.
        /// </summary>
        /// <returns>The URL to redirect user to.</returns>
        string SingOut();
    }
}