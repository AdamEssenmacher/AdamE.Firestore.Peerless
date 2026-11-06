// Deterministic tests of the shim listener's lock, stop, error and release paths, with no Firestore and no simulator
// benchmark: the test plays Firestore through adame_peerless_testing_listen (a stand-in registration, and the
// listener's own callback block called from chosen threads and queues). Each test counts release calls: exactly one.
#import <Foundation/Foundation.h>
#import <stdatomic.h>
#import <pthread.h>
#import <unistd.h>

#import <FirebaseCore/FIRApp.h>
#import <FirebaseCore/FIROptions.h>
#import <FirebaseFirestoreInternal/FIRCollectionReference.h>
#import <FirebaseFirestoreInternal/FIRFirestore.h>
#import <FirebaseFirestoreInternal/FIRFirestoreErrors.h>
#import <FirebaseFirestoreInternal/FIRFirestoreSettings.h>
#import <FirebaseFirestoreInternal/FIRLocalCacheSettings.h>
#import <FirebaseFirestoreInternal/FIRQuery.h>
#import <FirebaseFirestoreInternal/FIRListenerRegistration.h>

#import "AdamEPeerlessTesting.h"

extern void ListenerTestsRecord(BOOL ok, NSString *name, NSString *detail);

@interface AdamEPeerlessFakeRegistration : NSObject <FIRListenerRegistration>
@property(atomic) int removes;
@property(atomic, copy, nullable) void (^onRemove)(void);
@end

@implementation AdamEPeerlessFakeRegistration
- (void)remove {
    self.removes += 1;
    if (self.onRemove != nil)
        self.onRemove();
}
@end

typedef NS_ENUM(int, Behaviour) {
    BehaviourCount,
    BehaviourRemoveInside,  // the callback removes its own listener
    BehaviourSlow,          // the callback takes 100 ms
    BehaviourConcurrent,    // the callback checks it never overlaps another, or runs after removal
};

typedef struct Probe {
    Behaviour behaviour;
    void *listener;
    atomic_int frames;
    atomic_int errors;
    atomic_int releases;
    atomic_int releasesSeenInside;
    atomic_int inside;
    atomic_int overlaps;
    atomic_int afterRemoval;
    atomic_bool removalReturned;
    int lastCode;
    void *lastNative;    // the error callback's `native`, compared by identity only (borrowed, never dereferenced later)
    int lastNativeKind;  // 0 none, 1 NSError, 2 NSException, 3 something else
    adame_peerless_frame_timing firstFrameTiming;
    atomic_int timedFrames; // frames that arrived with non-NULL timing
} Probe;

static void OnFrame(void *context, const uint8_t *data, size_t length,
                    const adame_peerless_frame_timing *timing) {
    Probe *probe = context;
    if (atomic_load(&probe->removalReturned))
        atomic_fetch_add(&probe->afterRemoval, 1);
    if (atomic_fetch_add(&probe->inside, 1) > 0)
        atomic_fetch_add(&probe->overlaps, 1);
    if (atomic_fetch_add(&probe->frames, 1) == 0 && timing != NULL)
        probe->firstFrameTiming = *timing;
    if (timing != NULL)
        atomic_fetch_add(&probe->timedFrames, 1);
    switch (probe->behaviour) {
        case BehaviourSlow:
            usleep(100 * 1000);
            break;
        case BehaviourConcurrent:
            usleep(200);
            break;
        case BehaviourCount:
        case BehaviourRemoveInside: // removal happens in OnError (errors) or OnFrameRemovingWithRelease (frames)
            break;
    }
    atomic_fetch_sub(&probe->inside, 1);
}

static void OnRelease(void *context) {
    atomic_fetch_add(&((Probe *)context)->releases, 1);
}

static void OnFrameRemovingWithRelease(void *context, const uint8_t *data, size_t length,
                                       const adame_peerless_frame_timing *timing) {
    Probe *probe = context;
    atomic_fetch_add(&probe->frames, 1);
    adame_peerless_listener_remove(probe->listener, OnRelease);
    // Still inside the callback: the release must wait for it to return.
    atomic_store(&probe->releasesSeenInside, atomic_load(&probe->releases));
}

static void OnError(void *context, int32_t code, const char *message, void *native) {
    Probe *probe = context;
    probe->lastCode = code;
    probe->lastNative = native;
    id object = (__bridge id)native;
    probe->lastNativeKind = object == nil ? 0
        : [object isKindOfClass:[NSError class]] ? 1
        : [object isKindOfClass:[NSException class]] ? 2
        : 3;
    atomic_fetch_add(&probe->errors, 1);
    if (probe->behaviour == BehaviourRemoveInside) {
        adame_peerless_listener_remove(probe->listener, OnRelease); // as the .NET side does after an error
        atomic_store(&probe->releasesSeenInside, atomic_load(&probe->releases));
    }
}

