// WebAuthn glue for passkeys. The server produces the options JSON (ASP.NET Core Identity) and checks the result;
// this module only converts between JSON and the browser API.
//
// The result is serialised defensively. Password managers (1Password and others) replace navigator.credentials and
// hand back their own credential objects, whose toJSON() may leave out clientExtensionResults or throw. Identity
// requires id, type, response and clientExtensionResults, so toJSON() is used when it works, the fields are read by
// hand when it does not, and clientExtensionResults is always present.

export function isSupported() {
  return typeof window.PublicKeyCredential === "function"
    && typeof PublicKeyCredential.parseCreationOptionsFromJSON === "function"
    && typeof PublicKeyCredential.parseRequestOptionsFromJSON === "function";
}

export async function create(optionsJson) {
  const publicKey = PublicKeyCredential.parseCreationOptionsFromJSON(JSON.parse(optionsJson));
  return serialize(await navigator.credentials.create({ publicKey }));
}

export async function get(optionsJson) {
  const publicKey = PublicKeyCredential.parseRequestOptionsFromJSON(JSON.parse(optionsJson));
  return serialize(await navigator.credentials.get({ publicKey }));
}

function serialize(credential) {
  if (!credential) {
    throw new Error("No credential was returned.");
  }

  let json = null;
  try {
    json = typeof credential.toJSON === "function" ? credential.toJSON() : null;
  } catch {
    json = null;
  }

  json = json && typeof json === "object" && json.response ? { ...json } : byHand(credential);
  if (!json.clientExtensionResults || typeof json.clientExtensionResults !== "object") {
    json.clientExtensionResults = extensionResults(credential);
  }

  json.id ??= credential.id ?? base64url(credential.rawId);
  json.type ??= credential.type ?? "public-key";
  return JSON.stringify(json);
}

function byHand(credential) {
  const r = credential.response;
  const response = { clientDataJSON: base64url(r.clientDataJSON) };
  if (r.attestationObject) {
    response.attestationObject = base64url(r.attestationObject);
    const transports = attempt(() => r.getTransports());
    if (Array.isArray(transports)) response.transports = transports;
    const authenticatorData = attempt(() => r.getAuthenticatorData());
    if (authenticatorData) response.authenticatorData = base64url(authenticatorData);
    const publicKey = attempt(() => r.getPublicKey());
    if (publicKey) response.publicKey = base64url(publicKey);
    const algorithm = attempt(() => r.getPublicKeyAlgorithm());
    if (typeof algorithm === "number") response.publicKeyAlgorithm = algorithm;
  } else {
    response.authenticatorData = base64url(r.authenticatorData);
    response.signature = base64url(r.signature);
    if (r.userHandle) response.userHandle = base64url(r.userHandle);
  }

  return {
    id: credential.id,
    rawId: base64url(credential.rawId),
    type: credential.type,
    authenticatorAttachment: credential.authenticatorAttachment ?? null,
    response,
    clientExtensionResults: extensionResults(credential),
  };
}

function extensionResults(credential) {
  const results = attempt(() => credential.getClientExtensionResults());
  return results && typeof results === "object" ? plain(results) : {};
}

// Extension results may hold buffers (for example prf); JSON carries them as base64url.
function plain(value) {
  if (value instanceof ArrayBuffer || ArrayBuffer.isView(value)) return base64url(value);
  if (Array.isArray(value)) return value.map(plain);
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, plain(item)]));
  }
  return value;
}

function attempt(read) {
  try {
    return read();
  } catch {
    return undefined;
  }
}

function base64url(buffer) {
  if (!buffer) return undefined;
  const bytes = buffer instanceof ArrayBuffer
    ? new Uint8Array(buffer)
    : new Uint8Array(buffer.buffer, buffer.byteOffset, buffer.byteLength);
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
