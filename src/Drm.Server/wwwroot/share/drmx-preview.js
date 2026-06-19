// In-browser .drmx decryption for View-only preview (increment 2).
//
// The recipient's browser holds the .drmx (from the email attachment) and, after
// verifying, fetches the unwrapped AES key from /api/share-links/viewer/content-key.
// This module decrypts the container entirely client-side — the ciphertext never
// touches our servers, preserving the "file never touches our servers" model.
//
// Container format (must match Drm.Container.ProtectedFileWriter):
//   "DRM1" magic (4 bytes)
//   int32 big-endian length + header JSON (UTF-8)
//   int32 big-endian length + AES-GCM nonce  (12 bytes)
//   int32 big-endian length + AES-GCM tag    (16 bytes)
//   int32 big-endian length + ciphertext
//
// AES-GCM associated data must byte-match Drm.Container.ProtectedFileAssociatedData.For:
//   join("\n", [Version, TenantId "D", FileId "D", ContentType,
//               CreatedAtUtc.ToUniversalTime().Ticks])
// The Ticks value is .NET 100-nanosecond ticks since 0001-01-01 — reconstructed
// here to full precision from the header's ISO timestamp. If it is off by even one
// tick, GCM authentication fails, so this is the load-bearing detail.

const DRMX_MAGIC = "DRM1";
const TICKS_PER_SECOND = 10_000_000n;
// Ticks from 0001-01-01T00:00:00Z to 1970-01-01T00:00:00Z.
const UNIX_EPOCH_TICKS = 621_355_968_000_000_000n;

export function parseDrmxContainer(arrayBuffer) {
  const bytes = new Uint8Array(arrayBuffer);
  const view = new DataView(arrayBuffer);

  if (bytes.length < 4 || new TextDecoder().decode(bytes.subarray(0, 4)) !== DRMX_MAGIC) {
    throw new Error("This file is not a zcrDRM .drmx container.");
  }

  let offset = 4;
  const readChunk = () => {
    if (offset + 4 > bytes.length) throw new Error("Corrupt .drmx (truncated length).");
    const len = view.getInt32(offset, /* littleEndian */ false);
    offset += 4;
    if (len < 0 || offset + len > bytes.length) throw new Error("Corrupt .drmx (bad chunk length).");
    const chunk = bytes.subarray(offset, offset + len);
    offset += len;
    return chunk;
  };

  const headerBytes = readChunk();
  const nonce = readChunk();
  const tag = readChunk();
  const ciphertext = readChunk();
  const header = JSON.parse(new TextDecoder().decode(headerBytes));
  return { header, nonce, tag, ciphertext };
}

// Reconstruct .NET DateTimeOffset.ToUniversalTime().Ticks from the serialized
// ISO timestamp, to full 100ns precision. Date.parse handles any timezone offset
// (→ UTC ms); the fractional seconds are extracted literally so no precision is
// lost the way Date's millisecond resolution would.
export function dotnetTicks(isoTimestamp) {
  // Strip fractional seconds before parsing so sub-millisecond digits can never round the
  // whole-second value (e.g. ...:00.9999999Z); the fraction is re-applied at full 100ns precision below.
  const whole = isoTimestamp.replace(/\.\d+/, "");
  const ms = Date.parse(whole);
  if (Number.isNaN(ms)) throw new Error("Unparseable header timestamp: " + isoTimestamp);
  const unixSeconds = BigInt(Math.floor(ms / 1000));
  const fractionMatch = /T\d{2}:\d{2}:\d{2}\.(\d+)/.exec(isoTimestamp);
  const fractionDigits = ((fractionMatch ? fractionMatch[1] : "") + "0000000").slice(0, 7);
  return UNIX_EPOCH_TICKS + unixSeconds * TICKS_PER_SECOND + BigInt(fractionDigits);
}

export function associatedData(header) {
  // System.Text.Json serializes Guid as lowercase hyphenated — identical to
  // Guid.ToString("D") — and int as its plain decimal, so these match the
  // server-side canonical fields exactly.
  const canonical = [
    String(header.Version),
    header.TenantId,
    header.FileId,
    header.ContentType,
    dotnetTicks(header.CreatedAtUtc).toString(),
  ].join("\n");
  return new TextEncoder().encode(canonical);
}

export function base64ToBytes(base64) {
  const binary = atob(base64);
  const out = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) out[i] = binary.charCodeAt(i);
  return out;
}

// Decrypt a .drmx ArrayBuffer with the raw 32-byte AES key. Returns the
// plaintext bytes + the content type from the header.
export async function decryptDrmx(arrayBuffer, keyBytes) {
  const { header, nonce, tag, ciphertext } = parseDrmxContainer(arrayBuffer);
  const aad = associatedData(header);

  const key = await crypto.subtle.importKey("raw", keyBytes, { name: "AES-GCM" }, false, ["decrypt"]);

  // .NET stores ciphertext and tag separately; Web Crypto expects them
  // concatenated as ciphertext || tag.
  const sealed = new Uint8Array(ciphertext.length + tag.length);
  sealed.set(ciphertext, 0);
  sealed.set(tag, ciphertext.length);

  let plaintext;
  try {
    plaintext = await crypto.subtle.decrypt(
      { name: "AES-GCM", iv: nonce, additionalData: aad, tagLength: 128 },
      key,
      sealed,
    );
  } catch {
    // GCM auth failure: wrong key, tampered file, or an AAD mismatch.
    throw new Error("Could not decrypt this file — the key or file may be wrong, or the file was altered.");
  }

  return { bytes: new Uint8Array(plaintext), contentType: header.ContentType };
}

// Expose on window for non-module callers (the /share/ page script) and tests.
if (typeof window !== "undefined") {
  window.DrmxPreview = { parseDrmxContainer, dotnetTicks, associatedData, base64ToBytes, decryptDrmx };
}