static void (^const Encode)(AdamEPeerlessFrameWriter *) = ^(AdamEPeerlessFrameWriter *writer) {
    [writer reset];
    [writer writeHeader:AdamEPeerlessFrameKindQuery];
};

static NSString *Counts(Probe *probe, AdamEPeerlessFakeRegistration *registration) {
    return [NSString stringWithFormat:@"frames=%d errors=%d code=%d releases=%d releasedInside=%d removes=%d overlaps=%d afterRemoval=%d",
            atomic_load(&probe->frames), atomic_load(&probe->errors), probe->lastCode, atomic_load(&probe->releases),
            atomic_load(&probe->releasesSeenInside), registration.removes, atomic_load(&probe->overlaps),
            atomic_load(&probe->afterRemoval)];
}

static void *Listen(Probe *probe, adame_peerless_frame_callback onFrame, AdamEPeerlessFakeRegistration *registration,
                    AdamEPeerlessTestingCallback *callback) {
    probe->listener = adame_peerless_testing_listen(probe, onFrame, OnError, registration, NO, callback);
    return probe->listener;
}

static void FirstFrameTimingDoesNotRequireTheListenerHandle(void) {
    Probe probe = {.behaviour = BehaviourCount};
    AdamEPeerlessFakeRegistration *registration = [AdamEPeerlessFakeRegistration new];
    AdamEPeerlessTestingCallback callback = nil;
    void *listener = adame_peerless_testing_listen(&probe, OnFrame, OnError, registration, YES, &callback);

    // Model a callback that runs before the caller stores its listener handle.
    uint64_t before = adame_peerless_now_nanos();
    callback(@"snapshot", nil, Encode);
    uint64_t after = adame_peerless_now_nanos();
    adame_peerless_frame_timing timing = probe.firstFrameTiming;
    uint64_t cpuAfter = adame_peerless_thread_cpu_nanos();
    BOOL timed = probe.listener == NULL && probe.frames == 1 && atomic_load(&probe.timedFrames) == 1
        && timing.start_nanos >= before && timing.start_nanos <= after
        && timing.encode_nanos <= after - timing.start_nanos
        && timing.start_cpu_nanos > 0 && timing.start_cpu_nanos <= cpuAfter
        && timing.encode_cpu_nanos <= cpuAfter - timing.start_cpu_nanos
        // Encode reads no document, so no time inside -[FIRDocumentSnapshot data] can have been counted.
        && timing.data_nanos == 0;
    adame_peerless_listener_remove(listener, OnRelease);
    BOOL ok = timed && probe.releases == 1 && registration.removes == 1;
    ListenerTestsRecord(ok, @"first frame carries its timing before the caller stores the listener handle",
                        Counts(&probe, registration));
}

static void AnUntimedListenerGetsNoTiming(void) {
    Probe probe = {.behaviour = BehaviourCount};
    AdamEPeerlessFakeRegistration *registration = [AdamEPeerlessFakeRegistration new];
    AdamEPeerlessTestingCallback callback = nil;
    void *listener = Listen(&probe, OnFrame, registration, &callback);

    callback(@"snapshot", nil, Encode);
    callback(@"snapshot", nil, Encode);
    BOOL untimed = probe.frames == 2 && atomic_load(&probe.timedFrames) == 0;
    adame_peerless_listener_remove(listener, OnRelease);
    BOOL ok = untimed && probe.releases == 1 && registration.removes == 1;
    ListenerTestsRecord(ok, @"an untimed listener's frames carry NULL timing",
                        [NSString stringWithFormat:@"%@ timedFrames=%d", Counts(&probe, registration),
                                                   atomic_load(&probe.timedFrames)]);
}

static void DisposeBeforeFirstCallback(void) {
    Probe probe = {.behaviour = BehaviourCount};
    AdamEPeerlessFakeRegistration *registration = [AdamEPeerlessFakeRegistration new];
    AdamEPeerlessTestingCallback callback = nil;
    void *listener = Listen(&probe, OnFrame, registration, &callback);
    adame_peerless_listener_remove(listener, OnRelease);
    callback(@"snapshot", nil, Encode); // a callback Firestore had already queued
    BOOL ok = probe.frames == 0 && probe.errors == 0 && probe.releases == 1 && registration.removes == 1;
    ListenerTestsRecord(ok, @"dispose before the first callback: nothing delivered, released once", Counts(&probe, registration));
}

