namespace AdamE.Firestore.Peerless;

/// <summary>The constants of <c>docs/wire-format.md</c>, version 1.0.</summary>
internal static class Wire
{
    public static ReadOnlySpan<byte> Magic => "PLSF"u8;

    public const byte MajorVersion = 1;
    public const byte MinorVersion = 0;
    public const int HeaderLength = 8;
    public const int MaxDepth = 100;

    public static class FrameKind
    {
        public const byte Query = 1;
        public const byte Document = 2;
    }

    public static class Section
    {
        public const byte Metadata = 1;
        public const byte Documents = 2;
    }

    public static class MetadataFlags
    {
        public const byte FromCache = 0x01;
        public const byte HasPendingWrites = 0x02;
    }

    public static class DocumentFlags
    {
        public const byte Exists = 0x01;
        public const byte FromCache = 0x02;
        public const byte HasPendingWrites = 0x04;
    }

    public static class Tag
    {
        public const byte Null = 0;
        public const byte False = 1;
        public const byte True = 2;
        public const byte Integer = 3;
        public const byte Double = 4;
        public const byte String = 5;
        public const byte Bytes = 6;
        public const byte Timestamp = 7;
        public const byte GeoPoint = 8;
        public const byte Reference = 9;
        public const byte Array = 10;
        public const byte Map = 11;
        public const byte Unsupported = 12;
    }
}
