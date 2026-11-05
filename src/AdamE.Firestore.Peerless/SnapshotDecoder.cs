using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace AdamE.Firestore.Peerless;

/// <summary>
/// Decodes one Peerless frame (see <c>docs/wire-format.md</c>) into plain .NET values. The decoder is stateless and
/// thread-safe, and the result does not reference the input buffer, so the caller may reuse or free it afterwards.
/// </summary>
/// <remarks>
/// Every array item takes at least one byte and every map entry at least two, so the minimum size of everything the
/// open containers still declare must fit in the bytes left to read. A frame whose declared counts don't is rejected the
/// moment they stop fitting; otherwise each container is read straight into an array of its declared size. Memory
/// therefore stays proportional to the frame's size, however hostile its counts: about 48 bytes per frame byte at most,
/// whether the frame decodes or is rejected after a valid prefix (docs/wire-format.md).
/// </remarks>
public static class SnapshotDecoder
{
    // Kept for the tests' readability; the range lives on FirestoreTimestamp.
    internal const long MinTimestampSeconds = FirestoreTimestamp.MinSeconds;
    internal const long MaxTimestampSeconds = FirestoreTimestamp.MaxSeconds;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    [ThreadStatic]
    private static Scratch? cachedScratch;

    // FNV-1a from a seeded offset basis. Internal so the tests can check that their crafted keys really collide.
    internal static uint KeyHash(ReadOnlySpan<byte> bytes, uint seed)
    {
        uint hash = 2166136261 ^ seed;
        foreach (byte value in bytes)
            hash = (hash ^ value) * 16777619;
        return hash ^ (hash >> 15);
    }

    /// <summary>Decodes a query or document snapshot frame.</summary>
    /// <exception cref="PeerlessFormatException">The frame is malformed or uses an unsupported version.</exception>
    public static FirestoreSnapshot Decode(ReadOnlySpan<byte> frame)
    {
        // Taken, not shared: a decode never sees another decode's scratch, even if one somehow nested on this thread.
        Scratch scratch = cachedScratch ?? new Scratch();
        cachedScratch = null;
        try
        {
            var reader = new FrameReader(frame, scratch);
            return reader.ReadFrame();
        }
        finally
        {
            if (scratch.Reset())
                cachedScratch = scratch;
        }
    }

    /// <summary>Decodes a query snapshot frame.</summary>
    /// <exception cref="PeerlessFormatException">The frame is malformed or is not a query snapshot.</exception>
    public static FirestoreQuerySnapshot DecodeQuery(ReadOnlySpan<byte> frame) =>
        Decode(frame) as FirestoreQuerySnapshot
        ?? throw new PeerlessFormatException("The frame is not a query snapshot.", Wire.HeaderLength - 2);

    /// <summary>Decodes a document snapshot frame.</summary>
    /// <exception cref="PeerlessFormatException">The frame is malformed or is not a document snapshot.</exception>
    public static FirestoreDocumentSnapshot DecodeDocument(ReadOnlySpan<byte> frame) =>
        Decode(frame) as FirestoreDocumentSnapshot
        ?? throw new PeerlessFormatException("The frame is not a document snapshot.", Wire.HeaderLength - 2);

    // Strict: invalid UTF-8 (including overlong forms such as modified UTF-8's C0 80, and encoded surrogates) throws
    // DecoderFallbackException, which the reader turns into a PeerlessFormatException.
    private static string Utf8(ReadOnlySpan<byte> bytes) => bytes.IsEmpty ? string.Empty : StrictUtf8.GetString(bytes);

