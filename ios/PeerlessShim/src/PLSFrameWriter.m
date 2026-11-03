#import "PLSFrameWriter.h"

#import <time.h>

#import <FirebaseCore/FIRTimestamp.h>
#import <FirebaseFirestoreInternal/FIRDocumentReference.h>
#import <FirebaseFirestoreInternal/FIRDocumentSnapshot.h>
#import <FirebaseFirestoreInternal/FIRGeoPoint.h>
#import <FirebaseFirestoreInternal/FIRQuerySnapshot.h>
#import <FirebaseFirestoreInternal/FIRSnapshotMetadata.h>

enum {
    PLSTagNull = 0,
    PLSTagFalse = 1,
    PLSTagTrue = 2,
    PLSTagInteger = 3,
    PLSTagDouble = 4,
    PLSTagString = 5,
    PLSTagBytes = 6,
    PLSTagTimestamp = 7,
    PLSTagGeoPoint = 8,
    PLSTagReference = 9,
    PLSTagArray = 10,
    PLSTagMap = 11,
    PLSTagUnsupported = 12,
};

// Classes resolved once. They come from the app's own FirebaseFirestoreInternal and FirebaseCore frameworks.
static Class PLSStringClass, PLSNumberClass, PLSDictionaryClass, PLSArrayClass, PLSNullClass, PLSDataClass;
static Class PLSTimestampClass, PLSReferenceClass, PLSGeoPointClass;

@implementation PLSFrameWriter {
    uint8_t *_buffer;
    size_t _capacity;
    size_t _length;
    ptrdiff_t _sectionStart;
    unichar *_scratch;      // UTF-16 scratch for strings CFStringGetBytes can't convert (lone surrogates)
    size_t _scratchCapacity;
    uint64_t _dataNanos;
}

+ (void)initialize {
    if (self != [PLSFrameWriter class])
        return;
    PLSStringClass = [NSString class];
    PLSNumberClass = [NSNumber class];
    PLSDictionaryClass = [NSDictionary class];
    PLSArrayClass = [NSArray class];
    PLSNullClass = [NSNull class];
    PLSDataClass = [NSData class];
    PLSTimestampClass = [FIRTimestamp class];
    PLSReferenceClass = [FIRDocumentReference class];
    PLSGeoPointClass = [FIRGeoPoint class];
}

- (instancetype)init {
    return [self initWithCapacity:16 * 1024];
}

- (instancetype)initWithCapacity:(size_t)capacity {
    if ((self = [super init])) {
        _capacity = capacity < 64 ? 64 : capacity;
        _buffer = malloc(_capacity);
        if (_buffer == NULL)
            return nil;
        _sectionStart = -1;
    }
    return self;
}

- (void)dealloc {
    free(_buffer);
    free(_scratch);
}

- (const uint8_t *)bytes {
    return _buffer;
}

- (size_t)length {
    return _length;
}

- (uint64_t)dataNanos {
    return _dataNanos;
}

static inline void PLSEnsure(PLSFrameWriter *self, size_t extra) {
    if (self->_length + extra <= self->_capacity)
        return;
    size_t capacity = self->_capacity;
    while (capacity < self->_length + extra)
        capacity *= 2;
    uint8_t *grown = realloc(self->_buffer, capacity);
    if (grown == NULL)
        [NSException raise:NSMallocException format:@"Peerless could not grow its frame buffer to %zu bytes.", capacity];
    self->_buffer = grown;
    self->_capacity = capacity;
}

static inline void PLSByte(PLSFrameWriter *self, uint8_t value) {
    PLSEnsure(self, 1);
    self->_buffer[self->_length++] = value;
}

static inline void PLSVarint(PLSFrameWriter *self, uint32_t value) {
    PLSEnsure(self, 5);
    while (value >= 0x80) {
        self->_buffer[self->_length++] = (uint8_t)(value | 0x80);
        value >>= 7;
    }
    self->_buffer[self->_length++] = (uint8_t)value;
}

