// The proof of work of registration and code requests (design 6.4), the browser's side. The protocol is ALTCHA's, the code is
// ours: the server hands out a challenge (a salt with its expiry and the SHA-256 of salt + a secret number up to "maxnumber",
// signed with the server's key); the browser finds the number by trying and sends the challenge's fields with it, as base64 of
// JSON. Nothing here is stored (no cookie, no storage key), nothing is loaded from another host, and the person has nothing
// to do: the work starts when the form is shown and is done before the button is pressed.
//
// prepare(url): starts fetching and solving one challenge in the background (a no-op while one is under way or ready).
// take(url):    the solved payload to send, waiting for the work if it is still going; a prepared one that has expired, or
//               that failed, is replaced by a new one once. null when the server needs no proof (204) or the browser cannot
//               do it (the server then refuses, and the form says so). A payload is single use, so the next call makes a new one.

let prepared = null; // { promise, expires } for the challenge being solved or solved

const encoder = new TextEncoder();

// Digests are asked for in batches, so the browser's crypto threads work at the same time.
const BATCH = 256;

export function prepare(url) {
  if (prepared === null) {
    prepared = start(url);
  }
}

export async function take(url) {
  let entry = prepared;
  prepared = null;
  if (entry === null || entry.expired()) {
    entry = start(url);
  }

  let payload = await entry.promise;
  if (payload === undefined) {
    // The one retry, silently: the challenge may have been used up elsewhere, or the work failed.
    payload = await start(url).promise;
  }

  return payload === undefined ? null : payload;
}

function start(url) {
  const entry = { expires: Infinity, expired: () => Date.now() > entry.expires - 15000, promise: null };
  entry.promise = solve(url, entry).catch(() => undefined);
  return entry;
}

// Resolves to the payload, to null when no proof is needed, and rejects when the work cannot be done.
async function solve(url, entry) {
  const response = await fetch(url, { credentials: "same-origin", headers: { Accept: "application/json" } });
  if (response.status === 204) {
    return null;
  }

  if (!response.ok) {
    throw new Error("No challenge.");
  }

  const challenge = await response.json();
  if (challenge.algorithm !== "SHA-256" || typeof challenge.salt !== "string" || typeof challenge.challenge !== "string") {
    throw new Error("Unknown challenge.");
  }

  entry.expires = expiryOf(challenge.salt);
  const number = await find(challenge.salt, challenge.challenge, challenge.maxnumber);
  if (number === null) {
    throw new Error("No number found.");
  }

  return btoa(JSON.stringify({
    algorithm: challenge.algorithm,
    challenge: challenge.challenge,
    number,
    salt: challenge.salt,
    signature: challenge.signature,
  }));
}

function expiryOf(salt) {
  const match = /[?&]expires=(\d+)/.exec(salt);
  return match ? Number(match[1]) * 1000 : Infinity;
}

async function find(salt, target, max) {
  const wanted = fromHex(target);
  for (let start = 0; start <= max; start += BATCH) {
    const end = Math.min(start + BATCH - 1, max);
    const hashes = [];
    for (let n = start; n <= end; n++) {
      hashes.push(crypto.subtle.digest("SHA-256", encoder.encode(salt + n)));
    }

    const results = await Promise.all(hashes);
    for (let i = 0; i < results.length; i++) {
      if (same(new Uint8Array(results[i]), wanted)) {
        return start + i;
      }
    }
  }

  return null;
}

function fromHex(hex) {
  const bytes = new Uint8Array(hex.length / 2);
  for (let i = 0; i < bytes.length; i++) {
    bytes[i] = parseInt(hex.substr(i * 2, 2), 16);
  }

  return bytes;
}

function same(a, b) {
  if (a.length !== b.length) {
    return false;
  }

  for (let i = 0; i < a.length; i++) {
    if (a[i] !== b[i]) {
      return false;
    }
  }

  return true;
}
