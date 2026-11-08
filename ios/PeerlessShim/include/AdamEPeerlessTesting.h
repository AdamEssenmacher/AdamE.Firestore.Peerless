// Test-only entry points into the shim's listener (not shipped in the xcframework's headers). They drive the same
// lock, stop, error and release code as a Firestore listener, with the test playing Firestore: it supplies a stand-in
// registration and calls the listener's callback block itself, from whatever thread or queue it likes.
#import <Foundation/Foundation.h>

#import "AdamEPeerlessFrameWriter.h"
#import "PeerlessShim.h"

@protocol FIRListenerRegistration;

NS_ASSUME_NONNULL_BEGIN

/// What Firestore would call: the snapshot (nil on error), the error, and how to encode the snapshot.
typedef void (^AdamEPeerlessTestingCallback)(id _Nullable snapshot, NSError *_Nullable error,
                                             void (^encode)(AdamEPeerlessFrameWriter *writer));

/// Creates a listener as adame_peerless_listen_query does, attached to `registration`, except that the caller chooses
/// `timed` (the listen functions' `timed` argument): YES delivers adame_peerless_frame_timing with each frame, NO
/// delivers NULL timing. `*callback` receives the block to call in place of Firestore; it holds the listener weakly,
/// as Firestore's block does.
void *adame_peerless_testing_listen(void *context, adame_peerless_frame_callback on_frame,
                                    adame_peerless_error_callback on_error, id<FIRListenerRegistration> registration,
                                    BOOL timed, AdamEPeerlessTestingCallback _Nullable *_Nonnull callback);

/// Makes the next listener created (by any listen function) start without a frame writer, as when its buffer can't be
/// allocated.
void adame_peerless_testing_fail_next_writer_allocation(void);

NS_ASSUME_NONNULL_END