static inline size_t PLSVarintSize(uint32_t value) {
    size_t size = 1;
    while (value >= 0x80) {
        value >>= 7;
        size++;
    }
    return size;
}

// Little-endian, as the format requires; arm64 and x86_64 both are.
static inline void PLSInt32(PLSFrameWriter *self, int32_t value) {
    PLSEnsure(self, 4);
    uint32_t bits = CFSwapInt32HostToLittle((uint32_t)value);
    memcpy(self->_buffer + self->_length, &bits, 4);
    self->_length += 4;
}

static inline void PLSInt64(PLSFrameWriter *self, int64_t value) {
    PLSEnsure(self, 8);
    uint64_t bits = CFSwapInt64HostToLittle((uint64_t)value);
    memcpy(self->_buffer + self->_length, &bits, 8);
    self->_length += 8;
}

static inline void PLSDouble(PLSFrameWriter *self, double value) {
    uint64_t bits;
    memcpy(&bits, &value, 8); // the bits as they are: -0.0, NaN payloads and infinities survive
    PLSInt64(self, (int64_t)bits);
}

// UTF-16 → UTF-8 by hand, for strings holding a lone surrogate: it becomes U+FFFD, as in .NET and the Java encoder.
static void PLSStringSlow(PLSFrameWriter *self, CFStringRef value, CFIndex chars) {
    if (self->_scratchCapacity < (size_t)chars) {
        unichar *grown = realloc(self->_scratch, (size_t)chars * sizeof(unichar));
        if (grown == NULL)
            [NSException raise:NSMallocException format:@"Peerless could not convert a string of %ld characters.", (long)chars];
        self->_scratch = grown;
        self->_scratchCapacity = (size_t)chars;
    }
    unichar *units = self->_scratch;
    CFStringGetCharacters(value, CFRangeMake(0, chars), units);

    size_t bytes = 0;
    for (CFIndex index = 0; index < chars; index++) {
        unichar c = units[index];
        if (c < 0x80) {
            bytes += 1;
        } else if (c < 0x800) {
            bytes += 2;
        } else if (CFStringIsSurrogateHighCharacter(c) && index + 1 < chars && CFStringIsSurrogateLowCharacter(units[index + 1])) {
            bytes += 4;
            index++;
        } else {
            bytes += 3; // BMP character, or a lone surrogate written as U+FFFD
        }
    }

    PLSVarint(self, (uint32_t)bytes);
    PLSEnsure(self, bytes);
    uint8_t *out = self->_buffer + self->_length;
    for (CFIndex index = 0; index < chars; index++) {
        uint32_t c = units[index];
        if (c < 0x80) {
            *out++ = (uint8_t)c;
        } else if (c < 0x800) {
            *out++ = (uint8_t)(0xC0 | (c >> 6));
            *out++ = (uint8_t)(0x80 | (c & 0x3F));
        } else if (c >= 0xD800 && c <= 0xDFFF) {
            if (CFStringIsSurrogateHighCharacter((unichar)c) && index + 1 < chars && CFStringIsSurrogateLowCharacter(units[index + 1])) {
                uint32_t code = CFStringGetLongCharacterForSurrogatePair((unichar)c, units[++index]);
                *out++ = (uint8_t)(0xF0 | (code >> 18));
                *out++ = (uint8_t)(0x80 | ((code >> 12) & 0x3F));
                *out++ = (uint8_t)(0x80 | ((code >> 6) & 0x3F));
                *out++ = (uint8_t)(0x80 | (code & 0x3F));
            } else {
                *out++ = 0xEF; // U+FFFD
                *out++ = 0xBF;
                *out++ = 0xBD;
            }
        } else {
            *out++ = (uint8_t)(0xE0 | (c >> 12));
            *out++ = (uint8_t)(0x80 | ((c >> 6) & 0x3F));
            *out++ = (uint8_t)(0x80 | (c & 0x3F));
        }
    }
    self->_length = (size_t)(out - self->_buffer);
}

