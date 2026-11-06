package com.adame.firestore.peerless

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class FrameWriterTest {
    @Test
    fun stringsAreUtf8WithLoneSurrogatesReplaced() {
        assertEquals(bytes(0x01, 0x61), encodeString("a"))
        assertEquals(bytes(0x02, 0xC3, 0xA9), encodeString("é"))
        assertEquals(bytes(0x03, 0xE2, 0x9C, 0x93), encodeString("✓"))
        assertEquals(bytes(0x04, 0xF0, 0x9F, 0x98, 0x80), encodeString("😀"))
        // A lone high surrogate, then a lone low surrogate: each becomes U+FFFD, matching .NET's Encoding.UTF8.
        assertEquals(bytes(0x07, 0xEF, 0xBF, 0xBD, 0xEF, 0xBF, 0xBD, 0x78), encodeString("\uDE00\uD83Dx"))
    }

    @Test
    fun varintsUseTheShortestForm() {
        assertEquals(bytes(0x00), encode { it.writeVarint(0) }.toList())
        assertEquals(bytes(0x7F), encode { it.writeVarint(127) }.toList())
        assertEquals(bytes(0x80, 0x01), encode { it.writeVarint(128) }.toList())
        assertEquals(bytes(0xFF, 0xFF, 0xFF, 0xFF, 0x07), encode { it.writeVarint(Int.MAX_VALUE) }.toList())
    }

    @Test
    fun integersAndDoublesKeepTheirKinds() {
        assertEquals(FrameWriter.TAG_INTEGER.toInt(), encode { it.writeValue(3L) }[0].toInt())
        assertEquals(FrameWriter.TAG_DOUBLE.toInt(), encode { it.writeValue(3.0) }[0].toInt())
    }

    @Test
    fun anUnrecognisedValueIsWrittenAsUnsupportedWithItsClassName() {
        val frame = encode { it.writeValue(7) } // java.lang.Integer: not something getData() returns.
        assertEquals(FrameWriter.TAG_UNSUPPORTED.toInt(), frame[0].toInt())
        assertEquals("java.lang.Integer", String(frame.copyOfRange(2, frame.size), Charsets.UTF_8))
    }

    @Test
    fun theBufferGrowsPastItsInitialCapacity() {
        val writer = FrameWriter(64)
        val text = "x".repeat(100_000)
        writer.writeString(text)
        assertEquals(3 + 100_000, writer.length())
    }

    @Test
    fun growingPastTheCapThrowsInsteadOfOverflowing() {
        val writer = FrameWriter(64, 1_000)
        writer.writeString("x".repeat(900))
        assertThrows(FrameWriter.FrameTooLargeException::class.java) { writer.writeString("y".repeat(200)) }
    }

    @Test
    fun aStringTooLargeForAFrameIsRejectedBeforeItsLengthOverflows() {
        val writer = FrameWriter(64, 1_000)
        // 400 chars x 3 UTF-8 bytes is past the cap; the length is computed as a long, so it can't wrap.
        assertThrows(FrameWriter.FrameTooLargeException::class.java) { writer.writeString("✓".repeat(400)) }
        assertEquals(0, writer.length())
    }

    @Test
    fun aGrownBufferCanBeShrunkBetweenFrames() {
        val writer = FrameWriter(64)
        writer.writeString("x".repeat(3_000_000))
        val grown = writer.capacity()
        writer.reset()
        writer.shrinkTo(64 * 1024)
        assertEquals(64 * 1024, writer.capacity())
        assertEquals(0, writer.length())
        writer.writeString("still works")
        assertEquals(12, writer.length())
        assert(grown > 3_000_000)
    }

    @Test
    fun keysThatOnlyDifferInLoneSurrogatesFailTheFrameInsteadOfCollidingInIt() {
        val colliding = linkedMapOf<String, Any?>("\uD800a" to 1L, "\uDBFFa" to 2L) // both become "\uFFFDa"
        assertThrows(FrameWriter.KeyCollisionException::class.java) { FrameWriter().writeMapBody(colliding) }
    }

    @Test
    fun aLiteralReplacementCharacterKeyCollidesWithALoneSurrogateKey() {
        // "\uFFFDa" is a real key; "\uD800a" is written as the same bytes.
        val colliding = linkedMapOf<String, Any?>("\uFFFDa" to 1L, "\uD800a" to 2L)
        assertThrows(FrameWriter.KeyCollisionException::class.java) { FrameWriter().writeMapBody(colliding) }
    }

    @Test
    fun aCollisionInsideANestedMapFailsTheFrameToo() {
        val nested = linkedMapOf<String, Any?>("\uDC00x" to 1L, "\uDBFFx" to 2L)
        val outer = linkedMapOf<String, Any?>("fine" to 1L, "inner" to listOf(linkedMapOf<String, Any?>("deeper" to nested)))
        assertThrows(FrameWriter.KeyCollisionException::class.java) { FrameWriter().writeMapBody(outer) }
    }

    @Test
    fun aSingleLossyKeyOrOrdinaryKeysAreFine() {
        FrameWriter().writeMapBody(linkedMapOf<String, Any?>("\uD800a" to 1L, "b" to 2L))
        FrameWriter().writeMapBody(linkedMapOf<String, Any?>("😀" to 1L, "a" to 2L))
        assertEquals("\uFFFDa😀", FrameWriter.withLoneSurrogatesReplaced("\uDC00a😀"))
    }

    private fun encodeString(value: String) = encode { it.writeString(value) }.toList()

    private fun encode(write: (FrameWriter) -> Unit): ByteArray = FrameWriter().also(write).toByteArray()

    private fun bytes(vararg values: Int) = values.map { it.toByte() }
}
