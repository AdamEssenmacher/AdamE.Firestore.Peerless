// The Objective-C encoder must reproduce the golden frames in fixtures/wire byte for byte. The inputs mirror
// fixtures/wire/README.md (and GoldenFixtures.cs), in the same key order. NSDictionary has no order, so every map with
// more than one key is written entry by entry with the writer's building blocks; single-key maps and arrays go through
// -writeValue:, as snapshot data does.
//
// ListenerTests.m adds the listener's lock, stop, error and release paths.
//
// A plain executable rather than XCTest, so it runs headless on a simulator with `simctl spawn` and needs no Xcode
// project: scripts/test-ios-shim.sh. Usage: GoldenFixtureTests <fixtures/wire directory>. Exit code 0 = all passed.
#import <Foundation/Foundation.h>

#import <FirebaseCore/FIRTimestamp.h>
#import <FirebaseFirestoreInternal/FIRFieldValue.h>
#import <FirebaseFirestoreInternal/FIRGeoPoint.h>

#import "AdamEPeerlessFrameWriter.h"

static int failures = 0;
static int passes = 0;

static NSString *Hex(const uint8_t *bytes, size_t length) {
    NSMutableString *text = [NSMutableString stringWithCapacity:length * 2];
    for (size_t index = 0; index < length; index++)
        [text appendFormat:@"%02X", bytes[index]];
    return text;
}

static void Expect(NSString *name, NSData *expected, AdamEPeerlessFrameWriter *writer) {
    NSString *want = Hex(expected.bytes, expected.length);
    NSString *got = Hex(writer.bytes, writer.length);
    if ([want isEqualToString:got]) {
        passes++;
        printf("PASS %s\n", name.UTF8String);
    } else {
        failures++;
        printf("FAIL %s\n  expected %s\n  actual   %s\n", name.UTF8String, want.UTF8String, got.UTF8String);
    }
}

static void ExpectFixture(NSString *directory, NSString *name, AdamEPeerlessFrameWriter *writer) {
    NSData *expected = [NSData dataWithContentsOfFile:[directory stringByAppendingPathComponent:[name stringByAppendingString:@".bin"]]];
    if (expected == nil) {
        failures++;
        printf("FAIL %s: fixture not found in %s\n", name.UTF8String, directory.UTF8String);
        return;
    }
    Expect(name, expected, writer);
}

static NSData *Bytes(const uint8_t *bytes, size_t length) {
    return [NSData dataWithBytes:bytes length:length];
}

static void DocumentScalars(NSString *fixtures) {
    AdamEPeerlessFrameWriter *writer = [[AdamEPeerlessFrameWriter alloc] initWithCapacity:64];
    [writer writeHeader:AdamEPeerlessFrameKindDocument];
    [writer writeMetadataSectionFromCache:NO hasPendingWrites:NO];
    [writer beginSection:AdamEPeerlessSectionDocuments];
    [writer writeVarint:1];
    [writer writeString:@"fixtures/scalars"];
    [writer writeByte:0x01];

    double javaNaN;
    uint64_t javaNaNBits = 0x7FF8000000000000ULL; // the fixture pins Java's Double.NaN bits
    memcpy(&javaNaN, &javaNaNBits, 8);
    const uint8_t blob[] = {0x00, 0x01, 0x7F, 0x80, 0xFF};
    NSArray *fields = @[
        @[@"null", [NSNull null]],
        @[@"false", @NO],
        @[@"true", @YES],
        @[@"zero", @0LL],
        @[@"int-min", @(INT64_MIN)],
        @[@"int-max", @(INT64_MAX)],
        @[@"int-negative", @(-42LL)],
        @[@"double-whole", @3.0],
        @[@"double-fraction", @(-1.5)],
        @[@"double-negative-zero", @(-0.0)],
        @[@"double-nan", @(javaNaN)],
        @[@"double-infinity", @(INFINITY)],
        @[@"string-empty", @""],
        @[@"string-ascii", @"hello"],
        @[@"string-unicode", @"héllo wörld ✓ 😀"],
        @[@"bytes", Bytes(blob, sizeof blob)],
        @[@"bytes-empty", [NSData data]],
        @[@"timestamp", [FIRTimestamp timestampWithSeconds:1767225600 nanoseconds:123456789]],
        @[@"timestamp-pre-epoch", [FIRTimestamp timestampWithSeconds:-1 nanoseconds:999999999]],
        @[@"geopoint", [[FIRGeoPoint alloc] initWithLatitude:42.3314 longitude:-83.0458]],
        @[@"reference", @"users/user-001"],
        @[@"unsupported", @"com.google.firebase.firestore.VectorValue"],
    ];
    [writer writeVarint:(uint32_t)fields.count];
    for (NSArray *pair in fields) {
        NSString *key = pair[0];
        [writer writeString:key];
        // A FIRDocumentReference needs a Firestore instance, and the fixture pins Java's class name for the
        // unsupported value, so those two go through the building blocks -writeValue: uses for them.
        if ([key isEqualToString:@"reference"])
            [writer writeReferencePath:pair[1]];
        else if ([key isEqualToString:@"unsupported"])
            [writer writeUnsupported:pair[1]];
        else
            [writer writeValue:pair[1]];
    }
    [writer endSection];
    ExpectFixture(fixtures, @"document-scalars", writer);
}

