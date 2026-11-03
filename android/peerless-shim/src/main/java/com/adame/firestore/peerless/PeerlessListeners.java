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
                encoder.writer.writeQuery(snapshot);
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
                encoder.writer.writeDocumentSnapshot(snapshot);
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
                sink.onFrame(writer.toByteArray(), length, start, cpuAtStart, encodeNanos, encodeCpuNanos);
                return;
            }

            ByteBuffer buffer = direct;
            if (buffer == null || buffer.capacity() < length) {
                int capacity = Math.max(64 * 1024, Integer.highestOneBit(Math.max(1, length - 1)) << 1);
                buffer = ByteBuffer.allocateDirect(capacity).order(ByteOrder.LITTLE_ENDIAN);
                direct = buffer;
                sink.onBuffer(buffer);
            }
            buffer.clear();
            buffer.put(writer.buffer(), 0, length);
            sink.onFrame(null, length, start, cpuAtStart, encodeNanos, encodeCpuNanos);
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
