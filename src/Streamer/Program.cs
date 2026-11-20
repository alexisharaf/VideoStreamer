using System.Net;
using System.Net.Sockets;
using NAudio.Wave;
using OpenCvSharp;
using StreamingPlayerLib;

if (args.Length > 0 && args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: Streamer [port] [cameraIndex] [audioDeviceIndex|-1]\nDefaults: port={0} camera=0 audio=0", StreamProtocol.DefaultPort);
    return;
}

int port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : StreamProtocol.DefaultPort;
int camIndex = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 0;
int audioIndex = args.Length > 2 && int.TryParse(args[2], out var a) ? a : 0;

var listener = new TcpListener(IPAddress.Any, port);
listener.Start();
Console.WriteLine($"Streamer listening on 0.0.0.0:{port} (cam {camIndex}, audio {audioIndex}). Ctrl+C to stop.");

while (true)
{
    Console.WriteLine("Waiting for client...");
    using var client = await listener.AcceptTcpClientAsync();
    Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");
    try { await StreamToClientAsync(client, camIndex, audioIndex); }
    catch (Exception ex) { Console.WriteLine($"Client error: {ex.Message}"); }
    Console.WriteLine("Client disconnected.");
}

static async Task StreamToClientAsync(TcpClient client, int camIndex, int audioIndex)
{
    using var stream = client.GetStream();
    using var cap = new VideoCapture(camIndex);
    if (!cap.IsOpened()) throw new InvalidOperationException($"Cannot open camera {camIndex}.");

    int w = (int)cap.FrameWidth, h = (int)cap.FrameHeight;
    if (w <= 0 || h <= 0) { w = 640; h = 480; }
    var sendLock = new object();
    bool alive = true;

    // Header: magic + w,h,rate,ch,bits
    lock (sendLock)
    {
        stream.Write(StreamProtocol.Magic);
        Span<byte> hdr = stackalloc byte[16];
        BitConverter.TryWriteBytes(hdr[0..4], w);
        BitConverter.TryWriteBytes(hdr[4..8], h);
        BitConverter.TryWriteBytes(hdr[8..12], StreamProtocol.AudioSampleRate);
        BitConverter.TryWriteBytes(hdr[12..14], (short)StreamProtocol.AudioChannels);
        BitConverter.TryWriteBytes(hdr[14..16], (short)StreamProtocol.AudioBits);
        stream.Write(hdr);
        stream.Flush();
    }

    using var waveIn = audioIndex >= 0 ? new WaveInEvent() : null;
    if (waveIn != null)
    {
        waveIn.DeviceNumber = audioIndex;
        waveIn.WaveFormat = new WaveFormat(StreamProtocol.AudioSampleRate, StreamProtocol.AudioBits, StreamProtocol.AudioChannels);
        waveIn.BufferMilliseconds = 100;
        waveIn.DataAvailable += (_, e) =>
        {
            if (!alive || e.BytesRecorded <= 0) return;
            try { SendPacket(stream, sendLock, StreamProtocol.PacketAudio, e.Buffer, e.BytesRecorded); }
            catch { alive = false; }
        };
        waveIn.StartRecording();
    }

    try
    {
        using var frame = new Mat();
        var encodeParams = new[] { new ImageEncodingParam(ImwriteFlags.JpegQuality, StreamProtocol.JpegQuality) };
        var frameDelay = TimeSpan.FromMilliseconds(1000.0 / StreamProtocol.VideoFps);
        while (alive && client.Connected)
        {
            var t0 = DateTime.UtcNow;
            if (!cap.Read(frame) || frame.Empty()) break;
            Cv2.ImEncode(".jpg", frame, out var jpg, encodeParams);
            try { SendPacket(stream, sendLock, StreamProtocol.PacketVideo, jpg, jpg.Length); }
            catch { break; }
            var sleep = frameDelay - (DateTime.UtcNow - t0);
            if (sleep > TimeSpan.Zero) await Task.Delay(sleep);
        }
    }
    finally
    {
        alive = false;
        try { waveIn?.StopRecording(); } catch { }
    }
}

static void SendPacket(NetworkStream stream, object sendLock, byte type, byte[] data, int len)
{
    lock (sendLock)
    {
        Span<byte> head = stackalloc byte[5];
        head[0] = type;
        BitConverter.TryWriteBytes(head[1..5], len);
        stream.Write(head);
        stream.Write(data, 0, len);
    }
}
