// Peerless iOS shim: listens to Firestore through the app's own FirebaseFirestore, encodes each snapshot into one
// wire format 1.0 frame (docs/wire-format.md) on Firestore's callback queue, and hands it to .NET in one call.
//
// A plain C API, so .NET reaches it through [DllImport("__Internal")] and function pointers: no registrar types and no
// managed peer per snapshot.
#ifndef PEERLESS_SHIM_H
#define PEERLESS_SHIM_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/// One encoded snapshot. `data` is valid only during the call: the listener reuses its buffer for the next frame.
typedef void (*peerless_frame_callback)(void *context, const uint8_t *data, size_t length);

/// The listener failed (Firestore stops it). `code` is the NSError code (Firestore's FIRFirestoreErrorCode when the
/// domain is FIRFirestoreErrorDomain, else 2 = unknown); `message` is UTF-8, valid only during the call.
typedef void (*peerless_error_callback)(void *context, int32_t code, const char *message);

/// Called once, on the listener's callback queue, after removal: no frame or error callback can follow it.
typedef void (*peerless_release_callback)(void *context);

/// Measurement of the frame being delivered. Clock: peerless_now_nanos.
typedef struct peerless_frame_timing {
    uint64_t start_nanos;       // when the Firestore callback began
    uint64_t start_cpu_nanos;   // the callback thread's CPU time then
    uint64_t encode_nanos;      // wall time spent encoding
    uint64_t encode_cpu_nanos;  // thread CPU time spent encoding
    uint64_t data_nanos;        // of encode_nanos, the time inside Firestore's -[FIRDocumentSnapshot data]
} peerless_frame_timing;

/// Listens to a FIRQuery (passed as its Objective-C object pointer). Returns the listener, or NULL if `query` isn't
/// a FIRQuery. Callbacks run on the Firestore instance's callback queue (the main queue by default).
void *peerless_listen_query(void *query, void *context, peerless_frame_callback on_frame, peerless_error_callback on_error);

/// Listens to a FIRDocumentReference. Same contract as peerless_listen_query.
void *peerless_listen_document(void *document, void *context, peerless_frame_callback on_frame, peerless_error_callback on_error);

/// Mutes the listener at once (no frame or error callback starts after this returns), removes it from Firestore, and
/// then calls `release` with the context on the callback queue, after any callback that was already running. The
/// listener pointer is invalid after this call.
void peerless_listener_remove(void *listener, peerless_release_callback release);

/// Fills in the timing of the frame being delivered. Only meaningful during a frame callback.
void peerless_listener_timing(void *listener, peerless_frame_timing *timing);

/// The shim's monotonic clock (CLOCK_UPTIME_RAW), in nanoseconds.
uint64_t peerless_now_nanos(void);

/// The calling thread's CPU time, in nanoseconds.
uint64_t peerless_thread_cpu_nanos(void);

#ifdef __cplusplus
}
#endif

#endif
