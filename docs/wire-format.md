# Peerless wire format, version 1.0

A native shim (Android: Java, iOS: Objective-C/Swift) encodes each Firestore snapshot into one **frame**: a single
contiguous byte buffer. It hands the frame to .NET in one call, and the .NET decoder
(`AdamE.Firestore.Peerless.SnapshotDecoder`) turns it into plain .NET values. The format exists so that no Firestore
value ever needs a managed peer object.

Design goals, in order:

1. **Exact.** Every Firestore value kind survives with its kind intact. Integers and doubles stay distinct. A value the
   encoder doesn't understand is written as an explicit *unsupported* value carrying its native type name. It is never
   dropped or coerced.
2. **Cheap to write on the native side** (no second pass over the data, no intermediate objects), and cheap to read in
   .NET (forward-only, span-based).
3. **Versioned and extensible** without breaking existing decoders.

Golden frames that every encoder must reproduce byte for byte live in [`fixtures/wire`](../fixtures/wire).

## Primitives

| name | encoding |
|---|---|
| `u8` | one byte |
| `u32` | 4 bytes, unsigned, little-endian |
| `i32` | 4 bytes, two's complement, little-endian |
| `i64` | 8 bytes, two's complement, little-endian |
| `f64` | 8 bytes, IEEE 754 binary64, little-endian. The bits are kept as-is: `-0.0`, NaN payloads and infinities round-trip. Java encoders must use `Double.doubleToRawLongBits` (not `doubleToLongBits`, which collapses NaN payloads); Objective-C copies the `double`'s bytes. |
| `varint` | unsigned LEB128 (7 bits per byte, low group first, high bit = more), at most 5 bytes, value ≤ 2³¹−1. Encoders must use the shortest form, and decoders **reject** anything else: a fifth byte above `0x07` (more than 5 bytes, or a value above 2³¹−1), and a final byte of `0x00` after the first byte (a padded, non-shortest form). |
| `string` | `varint` byte length, then that many bytes of well-formed UTF-8 (RFC 3629). Decoders **reject** ill-formed UTF-8, including overlong forms such as modified UTF-8's `C0 80` for NUL, and encoded surrogates (CESU-8). A NUL character is written as the single byte `00`. A lone UTF-16 surrogate in a source string is written as U+FFFD (`EF BF BD`) on every encoder: .NET's `Encoding.UTF8` does that; Java's `String.getBytes(UTF_8)` writes `?` instead, so the Android encoder does its own UTF-8; CoreFoundation's conversion writes CESU-style `ED A0..BF xx`, so the iOS encoder detects that and re-encodes the string itself. |

## Frame

```
frame   = header section*
header  = magic:u8[4] major:u8 minor:u8 kind:u8 reserved:u8
section = id:u8 length:u32 payload:u8[length]
```

- `magic` is `50 4C 53 46` (ASCII `PLSF`).
- `major` is `1`. A decoder rejects any other major version. A major bump means existing decoders can't read the frame.
- `minor` is `0`. Minor versions only add things that 1.0 decoders can safely skip: **new sections**. A minor version
  can't add fields to the end of an existing section (or a document, or a value), because decoders require each
  section's content to fill its declared length exactly. Decoders accept any minor within their major.
- `reserved` is `0` in 1.0. Decoders ignore it.
- `kind`:

| kind | meaning | sections |
|---:|---|---|
| 1 | query snapshot | metadata (required), documents (required) |
| 2 | document snapshot | metadata (required), documents (required, exactly one document) |
| 3 | *reserved:* query changes | see [Future: changes-only frames](#future-changes-only-frames) |

  A decoder rejects any kind it doesn't know.

Sections follow the header until the end of the frame. The frame's length is known from the buffer it arrives in; there
is no trailing marker. Each section ID, known or not, appears at most once, and decoders reject a repeat. A decoder skips any section ID it doesn't know (that's
what the `u32` length is for), and rejects a frame that is missing a required section or repeats one. Sections may
come in any order, and encoders write them in ascending ID order.

### Section 1: metadata

```
metadata = flags:u8
```

Flag bits: `0x01` = `isFromCache`, `0x02` = `hasPendingWrites`. Other bits are `0` in 1.0, and decoders ignore them.

### Section 2: documents

```
documents = count:varint document*count
document  = path:string flags:u8 fields
fields    = (if flags & 0x01) map-body
```

- `path` is the document's full path, for example `rooms/abc/messages/1`. A path that is empty, has an empty segment
  (a leading, trailing or doubled `/`, including `/` alone) or has an odd number of segments (a collection path such
  as `rooms`) is rejected.