static void DocumentMissing(NSString *fixtures) {
    AdamEPeerlessFrameWriter *writer = [[AdamEPeerlessFrameWriter alloc] initWithCapacity:64];
    [writer writeHeader:AdamEPeerlessFrameKindDocument];
    [writer writeMetadataSectionFromCache:YES hasPendingWrites:NO];
    [writer beginSection:AdamEPeerlessSectionDocuments];
    [writer writeVarint:1];
    [writer writeDocumentPath:@"fixtures/missing" exists:NO fromCache:YES hasPendingWrites:NO data:nil];
    [writer endSection];
    ExpectFixture(fixtures, @"document-missing", writer);
}

static void QueryNested(NSString *fixtures) {
    AdamEPeerlessFrameWriter *writer = [[AdamEPeerlessFrameWriter alloc] initWithCapacity:64];
    [writer writeHeader:AdamEPeerlessFrameKindQuery];
    [writer writeMetadataSectionFromCache:NO hasPendingWrites:YES];
    [writer beginSection:AdamEPeerlessSectionDocuments];
    [writer writeVarint:2];

    // items/a: four fields in fixture order.
    [writer writeString:@"items/a"];
    [writer writeByte:0x01 | 0x04];
    [writer writeVarint:4];
    [writer writeString:@"tags"];
    [writer writeValue:@[@"x", @"y"]];
    [writer writeString:@"empty-array"];
    [writer writeValue:@[]];
    [writer writeString:@"empty-map"];
    [writer writeValue:@{}];
    [writer writeString:@"nested"];
    [writer writeByte:11]; // map: { level, deeper }, in that order
    [writer writeVarint:2];
    [writer writeString:@"level"];
    [writer writeValue:@1LL];
    [writer writeString:@"deeper"];
    [writer writeValue:@{@"list": @[@1LL, @2.5, @"three", [NSNull null], @YES, @{@"k": @"v"}, @[@[]]]}];

    // items/b: two fields in fixture order.
    [writer writeString:@"items/b"];
    [writer writeByte:0x01];
    [writer writeVarint:2];
    [writer writeString:@"count"];
    [writer writeValue:@128LL];
    [writer writeString:@"long-string"];
    [writer writeValue:[@"" stringByPaddingToLength:200 withString:@"a" startingAtIndex:0]];
    [writer endSection];
    ExpectFixture(fixtures, @"query-nested", writer);
}

static void WriteQueryEmpty(AdamEPeerlessFrameWriter *writer) {
    [writer reset];
    [writer writeHeader:AdamEPeerlessFrameKindQuery];
    [writer writeMetadataSectionFromCache:YES hasPendingWrites:NO];
    [writer beginSection:AdamEPeerlessSectionDocuments];
    [writer writeVarint:0];
    [writer endSection];
}

