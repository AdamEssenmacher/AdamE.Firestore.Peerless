# .NET calls into these by name through JNI; keep them when an app shrinks its Java code.
-keep class com.adame.firestore.peerless.** { *; }
