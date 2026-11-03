package com.adame.firestore.peerless;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;

import com.google.firebase.Timestamp;
import com.google.firebase.firestore.Blob;
import com.google.firebase.firestore.DocumentReference;
import com.google.firebase.firestore.DocumentSnapshot;
import com.google.firebase.firestore.GeoPoint;
import com.google.firebase.firestore.QuerySnapshot;
import com.google.firebase.firestore.SnapshotMetadata;

import java.util.List;
import java.util.Map;

/**
 * Encodes Firestore snapshots into Peerless wire format 1.0 frames (docs/wire-format.md). One writer is reused for
 * every snapshot of a listener: {@link #reset()} keeps the buffer, so steady-state encoding allocates nothing here.
 * Not thread-safe; a listener's callbacks are serialized on its executor.
 */
public final class FrameWriter {
    static final byte MAJOR_VERSION = 1;
    static final byte MINOR_VERSION = 0;

    static final byte KIND_QUERY = 1;
    static final byte KIND_DOCUMENT = 2;

    static final byte SECTION_METADATA = 1;
    static final byte SECTION_DOCUMENTS = 2;

    static final byte TAG_NULL = 0;
    static final byte TAG_FALSE = 1;
    static final byte TAG_TRUE = 2;
    static final byte TAG_INTEGER = 3;
    static final byte TAG_DOUBLE = 4;
    static final byte TAG_STRING = 5;
    static final byte TAG_BYTES = 6;
    static final byte TAG_TIMESTAMP = 7;
    static final byte TAG_GEO_POINT = 8;
    static final byte TAG_REFERENCE = 9;
    static final byte TAG_ARRAY = 10;
    static final byte TAG_MAP = 11;
    static final byte TAG_UNSUPPORTED = 12;

    private byte[] buffer;
    private int length;
    private int sectionStart = -1;

    public FrameWriter() {
        this(16 * 1024);
    }

    public FrameWriter(int initialCapacity) {
        buffer = new byte[Math.max(64, initialCapacity)];
    }

    /** Encodes a whole query snapshot: every document, in query order. */
    public void writeQuery(@NonNull QuerySnapshot snapshot) {
        reset();
        writeHeader(KIND_QUERY);
        writeMetadataSection(snapshot.getMetadata());
        List<DocumentSnapshot> documents = snapshot.getDocuments();
        beginSection(SECTION_DOCUMENTS);
        writeVarint(documents.size());
        for (int index = 0; index < documents.size(); index++)
            writeDocument(documents.get(index));
        endSection();
    }

    /** Encodes a document snapshot, which may be of a document that does not exist. */
    public void writeDocumentSnapshot(@NonNull DocumentSnapshot snapshot) {
        reset();
        writeHeader(KIND_DOCUMENT);
        writeMetadataSection(snapshot.getMetadata());
        beginSection(SECTION_DOCUMENTS);
        writeVarint(1);
        writeDocument(snapshot);
        endSection();
    }

    private void writeMetadataSection(SnapshotMetadata metadata) {
        writeMetadataSection(metadata.isFromCache(), metadata.hasPendingWrites());
    }

    // getData() uses the SDK's default ServerTimestampBehavior (NONE), the same as an ordinary app.
    private void writeDocument(DocumentSnapshot document) {
        SnapshotMetadata metadata = document.getMetadata();
        writeDocument(document.getReference().getPath(), document.exists(), metadata.isFromCache(),
                metadata.hasPendingWrites(), document.exists() ? document.getData() : null);
    }

    // ---- Building blocks, public so tests can build frames from plain Java values. ----

    public void reset() {
        length = 0;
        sectionStart = -1;
    }

    public void writeHeader(byte kind) {
        ensure(8);
        buffer[length++] = 'P';
        buffer[length++] = 'L';
        buffer[length++] = 'S';
        buffer[length++] = 'F';
        buffer[length++] = MAJOR_VERSION;
        buffer[length++] = MINOR_VERSION;
        buffer[length++] = kind;
        buffer[length++] = 0;
    }