// CoreFoundation writes a lone surrogate as a 3-byte ED A0..BF sequence (CESU-style), which isn't UTF-8. Those are
// the only bytes it produces that well-formed UTF-8 never contains, so finding ED followed by A0..BF is enough.
static inline BOOL PLSIsWellFormed(const uint8_t *bytes, size_t length) {
    const uint8_t *end = bytes + length;
    for (const uint8_t *cursor = memchr(bytes, 0xED, length); cursor != NULL && cursor + 1 < end;
         cursor = memchr(cursor + 1, 0xED, (size_t)(end - cursor - 1))) {
        if (cursor[1] >= 0xA0)
            return NO;
    }
    return YES;
}

// Converts straight into the frame buffer, 5 bytes past the cursor (room for the longest varint), then slides the
// bytes back once their length is known. CFStringGetBytes stops at a character it can't convert (lossByte 0), and
// then the slow path redoes the string.
static void PLSString(PLSFrameWriter *self, CFStringRef value) {
    CFIndex chars = CFStringGetLength(value);
    if (chars == 0) {
        PLSByte(self, 0);
        return;
    }
    size_t maxBytes = (size_t)chars * 3; // every UTF-16 unit takes at most 3 bytes; a surrogate pair 4 for 2 units
    PLSEnsure(self, 5 + maxBytes);
    uint8_t *start = self->_buffer + self->_length;
    CFIndex used = 0;
    CFIndex converted = CFStringGetBytes(value, CFRangeMake(0, chars), kCFStringEncodingUTF8, 0, false, start + 5,
                                         (CFIndex)maxBytes, &used);
    if (converted != chars || !PLSIsWellFormed(start + 5, (size_t)used)) {
        PLSStringSlow(self, value, chars);
        return;
    }
    size_t prefix = PLSVarintSize((uint32_t)used);
    if (prefix != 5)
        memmove(start + prefix, start + 5, (size_t)used);
    uint32_t remaining = (uint32_t)used;
    uint8_t *cursor = start;
    while (remaining >= 0x80) {
        *cursor++ = (uint8_t)(remaining | 0x80);
        remaining >>= 7;
    }
    *cursor = (uint8_t)remaining;
    self->_length += prefix + (size_t)used;
}

static void PLSValue(PLSFrameWriter *self, id value, int depth);

static void PLSMapBody(PLSFrameWriter *self, NSDictionary *map, int depth) {
    CFIndex count = CFDictionaryGetCount((__bridge CFDictionaryRef)map);
    PLSVarint(self, (uint32_t)count);
    if (count == 0)
        return;
    // Keys and values in one call, into the stack for ordinary maps.
    const void *stackKeys[32];
    const void *stackValues[32];
    const void **keys = count <= 32 ? stackKeys : malloc(sizeof(void *) * (size_t)count);
    const void **values = count <= 32 ? stackValues : malloc(sizeof(void *) * (size_t)count);
    if (keys == NULL || values == NULL)
        [NSException raise:NSMallocException format:@"Peerless could not read a map of %ld entries.", (long)count];
    CFDictionaryGetKeysAndValues((__bridge CFDictionaryRef)map, keys, values);
    for (CFIndex index = 0; index < count; index++) {
        id key = (__bridge id)keys[index];
        PLSString(self, (__bridge CFStringRef)([key isKindOfClass:PLSStringClass] ? key : [key description]));
        PLSValue(self, (__bridge id)values[index], depth + 1);
    }
    if (keys != stackKeys)
        free(keys);
    if (values != stackValues)
        free(values);
}

static void PLSUnsupported(PLSFrameWriter *self, NSString *typeName) {
    PLSByte(self, PLSTagUnsupported);
    PLSString(self, (__bridge CFStringRef)typeName);
}

