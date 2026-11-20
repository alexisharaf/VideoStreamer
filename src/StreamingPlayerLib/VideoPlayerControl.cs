using System.Drawing;
using System.Net.Sockets;
using System.Windows.Forms;
using NAudio.Wave;

namespace StreamingPlayerLib;

/// <summary>Embeddable WinForms control that receives and plays a video+audio stream from Streamer.</summary>
public class VideoPlayerControl : UserControl
{
    private TcpClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Image? _frame;
    private readonly object _frameLock = new();
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _audioBuffer;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<Exception>? ErrorOccurred;

    public bool IsConnected => _client?.Connected == true && _receiveTask is { IsCompleted: false };

    public VideoPlayerControl()
    {
        DoubleBuffered = true;
        BackColor = Color.Black;
    }

    public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        Disconnect();
        var client = new TcpClient();
        await client.ConnectAsync(host, port, ct);
        _client = client;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    public void Disconnect()
    {
        try { _cts?.Cancel(); } catch { }
        try { _client?.Close(); } catch { }
        try { _receiveTask?.Wait(1000); } catch { }
        _cts?.Dispose(); _cts = null;
        _client?.Dispose(); _client = null;
        _receiveTask = null;
        StopAudio();
        lock (_frameLock) { _frame?.Dispose(); _frame = null; }
        Invalidate();
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            using var stream = _client!.GetStream();
            var magic = await ReadExactAsync(stream, StreamProtocol.Magic.Length, ct);
            if (!magic.AsSpan().SequenceEqual(StreamProtocol.Magic))
                throw new InvalidDataException("Invalid stream header.");

            var header = await ReadExactAsync(stream, 16, ct); // w,h,rate,channels,bits
            int w = BitConverter.ToInt32(header, 0);
            int h = BitConverter.ToInt32(header, 4);
            int rate = BitConverter.ToInt32(header, 8);
            short ch = BitConverter.ToInt16(header, 12);
            short bits = BitConverter.ToInt16(header, 14);
            StartAudio(new WaveFormat(rate, bits, ch));
            StatusChanged?.Invoke(this, $"Connected {w}x{h}, {rate}Hz {ch}ch");

            while (!ct.IsCancellationRequested)
            {
                int type = stream.ReadByte();
                if (type < 0) break;
                var lenBuf = await ReadExactAsync(stream, 4, ct);
                int len = BitConverter.ToInt32(lenBuf, 0);
                if (len <= 0 || len > 50_000_000) throw new InvalidDataException($"Bad packet length {len}");
                var payload = await ReadExactAsync(stream, len, ct);
                if (type == StreamProtocol.PacketVideo) OnVideo(payload);
                else if (type == StreamProtocol.PacketAudio) OnAudio(payload);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorOccurred?.Invoke(this, ex); }
        finally { StatusChanged?.Invoke(this, "Disconnected"); }
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

    private void StartAudio(WaveFormat format)
    {
        if (InvokeRequired) { BeginInvoke(() => StartAudio(format)); return; }
        StopAudio();
        _audioBuffer = new BufferedWaveProvider(format) { DiscardOnBufferOverflow = true, BufferDuration = TimeSpan.FromSeconds(2) };
        _waveOut = new WaveOutEvent { DesiredLatency = 120 };
        _waveOut.Init(_audioBuffer);
        _waveOut.Play();
    }

    private void OnAudio(byte[] data)
    {
        _audioBuffer?.AddSamples(data, 0, data.Length);
    }

    private void OnVideo(byte[] jpeg)
    {
        Image img;
        using (var ms = new MemoryStream(jpeg))
            img = Image.FromStream(ms);
        lock (_frameLock)
        {
            var old = _frame;
            _frame = img;
            old?.Dispose();
        }
        Invalidate();
    }

    private void StopAudio()
    {
        if (InvokeRequired) { try { Invoke(StopAudio); } catch { } return; }
        try { _waveOut?.Stop(); } catch { }
        _waveOut?.Dispose(); _waveOut = null;
        _audioBuffer = null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Image? img;
        lock (_frameLock) img = _frame;
        if (img == null) return;
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
        var dst = Fit(ClientSize, img.Size);
        e.Graphics.DrawImage(img, dst);
    }

    private static Rectangle Fit(Size box, Size src)
    {
        if (src.Width <= 0 || src.Height <= 0 || box.Width <= 0 || box.Height <= 0) return Rectangle.Empty;
        float scale = Math.Min((float)box.Width / src.Width, (float)box.Height / src.Height);
        int w = (int)(src.Width * scale), h = (int)(src.Height * scale);
        return new Rectangle((box.Width - w) / 2, (box.Height - h) / 2, w, h);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Disconnect();
        base.Dispose(disposing);
    }
}
