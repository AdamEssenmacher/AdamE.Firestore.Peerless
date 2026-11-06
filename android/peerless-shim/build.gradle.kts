// The Peerless Android shim: owns Firestore listeners and encodes each snapshot into one wire-format frame
// (docs/wire-format.md). Written in Java so the AAR adds no Kotlin runtime dependency; the tests are Kotlin.
plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.adame.firestore.peerless"
    compileSdk = 36

    defaultConfig {
        minSdk = 24
        consumerProguardFiles("consumer-rules.pro")
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    testOptions {
        unitTests.isReturnDefaultValues = true
    }
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

dependencies {
    // Matches the native SDK inside Xamarin.Firebase.Firestore 126.0.2.4; the app supplies it at runtime.
    compileOnly("com.google.firebase:firebase-firestore:26.0.2")

    testImplementation("com.google.firebase:firebase-firestore:26.0.2")
    testImplementation("junit:junit:4.13.2")
}

// The golden frames shared with the .NET tests.
tasks.withType<Test>().configureEach {
    systemProperty("peerless.fixtures", rootProject.projectDir.resolve("../fixtures/wire").absolutePath)
}
