using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Framework.Strings;

namespace Framework.Hashing
{
    public static class DigitalSignature
    {
        private static Encoding _encoding = new UTF8Encoding();

        private static string StoreName {get { return ConfigurationManager.AppSettings["StoreName"]; }}

        private static string CertificateName
        {
            get { return ConfigurationManager.AppSettings["CertificateName"]; }
        }

        public static byte[] Sign(params string[] values)
        {
            return Sign(StoreName, CertificateName, _encoding, values);
        }

        public static byte[] Sign(Encoding encoding, params string[] values)
        {
            return Sign(StoreName, CertificateName, encoding, values);
        }

        public static byte[] Sign(string storeName, string certificateName, Encoding encoding, params string[] values)
        {
            var hash = ComputeHash(encoding, values);
            var certificate = GetStoreCertificate(storeName, certificateName);

            if (certificate.PrivateKey == null)
            {
                throw new Exception("Private key not accessible");
            }

            var rsaFormatter = new RSAPKCS1SignatureFormatter(certificate.PrivateKey);
            rsaFormatter.SetHashAlgorithm("SHA1");
            return rsaFormatter.CreateSignature(hash);
        }

        public static bool VerifySignature(string certPath, byte[] signature, Encoding encoding, params string[] values)
        {
            return VerifySignature(new X509Certificate2(certPath), signature, encoding, values);
        }

        public static bool VerifySignature(string certPath, byte[] signature, Encoding encoding, string signData)
        {
            return VerifySignature(new X509Certificate2(certPath), signature, encoding, signData);
        }

        public static bool VerifySignature(string certPath, byte[] signature, params string[] values)
        {
            return VerifySignature(new X509Certificate2(certPath), signature, _encoding, values);
        }

        public static bool VerifySignature(byte[] signature, params string[] values)
        {
            return VerifySignature(GetStoreCertificate(StoreName, CertificateName), signature, _encoding, values);
        }

        public static bool VerifySignature(string storeName, string certificateName, byte[] signature, params string[] values)
        {
            return VerifySignature(GetStoreCertificate(storeName, certificateName), signature, _encoding, values);
        }

        public static X509Certificate2 GetStoreCertificate()
        {
            return GetStoreCertificate(StoreName, CertificateName);
        }

        public static X509Certificate2 GetStoreCertificate(string storeName, string certificateName)
        {
            X509Store store = null;
            try
            {
                store = new X509Store(storeName, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly);
                var certs = store.Certificates.Find(X509FindType.FindBySubjectName, certificateName, true);
                if (certs.Count == 0)
                {
                    throw new Exception("Certificate not found");
                }
                if (certs.Count > 1)
                {
                    throw new Exception("Found multiple certificates");
                }

                return certs[0];
            }
            finally
            {
                if (store != null)
                {
                    store.Close();
                }
            }
        }

        private static bool VerifySignature(X509Certificate2 certificate, byte[] signature, Encoding encoding, params string[] values)
        {
            var hash = ComputeHash(encoding, values);

            return VerifySignature(certificate, signature, hash.Hash);
        }

        private static bool VerifySignature(X509Certificate2 certificate, byte[] signature, Encoding encoding, string signData)
        {
            var hash = ComputeHash(encoding, signData);

            return VerifySignature(certificate, signature, hash.Hash);
        }

        private static bool VerifySignature(X509Certificate2 certificate, byte[] signature, byte[] hash)
        {
            var rsaDeformatter = new RSAPKCS1SignatureDeformatter(certificate.PublicKey.Key);
            rsaDeformatter.SetHashAlgorithm("SHA1");

            //Verify the hash and display the results to the console.
            if (rsaDeformatter.VerifySignature(hash, signature))
            {
                return true;
            }

            return false;
        }

        private static HashAlgorithm ComputeHash(Encoding encoding, string[] values)
        {
            var signData = values.Concatenate("");
            return ComputeHash(encoding, signData);
        }

        private static HashAlgorithm ComputeHash(Encoding encoding, string signData)
        {
            var hash = SHA1.Create();
            var bytes = encoding.GetBytes(signData);
            hash.ComputeHash(bytes);
            return hash;
        }
    }
}