    public void writeMetadataSection(boolean fromCache, boolean hasPendingWrites) {
        beginSection(SECTION_METADATA);
        writeByte((fromCache ? 0x01 : 0) | (hasPendingWrites ? 0x02 : 0));
        endSection();
    }

    /** Starts a section; its u32 length is filled in by {@link #endSection()}. */
    public void beginSection(byte id) {
        if (sectionStart >= 0)
            throw new IllegalStateException("Sections do not nest.");
        writeByte(id);
        ensure(4);
        sectionStart = length;
        length += 4;
    }

    public void endSection() {
        if (sectionStart < 0)
            throw new IllegalStateException("No section is open.");
        int size = length - sectionStart - 4;
        buffer[sectionStart] = (byte) size;
        buffer[sectionStart + 1] = (byte) (size >>> 8);
        buffer[sectionStart + 2] = (byte) (size >>> 16);
        buffer[sectionStart + 3] = (byte) (size >>> 24);
        sectionStart = -1;
    }

    public void writeDocument(@NonNull String path, boolean exists, boolean fromCache, boolean hasPendingWrites,
                              @Nullable Map<String, Object> data) {
        writeString(path);
        writeByte((exists ? 0x01 : 0) | (fromCache ? 0x02 : 0) | (hasPendingWrites ? 0x04 : 0));
        if (!exists)
            return;
        if (data == null) {
            writeVarint(0);
            return;
        }
        writeMapBody(data);
    }

    public void writeMapBody(@NonNull Map<String, ?> map) {
        writeVarint(map.size());
        for (Map.Entry<String, ?> entry : map.entrySet()) {
            writeString(entry.getKey());
            writeValue(entry.getValue());
        }
    }

    /**
     * Writes any value {@code DocumentSnapshot.getData()} can return. Integers and doubles keep their kinds; anything
     * unrecognised becomes an explicit unsupported value carrying its class name, never a guess.
     */
    public void writeValue(@Nullable Object value) {
        if (value == null) {
            writeByte(TAG_NULL);
        } else if (value instanceof Boolean) {
            writeByte((Boolean) value ? TAG_TRUE : TAG_FALSE);
        } else if (value instanceof Long) {
            writeByte(TAG_INTEGER);
            writeLong((Long) value);
        } else if (value instanceof Double) {
            writeByte(TAG_DOUBLE);
            writeLong(Double.doubleToRawLongBits((Double) value));
        } else if (value instanceof String) {
            writeByte(TAG_STRING);
            writeString((String) value);
        } else if (value instanceof Map) {
            writeByte(TAG_MAP);
            @SuppressWarnings("unchecked") Map<String, ?> map = (Map<String, ?>) value;
            writeMapBody(map);
        } else if (value instanceof List) {
            List<?> list = (List<?>) value;
            writeByte(TAG_ARRAY);
            writeVarint(list.size());
            for (int index = 0; index < list.size(); index++)
                writeValue(list.get(index));
        } else if (value instanceof Timestamp) {
            Timestamp timestamp = (Timestamp) value;
            writeByte(TAG_TIMESTAMP);
            writeLong(timestamp.getSeconds());
            writeInt(timestamp.getNanoseconds());
        } else if (value instanceof DocumentReference) {
            writeReference(((DocumentReference) value).getPath());
        } else if (value instanceof GeoPoint) {
            GeoPoint point = (GeoPoint) value;
            writeByte(TAG_GEO_POINT);
            writeLong(Double.doubleToRawLongBits(point.getLatitude()));
            writeLong(Double.doubleToRawLongBits(point.getLongitude()));
        } else if (value instanceof Blob) {
            byte[] bytes = ((Blob) value).toBytes();
            writeByte(TAG_BYTES);
            writeVarint(bytes.length);
            ensure(bytes.length);
            System.arraycopy(bytes, 0, buffer, length, bytes.length);
            length += bytes.length;
        } else {
            writeUnsupported(value.getClass().getName());
        }
    }

    public void writeReference(@NonNull String path) {
        writeByte(TAG_REFERENCE);
        writeString(path);
    }

