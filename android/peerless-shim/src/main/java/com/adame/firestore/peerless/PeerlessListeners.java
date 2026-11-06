package com.adame.firestore.peerless;

import android.os.Debug;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;

import com.google.firebase.firestore.DocumentReference;
import com.google.firebase.firestore.DocumentSnapshot;
import com.google.firebase.firestore.EventListener;
import com.google.firebase.firestore.FirebaseFirestoreException;
import com.google.firebase.firestore.ListenerRegistration;
import com.google.firebase.firestore.MetadataChanges;
import com.google.firebase.firestore.Query;
import com.google.firebase.firestore.QuerySnapshot;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/**
 * Registers Firestore listeners whose snapshots are encoded here, on the callback thread, and delivered to .NET as one
 * frame per snapshot. Callbacks run where Firestore runs them by default (the main thread), the same as an ordinary
 * {@code addSnapshotListener}.
 */
public final class PeerlessListeners {
    /** Deliver each frame as a new {@code byte[]}, which .NET copies into a managed array. */
    public static final int DELIVERY_BYTE_ARRAY = 0;

    /** Deliver each frame in a reused direct buffer whose address .NET reads in place: no copy, no object per frame. */
    public static final int DELIVERY_DIRECT_BUFFER = 1;

    /** {@link FrameSink#onError} code for the shim's own failure to encode a snapshot (Firestore's codes are ≥ 0). */
    public static final int ERROR_ENCODING_FAILED = -1;

    private PeerlessListeners() {
    }

    @NonNull
    public static ListenerRegistration listen(@NonNull Query query, @NonNull FrameSink sink, int delivery, boolean timed) {
        Encoder encoder = new Encoder(sink, delivery, timed);
        return query.addSnapshotListener(MetadataChanges.EXCLUDE, new EventListener<QuerySnapshot>() {
            @Override
            public void onEvent(@Nullable QuerySnapshot snapshot, @Nullable FirebaseFirestoreException error) {
                long start = encoder.timed ? System.nanoTime() : 0;
                long cpu = encoder.timed ? Debug.threadCpuTimeNanos() : 0;
                if (snapshot == null) {
                    encoder.fail(error);
                    return;
                }
                try {
                    encoder.writer.writeQuery(snapshot);
                } catch (RuntimeException | OutOfMemoryError failure) {
                    encoder.encodingFailed(failure);
                    return;
                }
                encoder.deliver(start, cpu);
            }
        });
    }

    @NonNull
    public static ListenerRegistration listen(@NonNull DocumentReference document, @NonNull FrameSink sink, int delivery,
                                              boolean timed) {
        Encoder encoder = new Encoder(sink, delivery, timed);
        return document.addSnapshotListener(MetadataChanges.EXCLUDE, new EventListener<DocumentSnapshot>() {
            @Override
            public void onEvent(@Nullable DocumentSnapshot snapshot, @Nullable FirebaseFirestoreException error) {
                long start = encoder.timed ? System.nanoTime() : 0;
                long cpu = encoder.timed ? Debug.threadCpuTimeNanos() : 0;
                if (snapshot == null) {
                    encoder.fail(error);
                    return;
                }
                try {
                    encoder.writer.writeDocumentSnapshot(snapshot);
                } catch (RuntimeException | OutOfMemoryError failure) {
                    encoder.encodingFailed(failure);
                    return;
                }
                encoder.deliver(start, cpu);
            }
        });
    }

    private static final class Encoder {
        // Buffers above this size are shrunk once frames have been much smaller for a while, so one huge snapshot
        // doesn't pin its memory (the writer's array plus the direct buffer) for the rest of the listener's life.
        static final int SHRINK_ABOVE = 1024 * 1024;
        static final int SHRINK_AFTER_FRAMES = 8;

        final FrameWriter writer = new FrameWriter();
        final boolean timed;
        private final FrameSink sink;
        private final int delivery;
        @Nullable
        private ByteBuffer direct;
        private int smallFrames;
        private int largestRecentFrame;

        Encoder(FrameSink sink, int delivery, boolean timed) {
            if (delivery != DELIVERY_BYTE_ARRAY && delivery != DELIVERY_DIRECT_BUFFER)
                throw new IllegalArgumentException("Unknown delivery " + delivery);
            this.sink = sink;
            this.delivery = delivery;
            this.timed = timed;
        }

