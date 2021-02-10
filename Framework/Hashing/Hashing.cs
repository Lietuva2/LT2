using System.Security.Cryptography;
using System.Text;

namespace Framework.Hashing
{
    /// <summary>
    /// Provides hashing functionality.
    /// </summary>
    public static class Hashing
    {
        /// <summary>
        /// Default MD5 hash algorithm used with <see cref="ComputeHash(string)"/>.
        /// </summary>
        private static readonly HashAlgorithm md5 = new MD5CryptoServiceProvider();

        /// <summary>
        /// Computes MD5 hash for the specified value.
        /// </summary>
        /// <param name="value">The value to hash.</param>
        /// <returns>Computed MD5 hash.</returns>
        public static string ComputeHash(this string value)
        {
            return ComputeHash(value, md5);
        }

        /// <summary>
        /// Computes hash for the specified value.
        /// </summary>
        /// <param name="value">The value to hash.</param>
        /// <param name="algo">Hash algorithm to use.</param>
        /// <returns>Computed hash.</returns>
        public static string ComputeHash(this string value, HashAlgorithm algo)
        {
            if(string.IsNullOrEmpty(value))
            {
                return value;
            }

            byte[] data = Encoding.Unicode.GetBytes(value);
            data = algo.ComputeHash(data);
            var hash = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                hash.Append(data[i].ToString("x2"));
            }

            return hash.ToString();
        }

        public static string GetMd5Hash(this string input)
        {
            using (MD5 md5 = MD5CryptoServiceProvider.Create())
            {
                byte[] dataMd5 = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < dataMd5.Length; i++)
                    sb.AppendFormat("{0:x2}", dataMd5[i]);
                return sb.ToString();
            }
        }
    }
}