    private ref struct FrameReader(ReadOnlySpan<byte> frame, Scratch scratch)
    {
        private readonly ReadOnlySpan<byte> frame = frame;
        private readonly Scratch scratch = scratch;
        private int position;

        // The minimum unread bytes the items declared but not yet started still need, across all open containers: one
        // per array item, two per map entry (a key length and a value tag).
        private long reserved;

        public FirestoreSnapshot ReadFrame()
        {
            if (frame.Length < Wire.HeaderLength || !frame[..4].SequenceEqual(Wire.Magic))
                throw Fail("The frame does not start with the Peerless header.");
            if (frame[4] != Wire.MajorVersion)
                throw Fail($"Wire format major version {frame[4]} is not supported (expected {Wire.MajorVersion}).", 4);

            byte kind = frame[6];
            if (kind is not (Wire.FrameKind.Query or Wire.FrameKind.Document))
                throw Fail($"Frame kind {kind} is not supported.", 6);

            position = Wire.HeaderLength;
            SnapshotMetadata? metadata = null;
            FirestoreDocument[]? documents = null;
            Span<ulong> sectionsSeen = stackalloc ulong[4]; // one bit per section ID, known or not
            while (position < frame.Length)
            {
                int sectionStart = position;
                byte id = ReadByte();
                ref ulong word = ref sectionsSeen[id >> 6];
                ulong bit = 1UL << (id & 63);
                if ((word & bit) != 0)
                    throw Fail($"Section {id} appears twice.", sectionStart);
                word |= bit;

                uint declared = ReadUInt32();
                if (declared > (uint)(frame.Length - position))
                    throw Fail("A section runs past the end of the frame.", sectionStart);

                int length = (int)declared;
                int end = position + length;
                switch (id)
                {
                    case Wire.Section.Metadata:
                        metadata = ReadMetadata();
                        break;
                    case Wire.Section.Documents:
                        documents = ReadDocuments(kind);
                        break;
                    default:
                        position = end; // A section from a newer minor version.
                        break;
                }

                if (position != end)
                    throw Fail($"Section {id} declared {length} bytes but its content used {position - (end - length)}.", sectionStart);
            }

            if (metadata is not { } snapshotMetadata)
                throw Fail("The frame has no metadata section.");
            if (documents is null)
                throw Fail("The frame has no documents section.");

            if (kind == Wire.FrameKind.Query)
                return new FirestoreQuerySnapshot(snapshotMetadata, Array.AsReadOnly(documents));
            if (documents.Length != 1)
                throw Fail($"A document snapshot frame must hold exactly one document, not {documents.Length}.");
            return new FirestoreDocumentSnapshot(snapshotMetadata, documents[0]);
        }

        private SnapshotMetadata ReadMetadata()
        {
            byte flags = ReadByte();
            return new SnapshotMetadata(
                (flags & Wire.MetadataFlags.FromCache) != 0,
                (flags & Wire.MetadataFlags.HasPendingWrites) != 0);
        }

        // Only one level of documents, so its count is allocated exactly: at most 4 bytes of array per frame byte.
        private FirestoreDocument[] ReadDocuments(byte kind)
        {
            int count = ReadCount(minimumBytesEach: 2);
            var documents = new FirestoreDocument[count];
            for (int index = 0; index < count; index++)
            {
                int start = position;
                string path = ReadString();
                if (!FirestoreDocument.IsValidPath(path))
                    throw Fail($"\"{path}\" is not a document path: it is empty, has an empty segment or has an odd number of segments.", start);

                byte flags = ReadByte();
                bool exists = (flags & Wire.DocumentFlags.Exists) != 0;
                if (!exists && kind == Wire.FrameKind.Query)
                    throw Fail("A query snapshot holds a document that does not exist.", start);

                var metadata = new SnapshotMetadata(
                    (flags & Wire.DocumentFlags.FromCache) != 0,
                    (flags & Wire.DocumentFlags.HasPendingWrites) != 0);
                FirestoreMap fields = exists ? ReadMapBody(depth: 1) : FirestoreMap.Empty;
                documents[index] = new FirestoreDocument(path, exists, metadata, fields, validated: true);
            }

            return documents;
        }

        private FirestoreValue ReadValue(int depth)
        {
            int start = position;
            byte tag = ReadByte();
            switch (tag)
            {
                case Wire.Tag.Null:
                    return FirestoreValue.Null;
                case Wire.Tag.False:
                    return FirestoreValue.FromBoolean(false);
                case Wire.Tag.True:
                    return FirestoreValue.FromBoolean(true);
                case Wire.Tag.Integer:
                    return FirestoreValue.FromInteger(BinaryPrimitives.ReadInt64LittleEndian(Take(8)));
                case Wire.Tag.Double:
                    return FirestoreValue.FromDouble(BinaryPrimitives.ReadDoubleLittleEndian(Take(8)));
                case Wire.Tag.String:
                    return FirestoreValue.FromString(ReadString());
                case Wire.Tag.Bytes:
                    return FirestoreValue.OwningBytes(Take(ReadCount(minimumBytesEach: 1)).ToArray());
                case Wire.Tag.Timestamp:
                {
                    long seconds = BinaryPrimitives.ReadInt64LittleEndian(Take(8));
                    int nanoseconds = BinaryPrimitives.ReadInt32LittleEndian(Take(4));
                    if (!FirestoreTimestamp.IsValid(seconds, nanoseconds))
                        throw Fail($"Timestamp {seconds}s+{nanoseconds}ns is outside Firestore's range (years 0001 to 9999).", start);
                    return FirestoreValue.FromTimestamp(new FirestoreTimestamp(seconds, nanoseconds));
                }

                case Wire.Tag.GeoPoint:
                {
                    double latitude = BinaryPrimitives.ReadDoubleLittleEndian(Take(8));
                    double longitude = BinaryPrimitives.ReadDoubleLittleEndian(Take(8));
                    if (!FirestoreGeoPoint.IsValid(latitude, longitude))
                        throw Fail($"Geo point ({latitude}, {longitude}) is outside [-90, 90] x [-180, 180] or NaN.", start);
                    return FirestoreValue.FromGeoPoint(new FirestoreGeoPoint(latitude, longitude));
                }

                case Wire.Tag.Reference:
                {
                    string path = ReadString();
                    if (!FirestoreDocument.IsValidPath(path))
                        throw Fail($"Reference \"{path}\" is not a document path: it is empty, has an empty segment or has an odd number of segments.", start);
                    return FirestoreValue.FromReference(path);
                }
                case Wire.Tag.Array:
                {
                    CheckDepth(depth, start);
                    int count = ReadCount(minimumBytesEach: 1);
                    if (count == 0)
                        return FirestoreValue.FromArray(FirestoreArray.Empty);

                    Reserve(count, start);
                    var items = new FirestoreValue[count];
                    for (int index = 0; index < count; index++)
                    {
                        reserved--;
                        items[index] = ReadValue(depth + 1);
                    }

                    return FirestoreValue.FromArray(FirestoreArray.Owning(items));
                }

                case Wire.Tag.Map:
                    CheckDepth(depth, start);
                    return FirestoreValue.FromMap(ReadMapBody(depth + 1));
                case Wire.Tag.Unsupported:
                    return FirestoreValue.Unsupported(ReadString());
                default:
                    throw Fail($"Value tag {tag} is not supported.", start);
            }
        }

        private FirestoreMap ReadMapBody(int depth)
        {
            int start = position;
            int count = ReadCount(minimumBytesEach: 2);
            if (count == 0)
                return FirestoreMap.Empty;

            // Key hashes (already computed by the key cache) go on a scratch stack for the duplicate check.
            int hashBottom = scratch.HashTop;
            Reserve(2L * count, start);
            var names = new string[count];
            var values = new FirestoreValue[count];
            for (int index = 0; index < count; index++)
            {
                reserved -= 2;
                names[index] = ReadKey();
                values[index] = ReadValue(depth);
            }

            if (scratch.HasDuplicateKey(names, hashBottom, out string? duplicate))
                throw Fail($"The map key \"{duplicate}\" appears twice.", start);
            scratch.PopHashes(hashBottom);
            return FirestoreMap.Owning(names, values);
        }

        // An array item takes at least one byte (its tag) and a map entry at least two (a key length and a tag), so in a
        // frame that can decode, the minimum size of everything still declared always fits in the bytes left. A frame
        // whose declared counts don't is rejected here, before anything is allocated for them.
        private void Reserve(long bytes, int start)
        {
            if (reserved + bytes > frame.Length - position)
            {
                throw Fail(string.Create(CultureInfo.InvariantCulture,
                    $"Declared counts need at least {reserved + bytes:N0} more bytes, but only {frame.Length - position:N0} are left in the frame."), start);
            }

            reserved += bytes;
        }

        private readonly void CheckDepth(int depth, int start)
        {
            if (depth >= Wire.MaxDepth)
                throw Fail($"Values are nested deeper than {Wire.MaxDepth}.", start);
        }

        // Field names repeat across every document of a snapshot, so they are decoded once per frame.
        private string ReadKey()
        {
            int start = position;
            ReadOnlySpan<byte> bytes = Take(ReadCount(minimumBytesEach: 1));
            try
            {
                return scratch.Key(bytes);
            }
            catch (DecoderFallbackException)
            {
                throw Fail("A map key is not valid UTF-8.", start);
            }
        }

        private string ReadString()
        {
            int start = position;
            ReadOnlySpan<byte> bytes = Take(ReadCount(minimumBytesEach: 1));
            try
            {
                return Utf8(bytes);
            }
            catch (DecoderFallbackException)
            {
                throw Fail("A string is not valid UTF-8.", start);
            }
        }

        // A count can never exceed the bytes left, which stops a corrupt count from forcing a huge allocation.
        private int ReadCount(int minimumBytesEach)
        {
            int start = position;
            uint value = 0;
            for (int index = 0; ; index++)
            {
                byte next = ReadByte();

                // The fifth byte holds bits 28-30 of a value no larger than 2^31-1, so it is at most 0x07 and ends
                // the varint; anything above that is an over-long or out-of-range encoding.
                if (index == 4 && next > 0x07)
                    throw Fail("A varint is longer than 5 bytes or larger than 2^31-1.", start);
                value |= (uint)(next & 0x7F) << (7 * index);
                if ((next & 0x80) == 0)
                {
                    if (index > 0 && next == 0)
                        throw Fail("A varint is not in its shortest form.", start);
                    break;
                }
            }

            if (value > int.MaxValue || value > (uint)(frame.Length - position) / (uint)minimumBytesEach)
                throw Fail($"Length {value} runs past the end of the frame.", start);
            return (int)value;
        }

        private uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

        private byte ReadByte() => Take(1)[0];

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count > frame.Length - position)
                throw Fail("The frame ends in the middle of a value.");
            ReadOnlySpan<byte> taken = frame.Slice(position, count);
            position += count;
            return taken;
        }

        private readonly PeerlessFormatException Fail(string message) => new(message, position);

        private static PeerlessFormatException Fail(string message, int offset) => new(message, offset);
    }

    /// <summary>
    /// Per-thread working memory for one decode: the field-name cache, the key hashes and the duplicate-key table.
    /// Kept between decodes while it stays small, so after a thread's first decode the only allocation here is a copy
    /// of each new field name's bytes, once per frame.
    /// </summary>
    private sealed class Scratch
    {
        private const int InitialStack = 64;
        private const int MaxRetainedTable = 16 * 1024; // larger hash/duplicate tables (a huge map) are left for the GC
        private const int KeyCacheSize = 256;
        private const int KeyCacheLimit = KeyCacheSize / 2;
        private const int MaxProbes = 8;

        // Random per process, so the cache's slots can't be predicted by whoever writes the data.
        private static readonly uint Seed = (uint)Random.Shared.Next() | 1;


        private readonly byte[]?[] cachedBytes = new byte[KeyCacheSize][];
        private readonly string?[] cachedKeys = new string[KeyCacheSize];
        private int cachedCount;

        private uint[] hashes = new uint[InitialStack];
        private int[] seen = [];


        public int HashTop { get; private set; }

        public void PopHashes(int bottom) => HashTop = bottom;



        // Uses the hashes the key cache computed while reading the keys (pushed from hashBottom, one per key): small maps
        // compare pairwise, larger ones go through an open-addressing table of key indexes kept with the scratch. A key
        // that probes past 4 * log2(table size) slots (keys crafted to collide; random hashes stay well under it) switches
        // the whole map to an O(n log n) sort of the key indexes, so the check never goes quadratic.
        public bool HasDuplicateKey(string[] names, int hashBottom, out string? duplicate)
        {
            ReadOnlySpan<uint> keyHashes = hashes.AsSpan(hashBottom, names.Length);
            duplicate = null;
            if (names.Length <= 8)
            {
                for (int left = 1; left < names.Length; left++)
                {
                    for (int right = 0; right < left; right++)
                    {
                        if (keyHashes[left] == keyHashes[right] && string.Equals(names[left], names[right], StringComparison.Ordinal))
                        {
                            duplicate = names[left];
                            return true;
                        }
                    }
                }

                return false;
            }

            int size = (int)BitOperations.RoundUpToPowerOf2((uint)names.Length * 2);
            if (seen.Length < size)
                seen = new int[size];
            int mask = size - 1;
            int maxProbes = 4 * BitOperations.Log2((uint)size);
            try
            {
                for (int index = 0; index < names.Length; index++)
                {
                    int slot = (int)keyHashes[index] & mask;
                    int probes = 0;
                    while (seen[slot] is var occupied and > 0)
                    {
                        int other = occupied - 1;
                        if (keyHashes[other] == keyHashes[index] && string.Equals(names[other], names[index], StringComparison.Ordinal))
                        {
                            duplicate = names[index];
                            return true;
                        }

                        if (++probes == maxProbes)
                            return HasDuplicateKeyBySorting(names, hashBottom, out duplicate);

                        slot = (slot + 1) & mask;
                    }

                    seen[slot] = index + 1;
                }

                return false;
            }
            finally
            {
                Array.Clear(seen, 0, size);
            }
        }

        // Reuse the table as a sortable list of indexes. Collisions cannot force quadratic probing, and sorting
        // the indexes preserves the decoded map's key order and its correspondence with the values. Equal keys have
        // equal hashes, so ordering by hash first still puts duplicates next to each other while most comparisons
        // stay integer ones.
        private bool HasDuplicateKeyBySorting(string[] names, int hashBottom, out string? duplicate)
        {
            Span<int> indexes = seen.AsSpan(0, names.Length);
            for (int index = 0; index < indexes.Length; index++)
                indexes[index] = index;

            indexes.Sort(new KeyIndexComparer(names, hashes, hashBottom));
            for (int index = 1; index < indexes.Length; index++)
            {
                string key = names[indexes[index]];
                if (string.Equals(key, names[indexes[index - 1]], StringComparison.Ordinal))
                {
                    duplicate = key;
                    return true;
                }
            }

            duplicate = null;
            return false;
        }

        private readonly struct KeyIndexComparer(string[] names, uint[] hashes, int hashBottom) : IComparer<int>
        {
            public int Compare(int left, int right)
            {
                int byHash = hashes[hashBottom + left].CompareTo(hashes[hashBottom + right]);
                return byHash != 0 ? byHash : string.CompareOrdinal(names[left], names[right]);
            }
        }

        // Field names, decoded once per frame. The probe length is capped, so even keys crafted to collide cost a
        // bounded number of comparisons each; past the cap a key is simply decoded without caching.
        public string Key(ReadOnlySpan<byte> bytes)
        {
            uint hash = KeyHash(bytes, Seed);

            if (HashTop == hashes.Length)
                Array.Resize(ref hashes, hashes.Length * 2);
            hashes[HashTop++] = hash;

            if (bytes.IsEmpty)
                return string.Empty;

            for (int probe = 0; probe < MaxProbes; probe++)
            {
                int slot = (int)((hash + (uint)probe) & (KeyCacheSize - 1));
                if (cachedBytes[slot] is not { } existing)
                {
                    string text = Utf8(bytes);
                    if (cachedCount < KeyCacheLimit)
                    {
                        cachedBytes[slot] = bytes.ToArray();
                        cachedKeys[slot] = text;
                        cachedCount++;
                    }

                    return text;
                }

                if (bytes.SequenceEqual(existing))
                    return cachedKeys[slot] ?? string.Empty;
            }

            return Utf8(bytes);
        }

        // Clears what the last decode left (so no strings or values outlive it) and says whether to keep this scratch.
        public bool Reset()
        {
            HashTop = 0;
            if (cachedCount > 0)
            {
                Array.Clear(cachedBytes);
                Array.Clear(cachedKeys);
                cachedCount = 0;
            }

            return hashes.Length <= MaxRetainedTable && seen.Length <= MaxRetainedTable;
        }
    }
}
