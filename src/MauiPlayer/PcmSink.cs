#if ANDROID
using Android.Media;
#endif
#if WINDOWS
using NAudio.Wave;
#endif

namespace MauiPlayer;

// The stream carries PCM16 only, so both platforms get a raw sample sink.
internal sealed class PcmSink : IDisposable
{
#if ANDROID
    private AudioTrack? _track;

    public void Start(int sampleRate, int channels, int bits)
    {
        if (bits != 16) throw new NotSupportedException($"PCM{bits} is not supported");
        var channel = channels == 1 ? ChannelOut.Mono : ChannelOut.Stereo;
        int size = Math.Max(AudioTrack.GetMinBufferSize(sampleRate, channel, Android.Media.Encoding.Pcm16bit),
                            sampleRate * channels * sizeof(short) / 5);
        _track = new AudioTrack(Android.Media.Stream.Music, sampleRate, channel, Android.Media.Encoding.Pcm16bit, size, AudioTrackMode.Stream);
        _track.Play();
    }

    public void Write(byte[] data, int length) => _track?.Write(data, 0, length, WriteMode.NonBlocking);

    public void Dispose()
    {
        if (_track is null) return;
        try { _track.Stop(); } catch { }
        try { _track.Release(); } catch { }
        _track.Dispose();
        _track = null;
    }
#elif WINDOWS
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _buffer;

    public void Start(int sampleRate, int channels, int bits)
    {
        _buffer = new BufferedWaveProvider(new WaveFormat(sampleRate, bits, channels))
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(2)
        };
        _waveOut = new WaveOutEvent { DesiredLatency = 120 };
        _waveOut.Init(_buffer);
        _waveOut.Play();
    }

    public void Write(byte[] data, int length) => _buffer?.AddSamples(data, 0, length);

    public void Dispose()
    {
        try { _waveOut?.Stop(); } catch { }
        _waveOut?.Dispose();
        _waveOut = null;
        _buffer = null;
    }
#else
    public void Start(int sampleRate, int channels, int bits) { }
    public void Write(byte[] data, int length) { }
    public void Dispose() { }
#endif
}
