# .NET calls into these by name through JNI; keep them when an app shrinks its Java code.
-keep class com.adame.firestore.peerless.** { *; }

# The encoder reports a value it doesn't recognise as "unsupported" with the value's class name. Keep the names of
# Firestore's value types (shrinking may still remove unused ones) so that name stays readable when an app enables
# R8. A value type added in a future SDK and not listed here would be reported under its obfuscated name.
-keepnames class com.google.firebase.firestore.VectorValue
-keepnames class com.google.firebase.firestore.Blob
-keepnames class com.google.firebase.firestore.GeoPoint
-keepnames class com.google.firebase.firestore.DocumentReference
-keepnames class com.google.firebase.Timestamp
