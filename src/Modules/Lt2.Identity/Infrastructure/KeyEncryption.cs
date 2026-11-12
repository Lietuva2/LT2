using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lt2.Identity.Infrastructure;

/// <summary>The key that wraps the data-protection key ring stored in PostgreSQL (design 6.5), supplied as a secret.</summary>
internal sealed class KeyEncryptionOptions
{
    public const string Section = "DataProtection";

    /// <summary>Base64 of 32 random bytes.</summary>
    public string? KeyEncryptionKey { get; set; }

    public byte[] GetKey()
    {
        var key = Convert.FromBase64String(KeyEncryptionKey ?? throw new InvalidOperationException("DataProtection:KeyEncryptionKey is not set."));
        return key.Length == 32 ? key : throw new InvalidOperationException("DataProtection:KeyEncryptionKey must be 32 bytes.");
    }
}

/// <summary>Wraps each data-protection key with AES-256-GCM before it is written to the database.</summary>
internal sealed class AesGcmXmlEncryptor(IOptions<KeyEncryptionOptions> options) : IXmlEncryptor
{
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);
        var plaintext = System.Text.Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var ciphertext = new byte[plaintext.Length];
        using (var aes = new AesGcm(options.Value.GetKey(), tag.Length))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var element = new XElement(
            "encryptedKey",
            new XComment(" Wrapped with AES-256-GCM using DataProtection:KeyEncryptionKey. "),
            new XElement("nonce", Convert.ToBase64String(nonce)),
            new XElement("tag", Convert.ToBase64String(tag)),
            new XElement("value", Convert.ToBase64String(ciphertext)));
        return new EncryptedXmlInfo(element, typeof(AesGcmXmlDecryptor));
    }
}

/// <summary>Unwraps keys written by <see cref="AesGcmXmlEncryptor"/>. Created by data protection through its activator.</summary>
internal sealed class AesGcmXmlDecryptor(IServiceProvider services) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);
        var key = services.GetRequiredService<IOptions<KeyEncryptionOptions>>().Value.GetKey();
        var nonce = Convert.FromBase64String((string)encryptedElement.Element("nonce")!);
        var tag = Convert.FromBase64String((string)encryptedElement.Element("tag")!);
        var ciphertext = Convert.FromBase64String((string)encryptedElement.Element("value")!);
        var plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(key, tag.Length))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        return XElement.Parse(System.Text.Encoding.UTF8.GetString(plaintext));
    }
}
