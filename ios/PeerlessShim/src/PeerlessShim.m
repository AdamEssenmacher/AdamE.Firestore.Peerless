#import "PeerlessShim.h"
#import "PLSFrameWriter.h"

#import <stdatomic.h>
#import <time.h>

#import <FirebaseFirestoreInternal/FIRDocumentReference.h>
#import <FirebaseFirestoreInternal/FIRDocumentSnapshot.h>
#import <FirebaseFirestoreInternal/FIRFirestore.h>
#import <FirebaseFirestoreInternal/FIRFirestoreErrors.h>
#import <FirebaseFirestoreInternal/FIRFirestoreSettings.h>
#import <FirebaseFirestoreInternal/FIRListenerRegistration.h>
#import <FirebaseFirestoreInternal/FIRQuery.h>
#import <FirebaseFirestoreInternal/FIRQuerySnapshot.h>

uint64_t peerless_now_nanos(void) {
    return clock_gettime_nsec_np(CLOCK_UPTIME_RAW);
}

uint64_t peerless_thread_cpu_nanos(void) {
    return clock_gettime_nsec_np(CLOCK_THREAD_CPUTIME_ID);
}

/// One listener: its writer (reused for every snapshot), the .NET callbacks, and the Firestore registration.
@interface PLSListener : NSObject
@end

@implementation PLSListener {
@public
    PLSFrameWriter *_writer;
    void *_context;
    peerless_frame_callback _onFrame;
    peerless_error_callback _onError;
    dispatch_queue_t _queue;
    id<FIRListenerRegistration> _registration;
    atomic_bool _muted;
    peerless_frame_timing _timing;
}
@end

static PLSListener *PLSNewListener(FIRFirestore *firestore, void *context, peerless_frame_callback onFrame,
                                   peerless_error_callback onError) {
    PLSListener *listener = [PLSListener new];
    listener->_writer = [[PLSFrameWriter alloc] initWithCapacity:64 * 1024];
    listener->_context = context;
    listener->_onFrame = onFrame;
    listener->_onError = onError;
    // Firestore runs listener callbacks on this queue (the main queue unless the app chose another).
    listener->_queue = firestore.settings.dispatchQueue ?: dispatch_get_main_queue();
    atomic_init(&listener->_muted, false);
    return listener;
}

static void PLSFail(PLSListener *listener, NSError *error) {
    if (atomic_load(&listener->_muted))
        return;
    int32_t code = (int32_t)FIRFirestoreErrorCodeUnknown;
    NSString *message = @"Firestore sent neither a snapshot nor an error.";
    if (error != nil) {
        code = [error.domain isEqualToString:FIRFirestoreErrorDomain] ? (int32_t)error.code : (int32_t)FIRFirestoreErrorCodeUnknown;
        message = error.localizedDescription ?: @"Firestore reported an error without a description.";
    }
    listener->_onError(listener->_context, code, message.UTF8String);
}

static void PLSDeliver(PLSListener *listener, uint64_t start, uint64_t startCpu) {
    listener->_timing = (peerless_frame_timing){
        .start_nanos = start,
        .start_cpu_nanos = startCpu,
        .encode_nanos = peerless_now_nanos() - start,
        .encode_cpu_nanos = peerless_thread_cpu_nanos() - startCpu,
        .data_nanos = listener->_writer.dataNanos,
    };
    // Checked again after encoding: removal may have happened meanwhile, on another thread.
    if (atomic_load(&listener->_muted))
        return;
    listener->_onFrame(listener->_context, listener->_writer.bytes, listener->_writer.length);
}

void *peerless_listen_query(void *query, void *context, peerless_frame_callback onFrame, peerless_error_callback onError) {
    id object = (__bridge id)query;
    if (object == nil || onFrame == NULL || onError == NULL || ![object isKindOfClass:[FIRQuery class]])
        return NULL;
    FIRQuery *target = object;
    PLSListener *listener = PLSNewListener(target.firestore, context, onFrame, onError);
    __weak PLSListener *weak = listener;
    listener->_registration = [target addSnapshotListener:^(FIRQuerySnapshot *snapshot, NSError *error) {
        uint64_t start = peerless_now_nanos();
        uint64_t startCpu = peerless_thread_cpu_nanos();
        PLSListener *strong = weak;
        if (strong == nil || atomic_load(&strong->_muted))
            return;
        if (snapshot == nil) {
            PLSFail(strong, error);
            return;
        }
        [strong->_writer writeQuerySnapshot:snapshot];
        PLSDeliver(strong, start, startCpu);
    }];
    return (__bridge_retained void *)listener;
}

void *peerless_listen_document(void *document, void *context, peerless_frame_callback onFrame, peerless_error_callback onError) {
    id object = (__bridge id)document;
    if (object == nil || onFrame == NULL || onError == NULL || ![object isKindOfClass:[FIRDocumentReference class]])
        return NULL;
    FIRDocumentReference *target = object;
    PLSListener *listener = PLSNewListener(target.firestore, context, onFrame, onError);
    __weak PLSListener *weak = listener;
    listener->_registration = [target addSnapshotListener:^(FIRDocumentSnapshot *snapshot, NSError *error) {
        uint64_t start = peerless_now_nanos();
        uint64_t startCpu = peerless_thread_cpu_nanos();
        PLSListener *strong = weak;
        if (strong == nil || atomic_load(&strong->_muted))
            return;
        if (snapshot == nil) {
            PLSFail(strong, error);
            return;
        }
        [strong->_writer writeDocumentSnapshot:snapshot];
        PLSDeliver(strong, start, startCpu);
    }];
    return (__bridge_retained void *)listener;
}

void peerless_listener_remove(void *pointer, peerless_release_callback release) {
    if (pointer == NULL)
        return;
    PLSListener *listener = (__bridge_transfer PLSListener *)pointer;
    atomic_store(&listener->_muted, true);
    [listener->_registration remove];
    // A callback already running on the queue finishes first (it re-checks the flag before calling .NET); after this
    // block nothing can reach the context again.
    dispatch_async(listener->_queue, ^{
        listener->_registration = nil;
        if (release != NULL)
            release(listener->_context);
        listener->_context = NULL;
    });
}

void peerless_listener_timing(void *pointer, peerless_frame_timing *timing) {
    if (pointer == NULL || timing == NULL)
        return;
    PLSListener *listener = (__bridge PLSListener *)pointer;
    *timing = listener->_timing;
}
