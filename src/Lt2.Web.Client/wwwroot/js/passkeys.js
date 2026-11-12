// WebAuthn glue for passkeys. The server produces the options JSON (ASP.NET Core Identity) and checks the result;
// this module only converts between JSON and the browser API.

export function isSupported() {
  return typeof window.PublicKeyCredential === "function"
    && typeof PublicKeyCredential.parseCreationOptionsFromJSON === "function"
    && typeof PublicKeyCredential.parseRequestOptionsFromJSON === "function";
}

export async function create(optionsJson) {
  const publicKey = PublicKeyCredential.parseCreationOptionsFromJSON(JSON.parse(optionsJson));
  const credential = await navigator.credentials.create({ publicKey });
  return JSON.stringify(credential.toJSON());
}

export async function get(optionsJson) {
  const publicKey = PublicKeyCredential.parseRequestOptionsFromJSON(JSON.parse(optionsJson));
  const credential = await navigator.credentials.get({ publicKey });
  return JSON.stringify(credential.toJSON());
}