static void QueryEmpty(NSString *fixtures) {
    AdamEPeerlessFrameWriter *writer = [[AdamEPeerlessFrameWriter alloc] initWithCapacity:64];
    WriteQueryEmpty(writer);
    ExpectFixture(fixtures, @"query-empty", writer);
}

static void AReusedWriterProducesTheSameFrameAgain(NSString *fixtures) {
    AdamEPeerlessFrameWriter *writer = [[AdamEPeerlessFrameWriter alloc] initWithCapacity:64];
    for (int round = 0; round < 3; round++) {
        WriteQueryEmpty(writer);
        ExpectFixture(fixtures, @"query-empty", writer);
    }
}

// Not fixtures: properties the format requires of every encoder.
static void StringEdgeCases(void) {
    AdamEPeerlessFrameWriter *writer = [[AdamEPeerlessFrameWriter alloc] initWithCapacity:64];
    unichar lone[] = {'a', 0xD800, 'b', 0xDC00};
    [writer writeString:[NSString stringWithCharacters:lone length:4]];
    const uint8_t loneExpected[] = {8, 'a', 0xEF, 0xBF, 0xBD, 'b', 0xEF, 0xBF, 0xBD};
    Expect(@"lone surrogates become U+FFFD", Bytes(loneExpected, sizeof loneExpected), writer);

    [writer reset];
    unichar withNul[] = {'a', 0, 'b'};
    [writer writeString:[NSString stringWithCharacters:withNul length:3]];
    const uint8_t nulExpected[] = {3, 'a', 0, 'b'};
    Expect(@"embedded NUL survives", Bytes(nulExpected, sizeof nulExpected), writer);

    [writer reset];
    NSString *long300 = [@"" stringByPaddingToLength:300 withString:@"é" startingAtIndex:0]; // 600 bytes
    [writer writeString:long300];
    BOOL ok = writer.length == 602 && writer.bytes[0] == (0x80 | (600 & 0x7F)) && writer.bytes[1] == (600 >> 7);
    if (ok) {
        passes++;
        printf("PASS two-byte varint before a long non-ASCII string\n");
    } else {
        failures++;
        printf("FAIL two-byte varint before a long non-ASCII string (length %zu)\n", writer.length);
    }

    [writer reset];
    [writer writeValue:[FIRFieldValue vectorWithArray:@[@1.0]]];
    NSString *vector = [[NSString alloc] initWithBytes:writer.bytes + 2 length:writer.length - 2 encoding:NSUTF8StringEncoding];
    if (writer.bytes[0] == 12 && [vector hasSuffix:@"VectorValue"]) {
        passes++;
        printf("PASS a vector is unsupported, with its class name (%s)\n", vector.UTF8String);
    } else {
        failures++;
        printf("FAIL a vector is unsupported (tag %u, name %s)\n", writer.bytes[0], vector.UTF8String);
    }
}

// The listener turns an exception from the writer into error code -1 (ADAME_PEERLESS_ENCODE_FAILED) rather than a
// truncated frame, so the writer must throw for a value it refuses.
static void RefusesNestingDeeperThan100(void) {
    id value = @"leaf";
    for (int depth = 0; depth < 101; depth++)
        value = @[value];
    AdamEPeerlessFrameWriter *writer = [[AdamEPeerlessFrameWriter alloc] initWithCapacity:64];
    @try {
        [writer writeValue:value];
        failures++;
        printf("FAIL nesting deeper than 100 is refused (no exception)\n");
    } @catch (NSException *exception) {
        passes++;
        printf("PASS nesting deeper than 100 is refused (%s)\n", exception.reason.UTF8String);
    }
}

