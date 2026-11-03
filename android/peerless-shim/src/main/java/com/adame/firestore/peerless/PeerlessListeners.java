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
    public static ListenerRegistration listen(@NonNull Query query, @NonNull FrameSink sink, int delivery) {
        Encoder encoder = new Encoder(sink, delivery);
        return query.addSnapshotListener(MetadataChanges.EXCLUDE, new EventListener<QuerySnapshot>() {
            @Override
            public void onEvent(@Nullable QuerySnapshot snapshot, @Nullable FirebaseFirestoreException error) {
                long start = System.nanoTime();
                long cpu = Debug.threadCpuTimeNanos();
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
    public static ListenerRegistration listen(@NonNull DocumentReference document, @NonNull FrameSink sink, int delivery) {
        Encoder encoder = new Encoder(sink, delivery);
        return document.addSnapshotListener(MetadataChanges.EXCLUDE, new EventListener<DocumentSnapshot>() {
            @Override
            public void onEvent(@Nullable DocumentSnapshot snapshot, @Nullable FirebaseFirestoreException error) {
                long start = System.nanoTime();
                long cpu = Debug.threadCpuTimeNanos();
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
        final FrameWriter writer = new FrameWriter();
        private final FrameSink sink;
        private final int delivery;
        @Nullable
        private ByteBuffer direct;

        Encoder(FrameSink sink, int delivery) {
            if (delivery != DELIVERY_BYTE_ARRAY && delivery != DELIVERY_DIRECT_BUFFER)
                throw new IllegalArgumentException("Unknown delivery " + delivery);
            this.sink = sink;
            this.delivery = delivery;
        }

        void deliver(long start, long cpuAtStart) {
            long encodeNanos = System.nanoTime() - start;
            long encodeCpuNanos = Debug.threadCpuTimeNanos() - cpuAtStart;
            int length = writer.length();
            if (delivery == DELIVERY_BYTE_ARRAY) {
                byte[] copy;
                try {
                    copy = writer.toByteArray();
                } catch (OutOfMemoryError failure) {
                    encodingFailed(failure);
                    return;
                }
                sink.onFrame(copy, length, start, cpuAtStart, encodeNanos, encodeCpuNanos);
                return;
            }

            ByteBuffer buffer = direct;
            if (buffer == null || buffer.capacity() < length) {
                // The next power of two, without overflowing: above 2^30 the buffer is exactly the frame's size.
                int capacity = length > (1 << 30)
                        ? length
                        : Math.max(64 * 1024, Integer.highestOneBit(Math.max(1, length - 1)) << 1);
                try {
                    buffer = ByteBuffer.allocateDirect(capacity).order(ByteOrder.LITTLE_ENDIAN);
                } catch (RuntimeException | OutOfMemoryError failure) {
                    encodingFailed(failure);
                    return;
                }
                direct = buffer;
                sink.onBuffer(buffer);
            }
            buffer.clear();
            buffer.put(writer.buffer(), 0, length);
            sink.onFrame(null, length, start, cpuAtStart, encodeNanos, encodeCpuNanos);
        }

        // .NET stops the listener when it hears this. Exceptions thrown by .NET callbacks are not caught here: like an
        // exception from an ordinary Firestore listener, they propagate and crash the process.
        void encodingFailed(Throwable failure) {
            writer.reset();
            String message = failure.getMessage();
            sink.onError(ERROR_ENCODING_FAILED, failure.getClass().getSimpleName() + (message == null ? "" : ": " + message));
        }

        void fail(@Nullable FirebaseFirestoreException error) {
            if (error == null) {
                sink.onError(FirebaseFirestoreException.Code.UNKNOWN.value(), "Firestore sent neither a snapshot nor an error.");
            } else {
                String message = error.getMessage();
                sink.onError(error.getCode().value(), message == null ? error.getCode().name() : message);
            }
        }
    }
}
