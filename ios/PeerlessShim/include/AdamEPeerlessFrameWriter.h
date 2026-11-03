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

/// Encodes snapshots into frames. Reused per listener: -reset keeps the buffer. Not thread-safe.
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
/// Wall time the last snapshot spent inside Firestore's -[FIRDocumentSnapshot data] (building the NSDictionary).
@property(nonatomic, readonly) uint64_t dataNanos;

@end

NS_ASSUME_NONNULL_END