// The format counts a document's field values as depth 1 and refuses an array or map value at depth 100 or more; a
// scalar may sit at any depth. Entered the way a snapshot is: through a document's fields.
static BOOL DocumentWithNesting(int containers) {
    id value = @"leaf";
    for (int depth = 0; depth < containers; depth++)
        value = @[value];
    AdamEPeerlessFrameWriter *writer = [[AdamEPeerlessFrameWriter alloc] initWithCapacity:64];
    @try {
        [writer writeDocumentPath:@"c/d" exists:YES fromCache:NO hasPendingWrites:NO data:@{@"field": value}];
        return YES;
    } @catch (NSException *exception) {
        return NO;
    }
}

static void DepthLimitAtDocumentDepth(void) {
    BOOL ninetyNine = DocumentWithNesting(99);  // innermost array at depth 99, its string at depth 100
    BOOL hundred = DocumentWithNesting(100);    // innermost array at depth 100
    if (ninetyNine && !hundred) {
        passes++;
        printf("PASS depth limit as the format counts it (99 containers in a document accepted, 100 refused)\n");
    } else {
        failures++;
        printf("FAIL depth limit: 99 containers %s, 100 containers %s\n", ninetyNine ? "accepted" : "refused",
               hundred ? "accepted" : "refused");
    }
}

// Lone surrogates become U+FFFD, so two keys differing only there would collide in the frame (decoders reject a
// repeated key). As on Android, the encoder fails the snapshot instead; a lossy key without a collision is fine.
static void CollidingLoneSurrogateKeysAreRefused(void) {
    unichar high[] = {'k', 0xD800};
    unichar low[] = {'k', 0xDC00};
    NSString *first = [NSString stringWithCharacters:high length:2];
    NSString *second = [NSString stringWithCharacters:low length:2];
    AdamEPeerlessFrameWriter *writer = [[AdamEPeerlessFrameWriter alloc] initWithCapacity:64];
    BOOL refused = NO;
    @try {
        [writer writeValue:@{first: @1LL, second: @2LL}];
    } @catch (NSException *exception) {
        refused = [exception.reason containsString:@"lone UTF-16 surrogates"];
    }
    BOOL lossyAlone = YES;
    @try {
        [writer reset];
        [writer writeValue:@{first: @1LL, @"k": @2LL}]; // "k\uFFFD" and "k" stay distinct
    } @catch (NSException *exception) {
        lossyAlone = NO;
    }
    if (refused && lossyAlone) {
        passes++;
        printf("PASS keys colliding only in lone surrogates are refused; a lone lossy key is not\n");
    } else {
        failures++;
        printf("FAIL lone-surrogate key collision: refused=%d, lossy key alone accepted=%d\n", refused, lossyAlone);
    }
}

static void Check(BOOL ok, const char *name, NSString *detail) {
    if (ok) {
        passes++;
        printf("PASS %s\n", name);
    } else {
        failures++;
        printf("FAIL %s\n  %s\n", name, detail.UTF8String);
    }
}

// A frame of a header and, if given, one bytes value: NSData is copied in exactly, so the frame's length is known.
static void WriteFrame(AdamEPeerlessFrameWriter *writer, NSData *payload) {
    [writer reset];
    [writer writeHeader:AdamEPeerlessFrameKindQuery];
    if (payload != nil)
        [writer writeValue:payload];
}

static void ShrinkingDropsTheContentAndKeepsWorking(void) {
    AdamEPeerlessFrameWriter *writer = [[AdamEPeerlessFrameWriter alloc] initWithCapacity:64];
    WriteFrame(writer, [NSMutableData dataWithLength:3 * 1024 * 1024]);
    size_t grown = writer.capacity;
    [writer shrinkToCapacity:100 * 1024];
    size_t shrunk = writer.capacity, lengthAfter = writer.length;
    [writer shrinkToCapacity:1024 * 1024]; // larger than the buffer: kept
    size_t kept = writer.capacity;
    [writer shrinkToCapacity:1];
    size_t smallest = writer.capacity;
    WriteQueryEmpty(writer);
    NSData *frame = Bytes(writer.bytes, writer.length);
    AdamEPeerlessFrameWriter *fresh = [[AdamEPeerlessFrameWriter alloc] initWithCapacity:64];
    WriteQueryEmpty(fresh);
    BOOL same = [frame isEqualToData:Bytes(fresh.bytes, fresh.length)];
    Check(grown >= 3 * 1024 * 1024 && shrunk == 100 * 1024 && lengthAfter == 0 && kept == 100 * 1024 && smallest == 64
              && same,
          "shrinkToCapacity: replaces a larger buffer, drops the content, and the writer still encodes",
          [NSString stringWithFormat:@"grown=%zu shrunk=%zu length=%zu kept=%zu smallest=%zu same=%d", grown, shrunk,
                                     lengthAfter, kept, smallest, same]);
}