static void ErrorStopsAndReleasesAfterTheCallback(void) {
    Probe probe = {.behaviour = BehaviourRemoveInside};
    AdamEPeerlessFakeRegistration *registration = [AdamEPeerlessFakeRegistration new];
    AdamEPeerlessTestingCallback callback = nil;
    Listen(&probe, OnFrame, registration, &callback);
    NSError *error = [NSError errorWithDomain:FIRFirestoreErrorDomain code:FIRFirestoreErrorCodePermissionDenied userInfo:nil];
    callback(nil, error, Encode);
    callback(@"snapshot", nil, Encode); // after the first error, nothing more
    callback(nil, error, Encode);
    BOOL ok = probe.errors == 1 && probe.lastCode == FIRFirestoreErrorCodePermissionDenied && probe.frames == 0
        && probe.releasesSeenInside == 0 && probe.releases == 1 && registration.removes == 1
        && probe.lastNative == (__bridge void *)error;
    ListenerTestsRecord(ok, @"error: stops first, one error (with Firestore's NSError), release deferred to the callback's end",
                        Counts(&probe, registration));
}

static void EncodeFailureIsCodeMinusOne(void) {
    Probe probe = {.behaviour = BehaviourCount};
    AdamEPeerlessFakeRegistration *registration = [AdamEPeerlessFakeRegistration new];
    AdamEPeerlessTestingCallback callback = nil;
    void *listener = Listen(&probe, OnFrame, registration, &callback);
    NSException *thrown = [NSException exceptionWithName:NSInvalidArgumentException reason:@"refused" userInfo:nil];
    callback(@"snapshot", nil, ^(AdamEPeerlessFrameWriter *writer) {
        @throw thrown;
    });
    callback(@"snapshot", nil, Encode);
    BOOL stopped = probe.errors == 1 && probe.lastCode == ADAME_PEERLESS_ENCODE_FAILED && probe.frames == 0
        && probe.lastNative == (__bridge void *)thrown && registration.removes == 1 && probe.releases == 0;
    adame_peerless_listener_remove(listener, OnRelease);
    BOOL ok = stopped && probe.releases == 1 && registration.removes == 1;
    ListenerTestsRecord(ok, @"encode failure: code -1 with the raised NSException, stopped, released once on removal",
                        Counts(&probe, registration));
}

static void DisposeFromInsideACallback(void) {
    Probe probe = {.behaviour = BehaviourCount};
    AdamEPeerlessFakeRegistration *registration = [AdamEPeerlessFakeRegistration new];
    AdamEPeerlessTestingCallback callback = nil;
    Listen(&probe, OnFrameRemovingWithRelease, registration, &callback);
    callback(@"snapshot", nil, Encode); // removes itself; must not deadlock
    callback(@"snapshot", nil, Encode);
    BOOL ok = probe.frames == 1 && probe.releasesSeenInside == 0 && probe.releases == 1 && registration.removes == 1;
    ListenerTestsRecord(ok, @"dispose from inside a callback: returns at once, last callback, released after it", Counts(&probe, registration));
}

static void RemovalWaitsForACallbackInFlight(void) {
    Probe probe = {.behaviour = BehaviourSlow};
    AdamEPeerlessFakeRegistration *registration = [AdamEPeerlessFakeRegistration new];
    AdamEPeerlessTestingCallback callback = nil;
    void *listener = Listen(&probe, OnFrame, registration, &callback);
    dispatch_semaphore_t done = dispatch_semaphore_create(0);
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        callback(@"snapshot", nil, Encode);
        dispatch_semaphore_signal(done);
    });
    while (atomic_load(&probe.inside) == 0)
        usleep(100);
    adame_peerless_listener_remove(listener, OnRelease); // on this thread, while the callback sleeps on another
    BOOL finishedFirst = atomic_load(&probe.inside) == 0;
    dispatch_semaphore_wait(done, DISPATCH_TIME_FOREVER);
    BOOL ok = finishedFirst && probe.frames == 1 && probe.releases == 1 && registration.removes == 1;
    ListenerTestsRecord(ok, @"removal from another thread waits for the callback in flight", Counts(&probe, registration));
}

