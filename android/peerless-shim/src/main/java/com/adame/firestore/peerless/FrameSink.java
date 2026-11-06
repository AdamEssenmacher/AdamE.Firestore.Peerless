package com.adame.firestore.peerless;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;

import java.nio.ByteBuffer;

/**
 * Receives encoded frames. .NET implements this once per listener; each snapshot costs exactly one call into .NET,
 * on the listener's thread (the main thread by default).
 */
public interface FrameSink {
    /**
     * Direct delivery only: the listener's direct buffer was created or replaced (it grows to fit the largest frame).
     * Called before the {@link #onFrame} that first uses it, so .NET can resolve and cache its address once.
     */
    void onBuffer(@NonNull ByteBuffer buffer);

    /**
     * One encoded snapshot.
     *
     * @param frame  the frame, in byte-array delivery; {@code null} in direct delivery, where the frame is bytes
     *               {@code [0, length)} of the last buffer passed to {@link #onBuffer}
     * @param length the frame's length in bytes
     */
    void onFrame(@Nullable byte[] frame, int length);

    /**
     * {@link #onFrame} with timings, for the benchmark only: sent instead of it when the listener was registered with
     * {@code timed}, so production frames don't pay for the clock reads.
     *
     * @param startNanos     {@code System.nanoTime()} when the Firestore callback began (CLOCK_MONOTONIC)
     * @param startCpuNanos  {@code Debug.threadCpuTimeNanos()} when the Firestore callback began
     * @param encodeNanos    wall time spent encoding, in nanoseconds
     * @param encodeCpuNanos this thread's CPU time spent encoding, in nanoseconds
     */
    void onTimedFrame(@Nullable byte[] frame, int length, long startNanos, long startCpuNanos, long encodeNanos,
                      long encodeCpuNanos);

    /**
     * The listener failed; Firestore stops it. {@code code} is {@code FirebaseFirestoreException.Code.value()}, the
     * gRPC status number (not the enum's ordinal), or {@link PeerlessListeners#ERROR_ENCODING_FAILED}.
     * {@code cause} is the exception behind it: Firestore's {@code FirebaseFirestoreException}, or what the encoder
     * threw. It is {@code null} only when Firestore sent neither a snapshot nor an error.
     */
    void onError(int code, @NonNull String message, @Nullable Throwable cause);
}