// The listener's policy: 8 frames in a row under a quarter of a buffer above 1 MB shrink it to fit the largest.
static void AnIdleWriterGivesBackAnOversizedBuffer(void) {
    NSData *big = [NSMutableData dataWithLength:3 * 1024 * 1024];
    NSData *medium = [NSMutableData dataWithLength:600 * 1024];
    AdamEPeerlessFrameWriter *writer = [[AdamEPeerlessFrameWriter alloc] initWithCapacity:64];

    // A big frame (4 MB buffer, used over a quarter), then small ones: the 8th shrinks it, to 64 KB.
    WriteFrame(writer, big);
    [writer shrinkIfIdle];
    size_t afterBig = writer.capacity;
    for (int frame = 0; frame < 7; frame++) {
        WriteFrame(writer, nil);
        [writer shrinkIfIdle];
    }
    size_t afterSeven = writer.capacity;
    WriteFrame(writer, nil);
    [writer shrinkIfIdle];
    size_t afterEight = writer.capacity;
    Check(afterBig == 4 * 1024 * 1024 && afterSeven == afterBig && afterEight == 64 * 1024,
          "a writer shrinks after 8 small frames in a row, not 7",
          [NSString stringWithFormat:@"afterBig=%zu afterSeven=%zu afterEight=%zu", afterBig, afterSeven, afterEight]);

    // A big frame in between starts the count again; the size fits the largest small frame (600 KB → 1 MB).
    WriteFrame(writer, big);
    [writer shrinkIfIdle];
    for (int frame = 0; frame < 7; frame++) {
        WriteFrame(writer, frame == 3 ? medium : nil);
        [writer shrinkIfIdle];
    }
    WriteFrame(writer, big);
    [writer shrinkIfIdle];
    for (int frame = 0; frame < 7; frame++) {
        WriteFrame(writer, frame == 3 ? medium : nil);
        [writer shrinkIfIdle];
    }
    size_t afterReset = writer.capacity;
    WriteFrame(writer, nil);
    [writer shrinkIfIdle];
    size_t fitted = writer.capacity;
    Check(afterReset == 4 * 1024 * 1024 && fitted == 1024 * 1024,
          "a large frame resets the count, and the shrink fits the largest small frame",
          [NSString stringWithFormat:@"afterReset=%zu fitted=%zu", afterReset, fitted]);

    // A buffer of 1 MB or less is never shrunk.
    AdamEPeerlessFrameWriter *modest = [[AdamEPeerlessFrameWriter alloc] initWithCapacity:1024 * 1024];
    for (int frame = 0; frame < 16; frame++) {
        WriteFrame(modest, nil);
        [modest shrinkIfIdle];
    }
    Check(modest.capacity == 1024 * 1024, "a buffer of 1 MB or less is not shrunk",
          [NSString stringWithFormat:@"capacity=%zu", modest.capacity]);
}

