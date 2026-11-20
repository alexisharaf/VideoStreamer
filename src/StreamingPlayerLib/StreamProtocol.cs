namespace StreamingPlayerLib;

public static class StreamProtocol
{
    public static readonly byte[] Magic = "VS1\0"u8.ToArray();
    public const byte PacketVideo = 1;
    public const byte PacketAudio = 2;

    public const int DefaultPort = 5000;
    public const int VideoFps = 15;
    public const int JpegQuality = 70;
    public const int AudioSampleRate = 16000;
    public const int AudioChannels = 1;
    public const int AudioBits = 16;
}