        void deliver(long start, long cpuAtStart) {
            long encodeNanos = timed ? System.nanoTime() - start : 0;
            long encodeCpuNanos = timed ? Debug.threadCpuTimeNanos() - cpuAtStart : 0;
            int length = writer.length();
            if (delivery == DELIVERY_BYTE_ARRAY) {
                byte[] copy;
                try {
                    copy = writer.toByteArray();
                } catch (OutOfMemoryError failure) {
                    encodingFailed(failure);
                    return;
                }
                send(copy, length, start, cpuAtStart, encodeNanos, encodeCpuNanos);
                shrinkIfIdle(length);
                return;
            }

            ByteBuffer buffer = direct;
            if (buffer == null || buffer.capacity() < length) {
                try {
                    buffer = ByteBuffer.allocateDirect(capacityFor(length)).order(ByteOrder.LITTLE_ENDIAN);
                } catch (RuntimeException | OutOfMemoryError failure) {
                    encodingFailed(failure);
                    return;
                }
                direct = buffer;
                sink.onBuffer(buffer);
            }
            buffer.clear();
            buffer.put(writer.buffer(), 0, length);
            send(null, length, start, cpuAtStart, encodeNanos, encodeCpuNanos);
            shrinkIfIdle(length);
        }

        private void send(@Nullable byte[] frame, int length, long start, long cpu, long encodeNanos, long encodeCpuNanos) {
            if (timed)
                sink.onTimedFrame(frame, length, start, cpu, encodeNanos, encodeCpuNanos);
            else
                sink.onFrame(frame, length);
        }

        // After SHRINK_AFTER_FRAMES frames in a row that each used under a quarter of a buffer above SHRINK_ABOVE,
        // the writer's array is cut to fit the largest of them and the direct buffer is replaced at once by one of the
        // same fitting size, handed to .NET, which releases its reference to the old one then (not at the next frame,
        // which may never come). If that allocation fails the buffer is only dropped, and the next frame replaces it.
        private void shrinkIfIdle(int length) {
            int capacity = Math.max(writer.capacity(), direct == null ? 0 : direct.capacity());
            if (capacity <= SHRINK_ABOVE || length >= capacity / 4) {
                smallFrames = 0;
                largestRecentFrame = 0;
                return;
            }
            largestRecentFrame = Math.max(largestRecentFrame, length);
            if (++smallFrames < SHRINK_AFTER_FRAMES)
                return;
            int fitting = capacityFor(largestRecentFrame);
            writer.shrinkTo(fitting);
            if (direct != null && direct.capacity() > SHRINK_ABOVE) {
                direct = null;
                ByteBuffer smaller;
                try {
                    smaller = ByteBuffer.allocateDirect(fitting).order(ByteOrder.LITTLE_ENDIAN);
                } catch (RuntimeException | OutOfMemoryError failure) {
                    smaller = null;
                }
                if (smaller != null) {
                    direct = smaller;
                    sink.onBuffer(smaller);
                }
            }
            smallFrames = 0;
            largestRecentFrame = 0;
        }

        // The next power of two, at least 64 KB, without overflowing: above 2^30 it is exactly the frame's size.
        static int capacityFor(int length) {
            return length > (1 << 30)
                    ? length
                    : Math.max(64 * 1024, Integer.highestOneBit(Math.max(1, length - 1)) << 1);
        }

        // .NET stops the listener when it hears this. Exceptions thrown by .NET callbacks are not caught here: like an
        // exception from an ordinary Firestore listener, they propagate and crash the process.
        void encodingFailed(Throwable failure) {
            writer.reset();
            String message = failure.getMessage();
            sink.onError(ERROR_ENCODING_FAILED, failure.getClass().getSimpleName() + (message == null ? "" : ": " + message),
                    failure);
        }

        void fail(@Nullable FirebaseFirestoreException error) {
            if (error == null) {
                sink.onError(FirebaseFirestoreException.Code.UNKNOWN.value(), "Firestore sent neither a snapshot nor an error.", null);
            } else {
                String message = error.getMessage();
                sink.onError(error.getCode().value(), message == null ? error.getCode().name() : message, error);
            }
        }
    }
}
