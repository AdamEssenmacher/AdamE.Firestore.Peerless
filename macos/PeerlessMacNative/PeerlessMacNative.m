#import "PeerlessMacNative.h"
#import "PeerlessShim.h"
#import <FirebaseCore/FIRTimestamp.h>
#import <FirebaseFirestoreInternal/FirebaseFirestore.h>

#define MacExport __attribute__((used, visibility("default")))

typedef void (*MacCommitCallback)(void *context, int32_t code, const char *message);

MacExport int32_t adame_peerless_mac_abi_version(void) {
    return 1;
}

static void Invalid(NSString *reason) {
    [NSException raise:NSInvalidArgumentException format:@"%@", reason];
}

static id Parse(const char *json) {
    NSError *error = nil;
    id value = [NSJSONSerialization JSONObjectWithData:[NSData dataWithBytes:json length:strlen(json)]
                                               options:0 error:&error];
    if (!value) Invalid(error.localizedDescription);
    return value;
}

static id Convert(id value, FIRFirestore *store) {
    if ([value isKindOfClass:NSArray.class]) {
        NSMutableArray *items = [NSMutableArray array];
        for (id item in value) [items addObject:Convert(item, store)];
        return items;
    }
    if (![value isKindOfClass:NSDictionary.class]) return value;

    NSDictionary *tag = value;
    if (tag[@"reference"]) return [store documentWithPath:tag[@"reference"]];
    if (tag[@"timestamp"]) {
        NSArray<NSNumber *> *parts = tag[@"timestamp"];
        return [[FIRTimestamp alloc] initWithSeconds:parts[0].longLongValue nanoseconds:parts[1].intValue];
    }
    if (tag[@"double"]) return @(strtod([tag[@"double"] UTF8String], NULL));
    if (tag[@"increment"]) return [FIRFieldValue fieldValueForIntegerIncrement:[tag[@"increment"] longLongValue]];
    if (tag[@"serverTimestamp"]) return [FIRFieldValue fieldValueForServerTimestamp];
    Invalid(@"Unknown Firestore value tag.");
    return nil;
}

static void WriteError(char *error, size_t capacity, NSString *text) {
    if (capacity) snprintf(error, capacity, "%s", text.UTF8String ?: "Unknown Firestore error.");
}

MacExport int32_t adame_peerless_mac_use_emulator(const char *host, int32_t port, char *error, size_t capacity) {
    @try {
        FIRFirestore *store = [FIRFirestore firestore];
        [store useEmulatorWithHost:@(host) port:port];
        FIRFirestoreSettings *settings = store.settings;
        settings.sslEnabled = NO;
        store.settings = settings;
        return 1;
    } @catch (NSException *failure) {
        WriteError(error, capacity, failure.reason);
        return 0;
    }
}

static FIRFieldPath *FieldPath(NSString *name) {
    return [[FIRFieldPath alloc] initWithFields:[name componentsSeparatedByString:@"."]];
}

MacExport void adame_peerless_mac_post(void *context, void (*callback)(void *)) {
    dispatch_queue_t queue = [FIRFirestore firestore].settings.dispatchQueue;
    dispatch_queue_t main = dispatch_get_main_queue();
    dispatch_block_t deliver = ^{ callback(context); };
    if (queue == main) dispatch_async(main, deliver);
    else dispatch_async(main, ^{ dispatch_async(queue, deliver); });
}

MacExport void *adame_peerless_mac_listen(const char *path, const char *json, void *context,
    adame_peerless_frame_callback onFrame, adame_peerless_error_callback onError,
    int32_t includeMetadata, char *error, size_t capacity) {
    @try {
        FIRFirestore *store = [FIRFirestore firestore];
        if (!json) {
            return adame_peerless_listen_document((__bridge void *)[store documentWithPath:@(path)],
                context, onFrame, onError, 0, includeMetadata, error, capacity, NULL);
        }

        NSDictionary *spec = Parse(json);
        FIRQuery *query = [store collectionWithPath:@(path)];
        for (NSArray *filter in spec[@"filters"]) {
            FIRFieldPath *field = FieldPath(filter[0]);
            query = [query queryWhereFieldPath:field isEqualTo:Convert(filter[1], store)];
        }
        if (spec[@"order"]) {
            FIRFieldPath *field = FieldPath(spec[@"order"]);
            query = [query queryOrderedByFieldPath:field descending:[spec[@"descending"] boolValue]];
        }
        if (spec[@"limit"]) query = [query queryLimitedTo:[spec[@"limit"] integerValue]];
        return adame_peerless_listen_query((__bridge void *)query, context, onFrame, onError,
            0, includeMetadata, error, capacity, NULL);
    } @catch (NSException *failure) {
        WriteError(error, capacity, failure.reason);
        return NULL;
    }
}

MacExport void adame_peerless_mac_remove(void *listener, adame_peerless_release_callback release) {
    adame_peerless_listener_remove(listener, release);
}

MacExport int32_t adame_peerless_mac_commit(const char *json, void *context, MacCommitCallback callback,
    char *error, size_t capacity) {
    @try {
        FIRFirestore *store = [FIRFirestore firestore];
        FIRWriteBatch *batch = [store batch];
        for (NSDictionary *write in Parse(json)) {
            FIRDocumentReference *document = [store documentWithPath:write[@"path"]];
            NSString *kind = write[@"kind"];
            if ([kind isEqualToString:@"delete"]) {
                [batch deleteDocument:document];
                continue;
            }

            NSMutableDictionary *fields = [NSMutableDictionary dictionary];
            NSDictionary *values = write[@"fields"];
            for (NSString *name in values) fields[name] = Convert(values[name], store);
            if ([kind isEqualToString:@"set"]) [batch setData:fields forDocument:document];
            else if ([kind isEqualToString:@"merge"]) [batch setData:fields forDocument:document merge:YES];
            else if ([kind isEqualToString:@"update"]) [batch updateData:fields forDocument:document];
            else Invalid(@"Unknown Firestore write kind.");
        }
        [batch commitWithCompletion:^(NSError *failure) {
            int32_t code = !failure ? 0 : [failure.domain isEqualToString:FIRFirestoreErrorDomain]
                ? (int32_t)failure.code : (int32_t)FIRFirestoreErrorCodeUnknown;
            callback(context, code, failure.localizedDescription.UTF8String);
        }];
        return 1;
    } @catch (NSException *failure) {
        WriteError(error, capacity, failure.reason);
        return 0;
    }
}
