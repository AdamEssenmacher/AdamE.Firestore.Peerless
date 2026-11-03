#import "PeerlessShim.h"
#import "AdamEPeerlessFrameWriter.h"

#import <pthread.h>
#import <stdatomic.h>
#import <time.h>

#import <FirebaseFirestoreInternal/FIRDocumentReference.h>
#import <FirebaseFirestoreInternal/FIRDocumentSnapshot.h>
#import <FirebaseFirestoreInternal/FIRFirestore.h>
#import <FirebaseFirestoreInternal/FIRFirestoreErrors.h>
#import <FirebaseFirestoreInternal/FIRListenerRegistration.h>
#import <FirebaseFirestoreInternal/FIRQuery.h>
#import <FirebaseFirestoreInternal/FIRQuerySnapshot.h>

#ifndef ADAME_PEERLESS_FIREBASE_VERSION
#error "Build with -DADAME_PEERLESS_FIREBASE_VERSION=\"x.y.z\" (ios/build-shim.sh does)."
#endif

uint64_t adame_peerless_now_nanos(void) {
    return clock_gettime_nsec_np(CLOCK_UPTIME_RAW);
}

uint64_t adame_peerless_thread_cpu_nanos(void) {
    return clock_gettime_nsec_np(CLOCK_THREAD_CPUTIME_ID);
}

const char *adame_peerless_compiled_firebase_version(void) {
    return ADAME_PEERLESS_FIREBASE_VERSION;
}

/// One listener: its writer (reused for every snapshot), the .NET callbacks and context, and the registration.
///
/// Everything a callback touches is guarded by `_lock`, held for the whole callback, so callbacks never overlap (even
/// on a concurrent queue, where they'd otherwise share the writer) and removal can wait for one in flight.
/// `_callbackThread` names the thread holding the lock for a callback, so removal from inside that callback is told
/// apart from removal on another thread (which must wait) without a recursive lock.
@interface AdamEPeerlessListener : NSObject
@end

@implementation AdamEPeerlessListener {
@public
    pthread_mutex_t _lock;
    _Atomic(uintptr_t) _callbackThread;
    AdamEPeerlessFrameWriter *_writer;
    void *_context;
    adame_peerless_frame_callback _onFrame;
    adame_peerless_error_callback _onError;
    id<FIRListenerRegistration> _registration;
    BOOL _stopped;
    adame_peerless_release_callback _deferredRelease; // removal from inside a callback: released when it returns
    BOOL _releaseDeferred;
    adame_peerless_frame_timing _timing;
}

- (instancetype)init {
    if ((self = [super init]))
        pthread_mutex_init(&_lock, NULL);
    return self;
}

- (void)dealloc {
    pthread_mutex_destroy(&_lock);
}

@end

static AdamEPeerlessListener *AdamEPeerlessNewListener(void *context, adame_peerless_frame_callback onFrame,
                                                       adame_peerless_error_callback onError) {
    AdamEPeerlessListener *listener = [AdamEPeerlessListener new];
    listener->_writer = [[AdamEPeerlessFrameWriter alloc] initWithCapacity:64 * 1024];
    listener->_context = context;
    listener->_onFrame = onFrame;
    listener->_onError = onError;
    atomic_init(&listener->_callbackThread, 0);
    return listener;
}

static inline uintptr_t AdamEPeerlessThisThread(void) {
    return (uintptr_t)pthread_self();
}

// Under the lock. Mutes the listener and removes its Firestore registration (which is safe on any thread).
static void AdamEPeerlessStopLocked(AdamEPeerlessListener *listener) {
    listener->_stopped = YES;
    [listener->_registration remove];
    listener->_registration = nil;
}

// Stores the registration once addSnapshotListener: returns. A callback on another thread may already have failed and
// stopped the listener, in which case the registration is removed straight away.
static void AdamEPeerlessAttach(AdamEPeerlessListener *listener, id<FIRListenerRegistration> registration) {
    pthread_mutex_lock(&listener->_lock);
    if (listener->_stopped)
        [registration remove];
    else
        listener->_registration = registration;
    pthread_mutex_unlock(&listener->_lock);
}