// Stand-ins for a query snapshot, with only what -writeQuerySnapshot: reads. The writer is typed for FIR classes but
// only sends these messages, so the stand-ins can carry data that makes it throw.
@interface PeerlessFakeMetadata : NSObject
@property(nonatomic) BOOL isFromCache;
@property(nonatomic) BOOL hasPendingWrites;
@end
@implementation PeerlessFakeMetadata
@end
@interface PeerlessFakeReference : NSObject
@property(nonatomic, copy) NSString *path;
@end
@implementation PeerlessFakeReference
@end
@interface PeerlessFakeDocument : NSObject
@property(nonatomic) PeerlessFakeMetadata *metadata;
@property(nonatomic) PeerlessFakeReference *reference;
@property(nonatomic) BOOL exists;
@property(nonatomic) NSDictionary *data;
@end
@implementation PeerlessFakeDocument
@end
@interface PeerlessFakeQuerySnapshot : NSObject
@property(nonatomic) PeerlessFakeMetadata *metadata;
@property(nonatomic) NSArray *documents;
@end
@implementation PeerlessFakeQuerySnapshot
@end

// When the writer throws mid-snapshot (here, keys colliding in lone surrogates), the strong references its methods
// hold (the snapshot, its metadata, its documents) must be released as the exception unwinds. ARC does that only with
// -fobjc-arc-exceptions; without it a real FIRQuerySnapshot (and the Firestore instance it keeps alive) would leak.
static void AThrowingSnapshotIsReleased(void) {
    __weak PeerlessFakeQuerySnapshot *weakSnapshot = nil;
    BOOL threw = NO;
    AdamEPeerlessFrameWriter *writer = [[AdamEPeerlessFrameWriter alloc] initWithCapacity:64];
    @autoreleasepool {
        unichar high[] = {'k', 0xD800};
        unichar low[] = {'k', 0xDC00};
        PeerlessFakeDocument *document = [PeerlessFakeDocument new];
        document.metadata = [PeerlessFakeMetadata new];
        document.reference = [PeerlessFakeReference new];
        document.reference.path = @"items/a";
        document.exists = YES;
        document.data = @{[NSString stringWithCharacters:high length:2]: @1LL, [NSString stringWithCharacters:low length:2]: @2LL};
        PeerlessFakeQuerySnapshot *snapshot = [PeerlessFakeQuerySnapshot new];
        snapshot.metadata = [PeerlessFakeMetadata new];
        snapshot.documents = @[document];
        weakSnapshot = snapshot;
        @try {
            [writer writeQuerySnapshot:(id)snapshot];
        } @catch (NSException *exception) {
            threw = YES;
        }
    }
    BOOL released = weakSnapshot == nil;
    if (threw && released) {
        passes++;
        printf("PASS a snapshot the writer throws on is released (ARC exception cleanup)\n");
    } else {
        failures++;
        printf("FAIL a snapshot the writer throws on is released: threw=%d released=%d\n", threw, released);
    }
}

void ListenerTestsRecord(BOOL ok, NSString *name, NSString *detail) {
    if (ok) {
        passes++;
        printf("PASS %s\n", name.UTF8String);
    } else {
        failures++;
        printf("FAIL %s\n  %s\n", name.UTF8String, detail.UTF8String);
    }
}

extern void RunListenerTests(void);

int main(int argc, const char *argv[]) {
    @autoreleasepool {
        if (argc < 2) {
            fprintf(stderr, "usage: GoldenFixtureTests <fixtures/wire directory>\n");
            return 2;
        }
        NSString *fixtures = [NSString stringWithUTF8String:argv[1]];
        DocumentScalars(fixtures);
        DocumentMissing(fixtures);
        QueryNested(fixtures);
        QueryEmpty(fixtures);
        AReusedWriterProducesTheSameFrameAgain(fixtures);
        StringEdgeCases();
        RefusesNestingDeeperThan100();
        DepthLimitAtDocumentDepth();
        CollidingLoneSurrogateKeysAreRefused();
        AThrowingSnapshotIsReleased();
        ShrinkingDropsTheContentAndKeepsWorking();
        AnIdleWriterGivesBackAnOversizedBuffer();
        RunListenerTests();
        printf("SHIM_TESTS passed=%d failed=%d\n", passes, failures);
        return failures == 0 ? 0 : 1;
    }
}
