using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>
/// A minimal WebAuthn authenticator in software (ES256, "none" attestation, user verified), so the passkey endpoints can
/// be tested end to end against ASP.NET Core Identity's real verification. It produces exactly the JSON a browser's
/// <c>PublicKeyCredential.toJSON()</c> would.
/// </summary>
public sealed class SoftwareAuthenticator : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _credentialId = RandomNumberGenerator.GetBytes(32);
    private string? _userHandle;
    private uint _signCount;

    public string Origin { get; init; } = Lt2Factory.Origin;

    /// <summary>The credential id, base64url, as the server's options carry it.</summary>
    public string CredentialId => Base64Url(_credentialId);

    /// <summary>Answers <c>navigator.credentials.create()</c> for the given creation options.</summary>
    public string Create(string optionsJson)
    {
        using var options = JsonDocument.Parse(optionsJson);
        var root = options.RootElement;
        var rpId = root.GetProperty("rp").GetProperty("id").GetString()!;
        _userHandle = root.GetProperty("user").GetProperty("id").GetString();
        var clientData = ClientData("webauthn.create", root.GetProperty("challenge").GetString()!);

        var parameters = _key.ExportParameters(includePrivateParameters: false);
        var cose = new CborWriter(CborConformanceMode.Ctap2Canonical);
        cose.WriteStartMap(5);
        cose.WriteInt32(1); cose.WriteInt32(2);        // kty: EC2
        cose.WriteInt32(3); cose.WriteInt32(-7);       // alg: ES256
        cose.WriteInt32(-1); cose.WriteInt32(1);       // crv: P-256
        cose.WriteInt32(-2); cose.WriteByteString(parameters.Q.X!);
        cose.WriteInt32(-3); cose.WriteByteString(parameters.Q.Y!);
        cose.WriteEndMap();

        var attested = new List<byte>();
        attested.AddRange(new byte[16]); // AAGUID
        attested.AddRange(BigEndian((ushort)_credentialId.Length));
        attested.AddRange(_credentialId);
        attested.AddRange(cose.Encode());
        var authenticatorData = AuthenticatorData(rpId, flags: 0x01 | 0x04 | 0x40, attested.ToArray());

        var attestation = new CborWriter(CborConformanceMode.Ctap2Canonical);
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt"); attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt"); attestation.WriteStartMap(0); attestation.WriteEndMap();
        attestation.WriteTextString("authData"); attestation.WriteByteString(authenticatorData);
        attestation.WriteEndMap();

        return JsonSerializer.Serialize(new
        {
            id = Base64Url(_credentialId),
            rawId = Base64Url(_credentialId),
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = Base64Url(clientData),
                attestationObject = Base64Url(attestation.Encode()),
                transports = new[] { "internal" },
            },
        });
    }

    /// <summary>Answers <c>navigator.credentials.get()</c> for the given request options.</summary>
    public string Get(string optionsJson, string? challengeOverride = null)
    {
        using var options = JsonDocument.Parse(optionsJson);
        var root = options.RootElement;
        var rpId = root.TryGetProperty("rpId", out var rp) ? rp.GetString()! : new Uri(Origin).Host;
        var clientData = ClientData("webauthn.get", challengeOverride ?? root.GetProperty("challenge").GetString()!);
        var authenticatorData = AuthenticatorData(rpId, flags: 0x01 | 0x04, []);
        var signed = authenticatorData.Concat(SHA256.HashData(clientData)).ToArray();
        var signature = _key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return JsonSerializer.Serialize(new
        {
            id = Base64Url(_credentialId),
            rawId = Base64Url(_credentialId),
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = Base64Url(clientData),
                authenticatorData = Base64Url(authenticatorData),
                signature = Base64Url(signature),
                userHandle = _userHandle,
            },
        });
    }

    public void Dispose() => _key.Dispose();

    private byte[] ClientData(string type, string challenge) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, challenge, origin = Origin, crossOrigin = false }));

    private byte[] AuthenticatorData(string rpId, byte flags, byte[] attestedCredentialData)
    {
        _signCount++;
        var data = new List<byte>();
        data.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        data.Add(flags);
        var counter = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(counter, _signCount);
        data.AddRange(counter);
        data.AddRange(attestedCredentialData);
        return data.ToArray();
    }

    private static byte[] BigEndian(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