// Ordered by how often each kind appears in typical documents.
static void PLSValue(PLSFrameWriter *self, id value, int depth) {
    if (depth > 100)
        [NSException raise:NSInvalidArgumentException format:@"Peerless refuses values nested deeper than 100."];
    if (value == nil) {
        PLSByte(self, PLSTagNull);
    } else if ([value isKindOfClass:PLSStringClass]) {
        PLSByte(self, PLSTagString);
        PLSString(self, (__bridge CFStringRef)value);
    } else if ([value isKindOfClass:PLSNumberClass]) {
        // A boolean is the CFBoolean singleton. Otherwise the Objective-C type says integer or double; Firestore
        // reports integers as long long ('q') and doubles as 'd', and neither is ever converted into the other.
        if (CFGetTypeID((__bridge CFTypeRef)value) == CFBooleanGetTypeID()) {
            PLSByte(self, [value boolValue] ? PLSTagTrue : PLSTagFalse);
            return;
        }
        const char *type = [value objCType];
        switch (type[0]) {
            case 'd':
            case 'f':
                PLSByte(self, PLSTagDouble);
                PLSDouble(self, [value doubleValue]);
                break;
            case 'q': case 'l': case 'i': case 's': case 'c':
            case 'Q': case 'L': case 'I': case 'S': case 'C':
                PLSByte(self, PLSTagInteger);
                PLSInt64(self, [value longLongValue]);
                break;
            default:
                PLSUnsupported(self, [NSString stringWithFormat:@"NSNumber(%s)", type]);
                break;
        }
    } else if ([value isKindOfClass:PLSDictionaryClass]) {
        PLSByte(self, PLSTagMap);
        PLSMapBody(self, value, depth);
    } else if ([value isKindOfClass:PLSArrayClass]) {
        NSArray *array = value;
        NSUInteger count = array.count;
        PLSByte(self, PLSTagArray);
        PLSVarint(self, (uint32_t)count);
        for (id item in array)
            PLSValue(self, item, depth + 1);
    } else if ([value isKindOfClass:PLSTimestampClass]) {
        FIRTimestamp *timestamp = value;
        PLSByte(self, PLSTagTimestamp);
        PLSInt64(self, timestamp.seconds);
        PLSInt32(self, timestamp.nanoseconds);
    } else if ([value isKindOfClass:PLSReferenceClass]) {
        PLSByte(self, PLSTagReference);
        PLSString(self, (__bridge CFStringRef)((FIRDocumentReference *)value).path);
    } else if ([value isKindOfClass:PLSNullClass]) {
        PLSByte(self, PLSTagNull);
    } else if ([value isKindOfClass:PLSGeoPointClass]) {
        FIRGeoPoint *point = value;
        PLSByte(self, PLSTagGeoPoint);
        PLSDouble(self, point.latitude);
        PLSDouble(self, point.longitude);
    } else if ([value isKindOfClass:PLSDataClass]) {
        NSData *data = value;
        NSUInteger count = data.length;
        PLSByte(self, PLSTagBytes);
        PLSVarint(self, (uint32_t)count);
        PLSEnsure(self, count);
        if (count > 0)
            memcpy(self->_buffer + self->_length, data.bytes, count);
        self->_length += count;
    } else {
        PLSUnsupported(self, NSStringFromClass([value class]));
    }
}

static void PLSDocument(PLSFrameWriter *self, NSString *path, BOOL exists, BOOL fromCache, BOOL hasPendingWrites,
                        NSDictionary *data) {
    PLSString(self, (__bridge CFStringRef)path);
    PLSByte(self, (uint8_t)((exists ? 0x01 : 0) | (fromCache ? 0x02 : 0) | (hasPendingWrites ? 0x04 : 0)));
    if (!exists)
        return;
    if (data == nil) {
        PLSVarint(self, 0);
        return;
    }
    PLSMapBody(self, data, 1);
}

// `data` uses the SDK's default server timestamp behavior (none: unresolved server timestamps read as null), the
// same as an ordinary app reading snapshot.data.
static void PLSSnapshotDocument(PLSFrameWriter *self, FIRDocumentSnapshot *document) {
    FIRSnapshotMetadata *metadata = document.metadata;
    BOOL exists = document.exists;
    NSDictionary *data = nil;
    if (exists) {
        uint64_t start = clock_gettime_nsec_np(CLOCK_UPTIME_RAW);
        data = document.data;
        self->_dataNanos += clock_gettime_nsec_np(CLOCK_UPTIME_RAW) - start;
    }
    PLSDocument(self, document.reference.path, exists, metadata.isFromCache, metadata.hasPendingWrites, data);
}