    public void writeUnsupported(@NonNull String typeName) {
        writeByte(TAG_UNSUPPORTED);
        writeString(typeName);
    }

    /** UTF-8, length-prefixed. A lone surrogate becomes U+FFFD, as in .NET (Java's own encoder writes '?'). */
    public void writeString(@NonNull String value) {
        int chars = value.length();
        int bytes = 0;
        boolean ascii = true;
        for (int index = 0; index < chars; index++) {
            char c = value.charAt(index);
            if (c < 0x80) {
                bytes += 1;
            } else {
                ascii = false;
                if (c < 0x800) {
                    bytes += 2;
                } else if (Character.isHighSurrogate(c) && index + 1 < chars
                        && Character.isLowSurrogate(value.charAt(index + 1))) {
                    bytes += 4;
                    index++;
                } else {
                    bytes += 3; // BMP character, or a lone surrogate written as U+FFFD.
                }
            }
        }

        writeVarint(bytes);
        ensure(bytes);
        byte[] out = buffer;
        int position = length;
        if (ascii) {
            for (int index = 0; index < chars; index++)
                out[position++] = (byte) value.charAt(index);
        } else {
            for (int index = 0; index < chars; index++) {
                int c = value.charAt(index);
                if (c < 0x80) {
                    out[position++] = (byte) c;
                } else if (c < 0x800) {
                    out[position++] = (byte) (0xC0 | (c >> 6));
                    out[position++] = (byte) (0x80 | (c & 0x3F));
                } else if (Character.isSurrogate((char) c)) {
                    if (Character.isHighSurrogate((char) c) && index + 1 < chars
                            && Character.isLowSurrogate(value.charAt(index + 1))) {
                        int code = Character.toCodePoint((char) c, value.charAt(++index));
                        out[position++] = (byte) (0xF0 | (code >> 18));
                        out[position++] = (byte) (0x80 | ((code >> 12) & 0x3F));
                        out[position++] = (byte) (0x80 | ((code >> 6) & 0x3F));
                        out[position++] = (byte) (0x80 | (code & 0x3F));
                    } else {
                        out[position++] = (byte) 0xEF; // U+FFFD
                        out[position++] = (byte) 0xBF;
                        out[position++] = (byte) 0xBD;
                    }
                } else {
                    out[position++] = (byte) (0xE0 | (c >> 12));
                    out[position++] = (byte) (0x80 | ((c >> 6) & 0x3F));
                    out[position++] = (byte) (0x80 | (c & 0x3F));
                }
            }
        }
        length = position;
    }

    public void writeVarint(int value) {
        if (value < 0)
            throw new IllegalArgumentException("A varint cannot be negative.");
        ensure(5);
        while (value >= 0x80) {
            buffer[length++] = (byte) (value | 0x80);
            value >>>= 7;
        }
        buffer[length++] = (byte) value;
    }

    public void writeByte(int value) {
        ensure(1);
        buffer[length++] = (byte) value;
    }

    public void writeInt(int value) {
        ensure(4);
        buffer[length++] = (byte) value;
        buffer[length++] = (byte) (value >>> 8);
        buffer[length++] = (byte) (value >>> 16);
        buffer[length++] = (byte) (value >>> 24);
    }

    public void writeLong(long value) {
        ensure(8);
        for (int shift = 0; shift < 64; shift += 8)
            buffer[length++] = (byte) (value >>> shift);
    }

    /** The encoded frame occupies {@code buffer()[0 .. length())}. The array is reused by the next frame. */
    @NonNull
    public byte[] buffer() {
        return buffer;
    }

    public int length() {
        return length;
    }

    /** A copy of the encoded frame. */
    @NonNull
    public byte[] toByteArray() {
        byte[] copy = new byte[length];
        System.arraycopy(buffer, 0, copy, 0, length);
        return copy;
    }

    private void ensure(int extra) {
        if (length + extra <= buffer.length)
            return;
        int capacity = buffer.length;
        while (capacity < length + extra)
            capacity = capacity * 2;
        byte[] grown = new byte[capacity];
        System.arraycopy(buffer, 0, grown, 0, length);
        buffer = grown;
    }
}