- Flag bits: `0x01` = `exists`, `0x02` = `isFromCache`, `0x04` = `hasPendingWrites` (the document's own metadata).
- A document that doesn't exist has no `fields` at all. Only a document snapshot frame (kind 2) can carry one.
- Documents appear in the snapshot's order, which for a query is the query's order.

## Values

```
value = tag:u8 body
```

| tag | kind | body |
|---:|---|---|
| 0 | null | none |
| 1 | boolean `false` | none |
| 2 | boolean `true` | none |
| 3 | integer | `i64` |
| 4 | double | `f64` |
| 5 | string | `string` |
| 6 | bytes | `varint` length, raw bytes |
| 7 | timestamp | `seconds:i64 nanoseconds:i32`, within Firestore's range: 0 ≤ nanoseconds ≤ 999,999,999 and −62,135,596,800 ≤ seconds ≤ 253,402,300,799 (0001-01-01T00:00:00Z to 9999-12-31T23:59:59.999999999Z). Decoders reject anything outside it. |
| 8 | geo point | `latitude:f64 longitude:f64`, with −90 ≤ latitude ≤ 90 and −180 ≤ longitude ≤ 180 (never NaN), as Firestore requires. Decoders reject anything else. |
| 9 | reference | `path:string`, the referenced document's full path within its database, under the same rule as a document's path (non-empty, no empty segment, an even number of segments; decoders reject anything else). The database ID is not carried (the SDKs' `DocumentReference.path` doesn't include it), so a reference into another database of the same project reads as a path in the listener's own database. Carrying it would need a new tag, a major version. |
| 10 | array | `count:varint value*count` |
| 11 | map | `map-body` |
| 12 | unsupported | `typeName:string`, the native type the encoder didn't understand |

```
map-body = count:varint (key:string value)*count
```

- Map keys are unique within a map, and decoders **reject** a map with a repeated key, including a document's
  fields. Encoders read keys from a native map, so they start out unique, with one exception: a lone UTF-16 surrogate
  is written as U+FFFD, so two keys that differ only in lone surrogates would be written identically. An encoder must
  detect that (it can only happen in a map where some key held a lone surrogate) and **fail that snapshot** rather
  than write the duplicate. The Android shim reports it as an encoding failure (`onError` with a
  `PeerlessFormatException`); the iOS encoder must do the same. Key order is the encoder's iteration
  order and carries no meaning; decoders must not depend on it.
- A decoder rejects an unknown tag. Because a value carries no length, an unknown tag can't be skipped, so a new value
  kind needs a **major** version bump.
- Nesting is limited (Firestore's own limit is 20 levels), so a hostile frame can't overflow a decoder's stack. A
  document's field values are at depth 1; the items of an array or map value at depth *d* are at depth *d* + 1. An
  array or map value **at depth 100 or more is rejected**, so at most 99 containers can nest inside a document's
  fields. (The document's own fields map isn't counted.)
- A count (array items, map entries, documents, bytes) can't exceed the bytes left in the frame. An array item takes
  at least one byte (its tag), a map entry at least two (a key length and a tag), and a document at least two (a path
  length and its flags). So in a frame that can decode, the minimum size of everything the open containers still
  declare always fits in the bytes left. Decoders **reject a frame as soon as its declared counts stop fitting**,
  before allocating for them; a frame that nests counts claiming the same bytes over and over is caught at the second
  level. The .NET decoder then allocates each container at its declared size. Its memory per frame byte is at most
  about **48 bytes**, whether the frame decodes or is rejected:
  - The densest shape that decodes, a chain of one-entry maps at 3 bytes each, measures 43 on CoreCLR. Mono's 8-byte
    larger array headers add about 5. The tests run on CoreCLR only.
  - A rejected frame keeps what its valid prefix cost (a prefix of one-entry maps followed by a bad count measures
    41). The containers allocated before an inner count fails cost at most one array slot per byte they claim: 32
    for nested arrays, 20 for nested maps.
  - The documents array is allocated exactly as declared, at most 4 bytes per frame byte.
  - On top of that, a thread's first decode allocates about 5 KB of working memory (field-name cache and
    duplicate-key tables), reused by its later decodes.

### What encoders write

- **Server timestamps** that the backend hasn't resolved yet are written exactly as the platform SDK reports them with
  its default `ServerTimestampBehavior` (`NONE`, which means null). Peerless doesn't add its own estimate.
- **Integers** are whatever the SDK reports as a 64-bit integer (`Long` on Android, an integer `NSNumber` on iOS), and
  **doubles** are what it reports as a double. Neither is ever converted into the other.
- Anything else, for example `VectorValue` or a future SDK type, is written as **unsupported** with the platform's
  class name (on Android, `getClass().getName()`).

## Future: changes-only frames

A later minor or major version will add a "changes only" variant, so a large query that changes by one document
doesn't resend every document. The plan, which fits this layout without breaking it:

- Frame kind `3` (query changes) reuses section 1 (metadata) and section 2 (documents). Section 2 then carries only the
  documents that were added or modified.
- A new section 3 (changes) holds `count:varint (type:u8 oldIndex:varint newIndex:varint document:varint)*count`.
  `type` is added (0), modified (1) or removed (2). An index is stored `+1`, so `0` means −1 ("not in the old or new
  result"). `document` indexes into section 2, also `+1`, with `0` for none (removed documents).
- Kind 1 and 2 frames are unchanged, so a 1.x decoder that doesn't know kind 3 rejects it explicitly instead of
  misreading it.

None of this is implemented in 1.0. Section ID 3 and frame kind 3 are reserved for it.
