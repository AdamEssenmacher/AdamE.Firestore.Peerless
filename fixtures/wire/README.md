# Golden wire frames

Byte-exact frames for [wire format 1.0](../../docs/wire-format.md). Every encoder's tests (C# test encoder, Android Java
encoder, later the iOS encoder) build the inputs below and must produce these files exactly. The .NET decoder's tests
decode them back. The source of truth for the inputs is
[`GoldenFixtures.cs`](../../tests/AdamE.Firestore.Peerless.Tests/GoldenFixtures.cs).

Map keys are written in insertion order, so native tests must insert them in the order listed here (on Android, use a
`LinkedHashMap`).

To regenerate after a deliberate format change, run `PEERLESS_REGENERATE_FIXTURES=1 dotnet test --project
tests/AdamE.Firestore.Peerless.Tests`, then update the native encoders until their tests pass again.

## `document-scalars.bin`

Document snapshot. Snapshot metadata: not from cache, no pending writes. Document `fixtures/scalars` exists, with
metadata not from cache and no pending writes. Fields, in order:

| key | value |
|---|---|
| `null` | null |
| `false` | false |
| `true` | true |
| `zero` | integer 0 |
| `int-min` | integer −2⁶³ |
| `int-max` | integer 2⁶³−1 |
| `int-negative` | integer −42 |
| `double-whole` | double 3.0 (must stay a double) |
| `double-fraction` | double −1.5 |
| `double-negative-zero` | double −0.0 |
| `double-nan` | double NaN with bits `0x7FF8000000000000` (Java's `Double.NaN`) |
| `double-infinity` | double +∞ |
| `string-empty` | `""` |
| `string-ascii` | `"hello"` |
| `string-unicode` | `"héllo wörld ✓ 😀"` |
| `bytes` | bytes `00 01 7F 80 FF` |
| `bytes-empty` | bytes, empty |
| `timestamp` | 1767225600 s, 123456789 ns |
| `timestamp-pre-epoch` | −1 s, 999999999 ns |
| `geopoint` | (42.3314, −83.0458) |
| `reference` | reference `users/user-001` |
| `unsupported` | unsupported, type name `com.google.firebase.firestore.VectorValue` |

## `document-missing.bin`

Document snapshot. Snapshot metadata: from cache, no pending writes. Document `fixtures/missing` does **not** exist,
with metadata from cache and no pending writes.

## `query-nested.bin`

Query snapshot. Snapshot metadata: not from cache, **has pending writes**. Two documents:

1. `items/a` exists, with metadata not from cache and **has pending writes**. Fields:
   - `tags`: `["x", "y"]`
   - `empty-array`: `[]`
   - `empty-map`: `{}`
   - `nested`: `{ "level": 1, "deeper": { "list": [1, 2.5, "three", null, true, { "k": "v" }, [[]]] } }`. Here `1` is
     an integer and `2.5` a double.
2. `items/b` exists, with metadata not from cache and no pending writes. Fields:
   - `count`: integer 128
   - `long-string`: 200 × `"a"` (its length needs a 2-byte varint)

## `query-empty.bin`

Query snapshot with no documents. Snapshot metadata: from cache, no pending writes.
