// The wire format 1.0 encoder, exposed for the shim's own tests. Not part of the API .NET uses.
#import <Foundation/Foundation.h>

@class FIRQuerySnapshot;
@class FIRDocumentSnapshot;

NS_ASSUME_NONNULL_BEGIN

typedef NS_ENUM(uint8_t, AdamEPeerlessFrameKind) {
    AdamEPeerlessFrameKindQuery = 1,
    AdamEPeerlessFrameKindDocument = 2,
};

typedef NS_ENUM(uint8_t, AdamEPeerlessSection) {
    AdamEPeerlessSectionMetadata = 1,
    AdamEPeerlessSectionDocuments = 2,
};

/// Encodes snapshots into frames. Reused per listener: -reset keeps the buffer (-shrinkIfIdle gives back an oversized
/// one). Not thread-safe.
@interface AdamEPeerlessFrameWriter : NSObject

- (instancetype)initWithCapacity:(size_t)capacity NS_DESIGNATED_INITIALIZER;

- (void)writeQuerySnapshot:(FIRQuerySnapshot *)snapshot;
- (void)writeDocumentSnapshot:(FIRDocumentSnapshot *)snapshot;

// Building blocks, so tests can build frames from plain values in a fixed key order.
- (void)reset;
- (void)writeHeader:(AdamEPeerlessFrameKind)kind;
- (void)writeMetadataSectionFromCache:(BOOL)fromCache hasPendingWrites:(BOOL)hasPendingWrites;
- (void)beginSection:(AdamEPeerlessSection)section;
- (void)endSection;
/// Writes a document entry: path, flags, and (if it exists) the map body of `data` (nil means no fields).
- (void)writeDocumentPath:(NSString *)path exists:(BOOL)exists fromCache:(BOOL)fromCache
         hasPendingWrites:(BOOL)hasPendingWrites data:(nullable NSDictionary<NSString *, id> *)data;
/// Any value FIRDocumentSnapshot.data can hold; anything unrecognised is written as unsupported with its class name.
- (void)writeValue:(nullable id)value;
- (void)writeMapBody:(NSDictionary<NSString *, id> *)map;
- (void)writeReferencePath:(NSString *)path;
- (void)writeUnsupported:(NSString *)typeName;
- (void)writeString:(NSString *)value;
- (void)writeVarint:(uint32_t)value;
- (void)writeByte:(uint8_t)value;

@property(nonatomic, readonly) const uint8_t *bytes NS_RETURNS_INNER_POINTER;
@property(nonatomic, readonly) size_t length;
/// The buffer's current size. It grows as frames need, and only -shrinkIfIdle or -shrinkToCapacity: makes it smaller.
@property(nonatomic, readonly) size_t capacity;

/// Drops the content, and replaces the buffer with one of `capacity` bytes (at least 64) when that is smaller, then
/// also frees a string scratch larger than that. Unlike Android's shrinkTo it resets even when nothing shrinks, so call
/// it only between frames. If the smaller buffer can't be allocated the old one (and the scratch) is kept; it never
/// raises.
- (void)shrinkToCapacity:(size_t)capacity;
/// Call once the current frame has been delivered and its bytes are no longer read. After 8 frames in a row that each
/// used under a quarter of a buffer above 1 MB, shrinks the buffer to fit the largest of them (the next power of two,
/// at least 64 KB), as the Android shim does. Any larger frame, or a buffer of 1 MB or less, starts the count again.
- (void)shrinkIfIdle;
/// Whether to time -[FIRDocumentSnapshot data] (two clock reads per document). Off unless a listener is timed.
@property(nonatomic) BOOL timesData;
/// Wall time the last snapshot spent inside Firestore's -[FIRDocumentSnapshot data] (building the NSDictionary), when
/// timesData is on; 0 otherwise.
@property(nonatomic, readonly) uint64_t dataNanos;

@end

NS_ASSUME_NONNULL_END