static void ConcurrentQueue(void) {
    Probe probe = {.behaviour = BehaviourConcurrent};
    AdamEPeerlessFakeRegistration *registration = [AdamEPeerlessFakeRegistration new];
    AdamEPeerlessTestingCallback callback = nil;
    void *listener = Listen(&probe, OnFrame, registration, &callback);
    dispatch_queue_t queue = dispatch_queue_create("peerless.test.concurrent", DISPATCH_QUEUE_CONCURRENT);
    dispatch_group_t group = dispatch_group_create();
    for (int index = 0; index < 400; index++)
        dispatch_group_async(group, queue, ^{
            callback(@"snapshot", nil, Encode);
        });
    while (atomic_load(&probe.frames) < 50)
        usleep(100);
    adame_peerless_listener_remove(listener, OnRelease); // from another thread, mid-stream
    atomic_store(&probe.removalReturned, true);
    dispatch_group_wait(group, DISPATCH_TIME_FOREVER);
    BOOL ok = probe.overlaps == 0 && probe.afterRemoval == 0 && probe.frames >= 50 && probe.frames < 400
        && probe.releases == 1 && registration.removes == 1;
    ListenerTestsRecord(ok, @"concurrent queue: callbacks never overlap, none after removal, released once", Counts(&probe, registration));
}

static void ErrorWithoutRemovalReleasesOnceLater(void) {
    Probe probe = {.behaviour = BehaviourCount};
    AdamEPeerlessFakeRegistration *registration = [AdamEPeerlessFakeRegistration new];
    AdamEPeerlessTestingCallback callback = nil;
    void *listener = Listen(&probe, OnFrame, registration, &callback);
    NSError *foreign = [NSError errorWithDomain:NSURLErrorDomain code:-1 userInfo:nil]; // not Firestore's domain
    callback(nil, foreign, Encode);
    BOOL beforeRemoval = probe.releases == 0 && probe.errors == 1 && probe.lastCode == FIRFirestoreErrorCodeUnknown
        && probe.lastNative == (__bridge void *)foreign;
    adame_peerless_listener_remove(listener, OnRelease);
    BOOL ok = beforeRemoval && probe.releases == 1 && registration.removes == 1;
    ListenerTestsRecord(ok, @"foreign error: code unknown (2) with the original NSError, released exactly once on removal", Counts(&probe, registration));
}

// Firestore holds its own listener mutex while it calls the callback block, and -remove takes that mutex. If removal
// called -remove while holding the shim's lock, a callback waiting for the shim's lock (holding Firestore's) would
// deadlock with it. Modelled here: the "Firestore" thread holds `firestoreMutex` around the callback, and the fake
// registration's remove takes it too. The removal starts first and reaches -remove; then the callback runs.
static void RemovalDoesNotHoldTheLockWhileFirestoreRemoves(void) {
    Probe probe = {.behaviour = BehaviourCount};
    AdamEPeerlessFakeRegistration *registration = [AdamEPeerlessFakeRegistration new];
    AdamEPeerlessTestingCallback callback = nil;
    void *listener = Listen(&probe, OnFrame, registration, &callback);
    __block pthread_mutex_t firestoreMutex = PTHREAD_MUTEX_INITIALIZER;
    dispatch_semaphore_t firestoreHoldsItsMutex = dispatch_semaphore_create(0);
    dispatch_semaphore_t removeReachedFirestore = dispatch_semaphore_create(0);
    dispatch_semaphore_t removed = dispatch_semaphore_create(0);
    registration.onRemove = ^{
        dispatch_semaphore_signal(removeReachedFirestore);
        pthread_mutex_lock(&firestoreMutex);
        pthread_mutex_unlock(&firestoreMutex);
    };

    dispatch_queue_t firestore = dispatch_queue_create("peerless.test.firestore", DISPATCH_QUEUE_SERIAL);
    dispatch_async(firestore, ^{
        pthread_mutex_lock(&firestoreMutex);
        dispatch_semaphore_signal(firestoreHoldsItsMutex);
        dispatch_semaphore_wait(removeReachedFirestore, DISPATCH_TIME_FOREVER); // removal is now inside -remove
        callback(@"snapshot", nil, Encode); // must not wait forever for the shim's lock
        pthread_mutex_unlock(&firestoreMutex);
    });
    dispatch_semaphore_wait(firestoreHoldsItsMutex, DISPATCH_TIME_FOREVER);
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        adame_peerless_listener_remove(listener, OnRelease);
        dispatch_semaphore_signal(removed);
    });
    BOOL finished = dispatch_semaphore_wait(removed, dispatch_time(DISPATCH_TIME_NOW, 5 * NSEC_PER_SEC)) == 0;
    BOOL ok = finished && probe.frames == 0 && probe.releases == 1 && registration.removes == 1;
    ListenerTestsRecord(ok, @"removal never holds the lock while Firestore removes (no deadlock with its mutex)",
                        finished ? Counts(&probe, registration) : @"DEADLOCK: removal did not return within 5 s");
    if (!finished)
        exit(1); // the threads stay blocked; don't run anything else
}

