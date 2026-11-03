// The Objective-C encoder must reproduce the golden frames in fixtures/wire byte for byte. The inputs mirror
// fixtures/wire/README.md (and GoldenFixtures.cs), in the same key order. NSDictionary has no order, so every map with
// more than one key is written entry by entry with the writer's building blocks; single-key maps and arrays go through
// -writeValue:, as snapshot data does.
//
// A plain executable rather than XCTest, so it runs headless on a simulator with `simctl spawn` and needs no Xcode
// project: scripts/test-ios-shim.sh. Usage: GoldenFixtureTests <fixtures/wire directory>. Exit code 0 = all passed.
#import <Foundation/Foundation.h>

#import <FirebaseCore/FIRTimestamp.h>
#import <FirebaseFirestoreInternal/FIRFieldValue.h>
#import <FirebaseFirestoreInternal/FIRGeoPoint.h>

#import "PLSFrameWriter.h"

static int failures = 0;
static int passes = 0;

static NSString *Hex(const uint8_t *bytes, size_t length) {
    NSMutableString *text = [NSMutableString stringWithCapacity:length * 2];
    for (size_t index = 0; index < length; index++)
        [text appendFormat:@"%02X", bytes[index]];
    return text;
}

static void Expect(NSString *name, NSData *expected, PLSFrameWriter *writer) {
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

static void ExpectFixture(NSString *directory, NSString *name, PLSFrameWriter *writer) {
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
    PLSFrameWriter *writer = [[PLSFrameWriter alloc] initWithCapacity:64];
    [writer writeHeader:PLSFrameKindDocument];
    [writer writeMetadataSectionFromCache:NO hasPendingWrites:NO];
    [writer beginSection:PLSSectionDocuments];
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
    PLSFrameWriter *writer = [[PLSFrameWriter alloc] initWithCapacity:64];
    [writer writeHeader:PLSFrameKindDocument];
    [writer writeMetadataSectionFromCache:YES hasPendingWrites:NO];
    [writer beginSection:PLSSectionDocuments];
    [writer writeVarint:1];
    [writer writeDocumentPath:@"fixtures/missing" exists:NO fromCache:YES hasPendingWrites:NO data:nil];
    [writer endSection];
    ExpectFixture(fixtures, @"document-missing", writer);
}

static void QueryNested(NSString *fixtures) {
    PLSFrameWriter *writer = [[PLSFrameWriter alloc] initWithCapacity:64];
    [writer writeHeader:PLSFrameKindQuery];
    [writer writeMetadataSectionFromCache:NO hasPendingWrites:YES];
    [writer beginSection:PLSSectionDocuments];
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

static void WriteQueryEmpty(PLSFrameWriter *writer) {
    [writer reset];
    [writer writeHeader:PLSFrameKindQuery];
    [writer writeMetadataSectionFromCache:YES hasPendingWrites:NO];
    [writer beginSection:PLSSectionDocuments];
    [writer writeVarint:0];
    [writer endSection];
}

static void QueryEmpty(NSString *fixtures) {
    PLSFrameWriter *writer = [[PLSFrameWriter alloc] initWithCapacity:64];
    WriteQueryEmpty(writer);
    ExpectFixture(fixtures, @"query-empty", writer);
}

static void AReusedWriterProducesTheSameFrameAgain(NSString *fixtures) {
    PLSFrameWriter *writer = [[PLSFrameWriter alloc] initWithCapacity:64];
    for (int round = 0; round < 3; round++) {
        WriteQueryEmpty(writer);
        ExpectFixture(fixtures, @"query-empty", writer);
    }
}

// Not fixtures: properties the format requires of every encoder.
static void StringEdgeCases(void) {
    PLSFrameWriter *writer = [[PLSFrameWriter alloc] initWithCapacity:64];
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
        printf("SHIM_TESTS passed=%d failed=%d\n", passes, failures);
        return failures == 0 ? 0 : 1;
    }
}
