// swift-tools-version:6.2
import PackageDescription

let package = Package(
    name: "PeerlessMacNative",
    platforms: [.macOS(.v14)],
    products: [.library(name: "PeerlessMacNative", targets: ["PeerlessMacNative"])],
    dependencies: [
        .package(url: "https://github.com/firebase/firebase-ios-sdk.git", exact: "13.0.0", traits: ["Firestore"]),
    ],
    targets: [
        .target(
            name: "PeerlessAppleShim",
            dependencies: [.product(name: "FirebaseFirestore", package: "firebase-ios-sdk")],
            path: "ios/PeerlessShim",
            sources: ["src"],
            publicHeadersPath: "include",
            cSettings: [
                .define("ADAME_PEERLESS_FIREBASE_VERSION", to: "\"13.0.0\""),
                .unsafeFlags(["-fobjc-arc"]),
            ]),
        .target(
            name: "PeerlessMacNative",
            dependencies: ["PeerlessAppleShim", .product(name: "FirebaseFirestore", package: "firebase-ios-sdk")],
            path: "macos/PeerlessMacNative",
            cSettings: [.unsafeFlags(["-fobjc-arc"])]),
    ])