// Firestore raises an NSException from addSnapshotListener: for a listen it refuses, here one on a terminated
// instance ("The client has already been terminated."). That must come back as NULL plus a reason, not unwind into
// the caller. A real (offline) Firestore instance, with its own callback queue (a test executable has no main run
// loop). limit(toLast:) without an orderBy, the other case reported in review, does not raise at listen time in
// Firestore 12.18, so it isn't used here.
static void ARefusedListenReturnsAnErrorInsteadOfRaising(void) {
    FIROptions *options = [[FIROptions alloc] initWithGoogleAppID:@"1:000000000000:ios:0000000000000000"
                                                      GCMSenderID:@"000000000000"];
    options.projectID = @"demo-peerless";
    options.APIKey = @"demo-key-not-used";
    [FIRApp configureWithName:@"peerless-shim-tests" options:options];
    FIRFirestore *firestore = [FIRFirestore firestoreForApp:[FIRApp appNamed:@"peerless-shim-tests"]];
    FIRFirestoreSettings *settings = firestore.settings;
    settings.dispatchQueue = dispatch_queue_create("peerless.test.callbacks", DISPATCH_QUEUE_SERIAL);
    settings.cacheSettings = [[FIRMemoryCacheSettings alloc] init];
    firestore.settings = settings;
    FIRQuery *query = [firestore collectionWithPath:@"items"];
    dispatch_semaphore_t terminated = dispatch_semaphore_create(0);
    [firestore terminateWithCompletion:^(NSError *error) {
        dispatch_semaphore_signal(terminated);
    }];
    BOOL didTerminate = dispatch_semaphore_wait(terminated, dispatch_time(DISPATCH_TIME_NOW, 10 * NSEC_PER_SEC)) == 0;

    Probe probe = {.behaviour = BehaviourCount};
    char error[512] = "";
    void *raised = (void *)1; // must be overwritten either way
    void *listener = adame_peerless_listen_query((__bridge void *)query, &probe, OnFrame, OnError, 0, error, sizeof error,
                                                 &raised);
    id exception = raised == NULL || raised == (void *)1 ? nil : CFBridgingRelease(raised); // the caller owns it
    char documentError[512] = "";
    void *documentRaised = (void *)1;
    void *documentListener = adame_peerless_listen_document(
        (__bridge void *)[firestore documentWithPath:@"items/one"], &probe, OnFrame, OnError, 0, documentError,
        sizeof documentError, &documentRaised);
    id documentException = documentRaised == NULL || documentRaised == (void *)1 ? nil : CFBridgingRelease(documentRaised);
    char notAQuery[512] = "";
    void *notRaised = (void *)1;
    void *wrongType = adame_peerless_listen_query((__bridge void *)@"not a query", &probe, OnFrame, OnError, 0,
                                                  notAQuery, sizeof notAQuery, &notRaised);
    BOOL ok = didTerminate && listener == NULL && strlen(error) > 0 && [exception isKindOfClass:[NSException class]]
        && documentListener == NULL && strlen(documentError) > 0 && [documentException isKindOfClass:[NSException class]]
        && wrongType == NULL && strlen(notAQuery) > 0 && notRaised == NULL && probe.frames == 0 && probe.errors == 0;
    ListenerTestsRecord(ok, [NSString stringWithFormat:@"a query or document listen Firestore refuses returns NULL with its reason (\"%s\") and its NSException", error],
                        [NSString stringWithFormat:@"terminated=%d listener=%p error=\"%s\" wrongType=%p", didTerminate,
                                                   listener, error, wrongType]);
    if (documentListener != NULL)
        adame_peerless_listener_remove(documentListener, NULL);
    if (listener != NULL)
        adame_peerless_listener_remove(listener, NULL);
}

void RunListenerTests(void) {
    FirstFrameTimingDoesNotRequireTheListenerHandle();
    AnUntimedListenerGetsNoTiming();
    DisposeBeforeFirstCallback();
    ErrorStopsAndReleasesAfterTheCallback();
    EncodeFailureIsCodeMinusOne();
    DisposeFromInsideACallback();
    RemovalWaitsForACallbackInFlight();
    ConcurrentQueue();
    ErrorWithoutRemovalReleasesOnceLater();
    RemovalDoesNotHoldTheLockWhileFirestoreRemoves();
    ARefusedListenReturnsAnErrorInsteadOfRaising();
}