- (void)writeQuerySnapshot:(FIRQuerySnapshot *)snapshot {
    [self reset];
    [self writeHeader:PLSFrameKindQuery];
    FIRSnapshotMetadata *metadata = snapshot.metadata;
    [self writeMetadataSectionFromCache:metadata.isFromCache hasPendingWrites:metadata.hasPendingWrites];
    NSArray<FIRQueryDocumentSnapshot *> *documents = snapshot.documents;
    [self beginSection:PLSSectionDocuments];
    PLSVarint(self, (uint32_t)documents.count);
    for (FIRQueryDocumentSnapshot *document in documents) {
        @autoreleasepool {
            PLSSnapshotDocument(self, document);
        }
    }
    [self endSection];
}

- (void)writeDocumentSnapshot:(FIRDocumentSnapshot *)snapshot {
    [self reset];
    [self writeHeader:PLSFrameKindDocument];
    FIRSnapshotMetadata *metadata = snapshot.metadata;
    [self writeMetadataSectionFromCache:metadata.isFromCache hasPendingWrites:metadata.hasPendingWrites];
    [self beginSection:PLSSectionDocuments];
    PLSVarint(self, 1);
    PLSSnapshotDocument(self, snapshot);
    [self endSection];
}

- (void)reset {
    _length = 0;
    _sectionStart = -1;
    _dataNanos = 0;
}

- (void)writeHeader:(PLSFrameKind)kind {
    PLSEnsure(self, 8);
    const uint8_t header[8] = {'P', 'L', 'S', 'F', 1, 0, kind, 0};
    memcpy(_buffer + _length, header, 8);
    _length += 8;
}

- (void)writeMetadataSectionFromCache:(BOOL)fromCache hasPendingWrites:(BOOL)hasPendingWrites {
    [self beginSection:PLSSectionMetadata];
    PLSByte(self, (uint8_t)((fromCache ? 0x01 : 0) | (hasPendingWrites ? 0x02 : 0)));
    [self endSection];
}

- (void)beginSection:(PLSSection)section {
    if (_sectionStart >= 0)
        [NSException raise:NSInternalInconsistencyException format:@"Sections do not nest."];
    PLSByte(self, section);
    PLSEnsure(self, 4);
    _sectionStart = (ptrdiff_t)_length;
    _length += 4;
}

- (void)endSection {
    if (_sectionStart < 0)
        [NSException raise:NSInternalInconsistencyException format:@"No section is open."];
    uint32_t size = CFSwapInt32HostToLittle((uint32_t)(_length - (size_t)_sectionStart - 4));
    memcpy(_buffer + _sectionStart, &size, 4);
    _sectionStart = -1;
}

- (void)writeDocumentPath:(NSString *)path exists:(BOOL)exists fromCache:(BOOL)fromCache
         hasPendingWrites:(BOOL)hasPendingWrites data:(NSDictionary<NSString *, id> *)data {
    PLSDocument(self, path, exists, fromCache, hasPendingWrites, data);
}

- (void)writeValue:(id)value {
    PLSValue(self, value, 1);
}

- (void)writeMapBody:(NSDictionary<NSString *, id> *)map {
    PLSMapBody(self, map, 1);
}

- (void)writeReferencePath:(NSString *)path {
    PLSByte(self, PLSTagReference);
    PLSString(self, (__bridge CFStringRef)path);
}

- (void)writeUnsupported:(NSString *)typeName {
    PLSUnsupported(self, typeName);
}

- (void)writeString:(NSString *)value {
    PLSString(self, (__bridge CFStringRef)value);
}

- (void)writeVarint:(uint32_t)value {
    PLSVarint(self, value);
}

- (void)writeByte:(uint8_t)value {
    PLSByte(self, value);
}

@end
