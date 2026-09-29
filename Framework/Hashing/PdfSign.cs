using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.X509;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.security;

namespace Framework.Hashing
{
    public static class PdfSign
    {
        /// <summary>
        /// Signs a PDF document using iTextSharp library
        /// </summary>
        /// <param name="sourceDocument">The path of the source pdf document which is to be signed</param>
        /// <param name="reason">String describing the reason for signing, would be embedded as part of the signature</param>
        /// <param name="location">Location where the document was signed, would be embedded as part of the signature</param>
        public static byte[] SignPdfFile(byte[] sourceDocument, string reason, string location)
        {
            var cert = DigitalSignature.GetStoreCertificate();

            var cp = new Org.BouncyCastle.X509.X509CertificateParser();
            var pdfCert = cp.ReadCertificate(cert.RawData);
            var certChain = new[] { pdfCert };

            // reader and stamper
            PdfReader reader = new PdfReader(sourceDocument);
            using (MemoryStream fout = new MemoryStream())
            {
                using (PdfStamper stamper = PdfStamper.CreateSignature(reader, fout, '\0'))
                {
                    // appearance
                    PdfSignatureAppearance appearance = stamper.SignatureAppearance;
                    appearance.Reason = reason;
                    appearance.Location = location;
                    var rect = reader.GetPageSize(1);
                    appearance.SetVisibleSignature(new Rectangle(rect.Width - 128, rect.Height - 78, rect.Width - 10, rect.Height - 20), 1, null);
                    // digital signature
                    IExternalSignature es = new X509Certificate2Signature(cert, "SHA1");
                    MakeSignature.SignDetached(appearance, es, certChain, null, null, null, 0, CryptoStandard.CMS);

                    stamper.Close();
                }

                return fout.ToArray();
            }
        }

        /// <summary>
        /// Verifies the signature of a prevously signed PDF document using the specified public key
        /// </summary>
        /// <param name="pdfFile">a Previously signed pdf document</param>
        /// <param name="publicKeyStream">Public key to be used to verify the signature in .cer format</param>
        /// <exception cref="System.InvalidOperationException">Throw System.InvalidOperationException if the document is not signed or the signature could not be verified</exception>
        public static void VerifyPdfSignature(string pdfFile, Stream publicKeyStream)
        {
            var parser = new X509CertificateParser();
            var certificate = parser.ReadCertificate(publicKeyStream);
            publicKeyStream.Dispose();

            PdfReader reader = new PdfReader(pdfFile);
            AcroFields af = reader.AcroFields;
            var names = af.GetSignatureNames();

            if (names.Count == 0)
            {
                throw new InvalidOperationException("No Signature present in pdf file.");
            }

            foreach (string name in names)
            {
                if (!af.SignatureCoversWholeDocument(name))
                {
                    throw new InvalidOperationException(string.Format("The signature: {0} does not covers the whole document.", name));
                }

                PdfPKCS7 pk = af.VerifySignature(name);
                var cal = pk.SignDate;
                var pkc = pk.Certificates;

                if (!pk.Verify())
                {
                    throw new InvalidOperationException("The signature could not be verified.");
                }
                if (!pk.VerifyTimestampImprint())
                {
                    throw new InvalidOperationException("The signature timestamp could not be verified.");
                }

                var fails = CertificateVerification.VerifyCertificates(pkc, new[] { certificate }, null, cal);
                if (fails != null && fails.Any())
                {
                    throw new InvalidOperationException("The file is not signed using the specified key-pair.");
                }
            }
        }
    }
}