// One Firestore callback. `encode` writes the snapshot into the writer.
static void AdamEPeerlessHandle(AdamEPeerlessListener *listener, id snapshot, NSError *error,
                                void (^encode)(AdamEPeerlessFrameWriter *writer)) {
    uint64_t start = adame_peerless_now_nanos();
    uint64_t startCpu = adame_peerless_thread_cpu_nanos();
    pthread_mutex_lock(&listener->_lock);
    if (listener->_stopped) {
        pthread_mutex_unlock(&listener->_lock);
        return;
    }
    atomic_store(&listener->_callbackThread, AdamEPeerlessThisThread());

    if (snapshot == nil) {
        int32_t code = (int32_t)FIRFirestoreErrorCodeUnknown;
        NSString *message = @"Firestore sent neither a snapshot nor an error.";
        if (error != nil) {
            code = [error.domain isEqualToString:FIRFirestoreErrorDomain] ? (int32_t)error.code : (int32_t)FIRFirestoreErrorCodeUnknown;
            message = error.localizedDescription ?: @"Firestore reported an error without a description.";
        }
        AdamEPeerlessStopLocked(listener);
        listener->_onError(listener->_context, code, message.UTF8String);
    } else {
        NSString *failure = nil;
        @try {
            encode(listener->_writer);
            if (listener->_writer.length > INT32_MAX)
                failure = [NSString stringWithFormat:@"The frame is %zu bytes, over the 2 GB limit.", listener->_writer.length];
        } @catch (NSException *exception) {
            failure = exception.reason ?: exception.name;
        }

        if (failure != nil) {
            AdamEPeerlessStopLocked(listener);
            listener->_onError(listener->_context, ADAME_PEERLESS_ENCODE_FAILED, failure.UTF8String);
        } else {
            listener->_timing = (adame_peerless_frame_timing){
                .start_nanos = start,
                .start_cpu_nanos = startCpu,
                .encode_nanos = adame_peerless_now_nanos() - start,
                .encode_cpu_nanos = adame_peerless_thread_cpu_nanos() - startCpu,
                .data_nanos = listener->_writer.dataNanos,
            };
            listener->_onFrame(listener->_context, listener->_writer.bytes, listener->_writer.length);
        }
    }

    atomic_store(&listener->_callbackThread, 0);
    if (listener->_releaseDeferred) {
        listener->_releaseDeferred = NO;
        void *context = listener->_context;
        listener->_context = NULL;
        if (listener->_deferredRelease != NULL)
            listener->_deferredRelease(context);
    }
    pthread_mutex_unlock(&listener->_lock);
}

void *adame_peerless_listen_query(void *query, void *context, adame_peerless_frame_callback onFrame,
                                  adame_peerless_error_callback onError) {
    id object = (__bridge id)query;
    if (object == nil || onFrame == NULL || onError == NULL || ![object isKindOfClass:[FIRQuery class]])
        return NULL;
    AdamEPeerlessListener *listener = AdamEPeerlessNewListener(context, onFrame, onError);
    __weak AdamEPeerlessListener *weak = listener;
    id<FIRListenerRegistration> registration =
        [(FIRQuery *)object addSnapshotListener:^(FIRQuerySnapshot *snapshot, NSError *error) {
            AdamEPeerlessListener *strong = weak; // keeps the listener alive while this callback runs
            if (strong != nil)
                AdamEPeerlessHandle(strong, snapshot, error, ^(AdamEPeerlessFrameWriter *writer) {
                    [writer writeQuerySnapshot:snapshot];
                });
        }];
    AdamEPeerlessAttach(listener, registration);
    return (__bridge_retained void *)listener;
}

void *adame_peerless_listen_document(void *document, void *context, adame_peerless_frame_callback onFrame,
                                     adame_peerless_error_callback onError) {
    id object = (__bridge id)document;
    if (object == nil || onFrame == NULL || onError == NULL || ![object isKindOfClass:[FIRDocumentReference class]])
        return NULL;
    AdamEPeerlessListener *listener = AdamEPeerlessNewListener(context, onFrame, onError);
    __weak AdamEPeerlessListener *weak = listener;
    id<FIRListenerRegistration> registration =
        [(FIRDocumentReference *)object addSnapshotListener:^(FIRDocumentSnapshot *snapshot, NSError *error) {
            AdamEPeerlessListener *strong = weak;
            if (strong != nil)
                AdamEPeerlessHandle(strong, snapshot, error, ^(AdamEPeerlessFrameWriter *writer) {
                    [writer writeDocumentSnapshot:snapshot];
                });
        }];
    AdamEPeerlessAttach(listener, registration);
    return (__bridge_retained void *)listener;
}

void adame_peerless_listener_remove(void *pointer, adame_peerless_release_callback release) {
    if (pointer == NULL)
        return;
    AdamEPeerlessListener *listener = (__bridge_transfer AdamEPeerlessListener *)pointer;

    // From inside a callback, on its thread: this thread already holds the lock. Stop now; the callback releases the
    // context when it returns (and keeps the listener alive until then).
    if (atomic_load(&listener->_callbackThread) == AdamEPeerlessThisThread()) {
        if (!listener->_stopped)
            AdamEPeerlessStopLocked(listener);
        listener->_deferredRelease = release;
        listener->_releaseDeferred = YES;
        return;
    }

    // Anywhere else: taking the lock waits for a callback in flight; once it's held, none can start.
    pthread_mutex_lock(&listener->_lock);
    if (!listener->_stopped)
        AdamEPeerlessStopLocked(listener);
    void *context = listener->_context;
    listener->_context = NULL;
    pthread_mutex_unlock(&listener->_lock);
    if (release != NULL)
        release(context);
}

void adame_peerless_listener_timing(void *pointer, adame_peerless_frame_timing *timing) {
    if (pointer == NULL || timing == NULL)
        return;
    AdamEPeerlessListener *listener = (__bridge AdamEPeerlessListener *)pointer;
    *timing = listener->_timing;
}
