using System;
using System.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace Framework.Hashing
{
    /// <summary>
    /// Signs values put into links sent by e-mail (e.g. the unsubscribe link) with HMAC-SHA256 and the
    /// "UrlSigningSecret" setting.
    /// </summary>
    public static class UrlSignature
    {
        public static string Sign(string purpose, string value)
        {
            var secret = ConfigurationManager.AppSettings["UrlSigningSecret"];
            if (string.IsNullOrEmpty(secret) || secret.Length < 32)
            {
                throw new InvalidOperationException("UrlSigningSecret (at least 32 characters) must be configured.");
            }

            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(purpose + "|" + (value ?? string.Empty)));
                return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
        }

        public static bool Verify(string purpose, string value, string signature)
        {
            if (string.IsNullOrEmpty(signature))
            {
                return false;
            }

            return PasswordHasher.SlowEquals(Encoding.ASCII.GetBytes(Sign(purpose, value)), Encoding.ASCII.GetBytes(signature));
        }
    }
}
