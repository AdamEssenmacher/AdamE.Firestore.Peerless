// Peerless iOS shim: listens to Firestore through the app's own FirebaseFirestore, encodes each snapshot into one
// wire format 1.0 frame (docs/wire-format.md) on Firestore's callback queue, and hands it to .NET in one call.
//
// A plain C API, so .NET reaches it through [DllImport("__Internal")] and function pointers: no registrar types and no
// managed peer per snapshot. Every symbol is prefixed adame_peerless_ (Objective-C classes: AdamEPeerless).
//
// Contract (the same as the Android shim's .NET listener):
// - Callbacks for one listener never overlap: each runs under the listener's lock, even on a concurrent callback
//   queue (GCD can't tell a concurrent queue from a serial one, so the shim doesn't try to reject one).
// - The first error is the last callback: the listener mutes itself (no callback starts after it), calls on_error, and
//   removes its Firestore registration once on_error has returned and the listener's lock is released.
// - After adame_peerless_listener_remove returns, no callback starts, and one that was running on another thread has
//   finished (remove waits for it). Called from inside a callback, on the callback's thread, remove returns at once
//   and the running callback is the last.
// - Listening waits for a first callback already in flight: the listen call takes the listener's lock to store the
//   registration, so if Firestore starts delivering on another thread before it returns, it waits for that callback
//   (the same reason Dispose waits). On the main queue, listening from the main thread, this can't happen.
// - `release` runs exactly once, when no callback can still use the context: inside remove, or, when remove is called
//   from inside a callback, right after that callback returns.
// - Firestore's own registration is never removed while the listener's lock is held. Firestore holds its listener
//   mutex while it calls the callback, which waits for our lock, so removing under our lock deadlocks (found by the
//   benchmark's dispose race and reproduced in tests/ListenerTests.m).
#ifndef ADAME_PEERLESS_SHIM_H
#define ADAME_PEERLESS_SHIM_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/// One encoded snapshot. `data` is valid only during the call: the listener reuses its buffer for the next frame.
typedef void (*adame_peerless_frame_callback)(void *context, const uint8_t *data, size_t length);

/// The listener failed and has stopped. `code` is Firestore's FIRFirestoreErrorCode (gRPC numbering) when the error is
/// in FIRFirestoreErrorDomain, 2 (unknown) for any other NSError, and -1 when the shim itself could not encode a
/// snapshot. `message` is UTF-8, valid only during the call.
typedef void (*adame_peerless_error_callback)(void *context, int32_t code, const char *message);

/// Releases the context once nothing can use it again (see the contract above).
typedef void (*adame_peerless_release_callback)(void *context);

/// The shim's code for "could not encode a snapshot" (a value it refuses, or a frame over 2 GB).
#define ADAME_PEERLESS_ENCODE_FAILED (-1)

/// Measurement of the frame being delivered. Clock: adame_peerless_now_nanos.
typedef struct adame_peerless_frame_timing {
    uint64_t start_nanos;       // when the Firestore callback began
    uint64_t start_cpu_nanos;   // the callback thread's CPU time then
    uint64_t encode_nanos;      // wall time spent encoding
    uint64_t encode_cpu_nanos;  // thread CPU time spent encoding
    uint64_t data_nanos;        // of encode_nanos, the time inside Firestore's -[FIRDocumentSnapshot data]
} adame_peerless_frame_timing;

/// Listens to a FIRQuery (passed as its Objective-C object pointer). Returns the listener, or NULL if `query` isn't
/// a FIRQuery. Callbacks run on the Firestore instance's callback queue (the main queue unless the app chose
/// another), with metadata-only changes excluded, as addSnapshotListener: does by default. With `timed` nonzero the
/// listener records adame_peerless_frame_timing for each frame (a few clock reads, plus two per document); production
/// listeners pass 0 and take no clock reads.
/// If Firestore refuses the query (it raises for, e.g., limit(toLast:) without an orderBy, or a terminated instance),
/// returns NULL and writes the reason, UTF-8 and NUL-terminated, into `error` (up to `error_capacity` bytes). Nothing
/// is left registered and the context is not referenced.
void *adame_peerless_listen_query(void *query, void *context, adame_peerless_frame_callback on_frame,
                                  adame_peerless_error_callback on_error, int timed, char *error, size_t error_capacity);

/// Listens to a FIRDocumentReference. Same contract as adame_peerless_listen_query.
void *adame_peerless_listen_document(void *document, void *context, adame_peerless_frame_callback on_frame,
                                     adame_peerless_error_callback on_error, int timed, char *error,
                                     size_t error_capacity);

/// Stops the listener (see the contract above) and invalidates the listener pointer. Safe from any thread, and more
/// than once per listener is a caller error.
void adame_peerless_listener_remove(void *listener, adame_peerless_release_callback release);

/// Fills in the timing of the frame being delivered. Only meaningful during a frame callback of a timed listener.
/// Timing starts once the callback holds the listener's lock, so time spent waiting for it isn't counted.
void adame_peerless_listener_timing(void *listener, adame_peerless_frame_timing *timing);

/// The Firebase iOS SDK version the shim was compiled against (for example "12.18.0").
const char *adame_peerless_compiled_firebase_version(void);

/// The version of the FirebaseFirestoreInternal framework the app actually loaded, from its bundle's
/// CFBundleShortVersionString (Firestore has no public version API), or "" if it can't be read. An ABI break (a
/// symbol the shim uses gone) fails earlier, when dyld loads the app, before any version check can run.
const char *adame_peerless_runtime_firestore_version(void);

/// The shim's monotonic clock (CLOCK_UPTIME_RAW), in nanoseconds.
uint64_t adame_peerless_now_nanos(void);

/// The calling thread's CPU time, in nanoseconds.
uint64_t adame_peerless_thread_cpu_nanos(void);

#ifdef __cplusplus
}
#endif

#endif
