package com.adame.firestore.peerless

import com.google.firebase.Timestamp
import com.google.firebase.firestore.Blob
import com.google.firebase.firestore.FieldValue
import com.google.firebase.firestore.GeoPoint
import org.junit.Assert.assertEquals
import org.junit.Test
import java.io.File

/**
 * The Java encoder must reproduce the golden frames in fixtures/wire byte for byte. The inputs mirror
 * fixtures/wire/README.md (and GoldenFixtures.cs), in the same key order.
 */
class GoldenFixtureTest {
    private val fixtures = File(
        System.getProperty("peerless.fixtures") ?: error("The peerless.fixtures system property is not set."),
    )

    @Test
    fun documentScalars() {
        val writer = FrameWriter()
        writer.writeHeader(FrameWriter.KIND_DOCUMENT)
        writer.writeMetadataSection(false, false)
        writer.beginSection(FrameWriter.SECTION_DOCUMENTS)
        writer.writeVarint(1)
        writer.writeString("fixtures/scalars")
        writer.writeByte(0x01)

        // A DocumentReference can't be built without a FirebaseFirestore instance, so this map is written entry by
        // entry with the writer's own building blocks; the reference goes through writeReference, which is what
        // writeValue calls for a DocumentReference.
        val fields = linkedMapOf<String, Any?>(
            "null" to null,
            "false" to false,
            "true" to true,
            "zero" to 0L,
            "int-min" to Long.MIN_VALUE,
            "int-max" to Long.MAX_VALUE,
            "int-negative" to -42L,
            "double-whole" to 3.0,
            "double-fraction" to -1.5,
            "double-negative-zero" to -0.0,
            "double-nan" to Double.NaN,
            "double-infinity" to Double.POSITIVE_INFINITY,
            "string-empty" to "",
            "string-ascii" to "hello",
            "string-unicode" to "héllo wörld ✓ 😀",
            "bytes" to Blob.fromBytes(byteArrayOf(0x00, 0x01, 0x7F, 0x80.toByte(), 0xFF.toByte())),
            "bytes-empty" to Blob.fromBytes(byteArrayOf()),
            "timestamp" to Timestamp(1_767_225_600L, 123_456_789),
            "timestamp-pre-epoch" to Timestamp(-1L, 999_999_999),
            "geopoint" to GeoPoint(42.3314, -83.0458),
            "reference" to Reference("users/user-001"),
            "unsupported" to FieldValue.vector(doubleArrayOf(1.0)),
        )
        writer.writeVarint(fields.size)
        for ((key, value) in fields) {
            writer.writeString(key)
            if (value is Reference) writer.writeReference(value.path) else writer.writeValue(value)
        }
        writer.endSection()

        assertFixture("document-scalars", writer)
    }

    @Test
    fun documentMissing() {
        val writer = FrameWriter()
        writer.writeHeader(FrameWriter.KIND_DOCUMENT)
        writer.writeMetadataSection(true, false)
        writer.beginSection(FrameWriter.SECTION_DOCUMENTS)
        writer.writeVarint(1)
        writer.writeDocument("fixtures/missing", false, true, false, null)
        writer.endSection()

        assertFixture("document-missing", writer)
    }

    @Test
    fun queryNested() {
        val nested = linkedMapOf<String, Any?>(
            "level" to 1L,
            "deeper" to linkedMapOf<String, Any?>(
                "list" to listOf(1L, 2.5, "three", null, true, linkedMapOf("k" to "v"), listOf(listOf<Any?>())),
            ),
        )
        val writer = FrameWriter()
        writer.writeHeader(FrameWriter.KIND_QUERY)
        writer.writeMetadataSection(false, true)
        writer.beginSection(FrameWriter.SECTION_DOCUMENTS)
        writer.writeVarint(2)
        writer.writeDocument(
            "items/a", true, false, true,
            linkedMapOf<String, Any?>(
                "tags" to listOf("x", "y"),
                "empty-array" to listOf<Any?>(),
                "empty-map" to linkedMapOf<String, Any?>(),
                "nested" to nested,
            ),
        )
        writer.writeDocument(
            "items/b", true, false, false,
            linkedMapOf<String, Any?>("count" to 128L, "long-string" to "a".repeat(200)),
        )
        writer.endSection()

        assertFixture("query-nested", writer)
    }

    @Test
    fun queryEmpty() {
        val writer = FrameWriter()
        writer.writeHeader(FrameWriter.KIND_QUERY)
        writer.writeMetadataSection(true, false)
        writer.beginSection(FrameWriter.SECTION_DOCUMENTS)
        writer.writeVarint(0)
        writer.endSection()

        assertFixture("query-empty", writer)
    }

    @Test
    fun aReusedWriterProducesTheSameFrameAgain() {
        val writer = FrameWriter(64)
        repeat(3) {
            writer.reset()
            writer.writeHeader(FrameWriter.KIND_QUERY)
            writer.writeMetadataSection(true, false)
            writer.beginSection(FrameWriter.SECTION_DOCUMENTS)
            writer.writeVarint(0)
            writer.endSection()
            assertFixture("query-empty", writer)
        }
    }

    private data class Reference(val path: String)

    private fun assertFixture(name: String, writer: FrameWriter) {
        val expected = File(fixtures, "$name.bin").readBytes()
        assertEquals(name, expected.toHex(), writer.toByteArray().toHex())
    }

    private fun ByteArray.toHex() = joinToString("") { "%02X".format(it) }
}
