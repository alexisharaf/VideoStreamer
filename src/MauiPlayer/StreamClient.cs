using System.Net.Sockets;
using StreamingPlayerLib;

namespace MauiPlayer;

internal sealed class StreamClient
{
    private TcpClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private PcmSink? _audio;

    public event Action<string>? StatusChanged;
    public event Action<Exception>? ErrorOccurred;
    public event Action<byte[]>? VideoFrame;

    public bool IsConnected => _client?.Connected == true && _receiveTask is { IsCompleted: false };

    public async Task ConnectAsync(string host, int port)
    {
        Disconnect();
        var client = new TcpClient();
        await client.ConnectAsync(host, port);
        _client = client;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _receiveTask = Task.Run(() => ReceiveAsync(client, ct));
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        _client?.Close();
        try { _receiveTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _cts?.Dispose(); _cts = null;
        _client?.Dispose(); _client = null;
        _receiveTask = null;
        _audio?.Dispose(); _audio = null;
        StatusChanged?.Invoke("Disconnected");
    }

    private async Task ReceiveAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using var stream = client.GetStream();
            var magic = await ReadExactAsync(stream, StreamProtocol.Magic.Length, ct);
            if (!magic.AsSpan().SequenceEqual(StreamProtocol.Magic))
                throw new InvalidDataException("Invalid stream header.");

            var header = await ReadExactAsync(stream, 16, ct); // w,h,rate,channels,bits
            int w = BitConverter.ToInt32(header, 0);
            int h = BitConverter.ToInt32(header, 4);
            int rate = BitConverter.ToInt32(header, 8);
            short ch = BitConverter.ToInt16(header, 12);
            short bits = BitConverter.ToInt16(header, 14);
            _audio = new PcmSink();
            _audio.Start(rate, ch, bits);
            StatusChanged?.Invoke($"Connected {w}x{h}, {rate}Hz {ch}ch");

            while (!ct.IsCancellationRequested)
            {
                int type = stream.ReadByte();
                if (type < 0) break;
                int len = BitConverter.ToInt32(await ReadExactAsync(stream, 4, ct), 0);
                if (len <= 0 || len > 50_000_000) throw new InvalidDataException($"Bad packet length {len}");
                var payload = await ReadExactAsync(stream, len, ct);
                if (type == StreamProtocol.PacketVideo) VideoFrame?.Invoke(payload);
                else if (type == StreamProtocol.PacketAudio) _audio.Write(payload, len);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorOccurred?.Invoke(ex); }
        finally
        {
            _audio?.Dispose();
            _audio = null;
            StatusChanged?.Invoke("Disconnected");
        }
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken ct)
    {
        var buf = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buf.AsMemory(read, count - read), ct);
            if (n == 0) throw new EndOfStreamException();
            read += n;
        }
        return buf;
    }
}
