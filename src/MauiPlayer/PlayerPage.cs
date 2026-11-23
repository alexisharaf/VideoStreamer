
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using StreamingPlayerLib;


namespace MauiPlayer;

public sealed class PlayerPage : ContentPage
{
    private readonly StreamClient _client = new();
    private readonly Entry _host = new() { Text = "127.0.0.1", Placeholder = "Host", WidthRequest = 150, BackgroundColor = Colors.White };
    private readonly Entry _port = new() { Text = StreamProtocol.DefaultPort.ToString(), Placeholder = "Port", Keyboard = Keyboard.Numeric, WidthRequest = 70, BackgroundColor = Colors.White };
    private readonly Button _connect = new() { Text = "Connect" };
    private readonly Label _status = new() { Text = "Disconnected", VerticalTextAlignment = TextAlignment.Center };

    // Заменили Image на SKGLView
    private readonly SKGLView _video = new() { };

    private readonly object _bitmapLock = new();
    private SKBitmap? _currentBitmap;
    private int _isDecoding;

    public PlayerPage()
    {
        Title = "Video Stream Player";
        BackgroundColor = Colors.Black;

        var bar = new HorizontalStackLayout { Padding = new Thickness(8), Spacing = 6, VerticalOptions = LayoutOptions.Center };
        bar.Children.Add(new Label { Text = "Host", VerticalTextAlignment = TextAlignment.Center });
        bar.Children.Add(_host);
        bar.Children.Add(new Label { Text = "Port", VerticalTextAlignment = TextAlignment.Center });
        bar.Children.Add(_port);
        bar.Children.Add(_connect);
        bar.Children.Add(_status);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        layout.Children.Add(bar);
        Grid.SetRow(bar, 0);
        layout.Children.Add(_video);
        Grid.SetRow(_video, 1);
        Content = layout;

        // Подписываемся на событие отрисовки OpenGL-поверхности
        _video.PaintSurface += OnPaintSurface;

        _connect.Clicked += OnConnectClicked;
        _client.StatusChanged += status => Dispatcher.Dispatch(() =>
        {
            _status.Text = status;
            _connect.Text = _client.IsConnected ? "Disconnect" : "Connect";
            if (status == "Disconnected")
            {
                ClearFrame();
            }
        });
        _client.ErrorOccurred += ex => Dispatcher.Dispatch(() => _status.Text = "Error: " + ex.Message);
        _client.VideoFrame += OnVideoFrame;
    }

    private void OnVideoFrame(byte[] jpeg)
    {
        // Если предыдущий кадр ещё декодируется — пропускаем этот, чтобы не копить очередь
        if (Interlocked.Exchange(ref _isDecoding, 1) != 0) return;

        try
        {
            // Декодируем JPEG сразу в фоновом потоке (очень быстро, без копирования массива)
            var newBitmap = SKBitmap.Decode(jpeg);
            if (newBitmap == null) return;

            SKBitmap? oldBitmap;
            lock (_bitmapLock)
            {
                oldBitmap = _currentBitmap;
                _currentBitmap = newBitmap;
            }

            // Освобождаем нативную память от старого кадра
            oldBitmap?.Dispose();

            // Запрашиваем перерисовку на UI-потоке
            Dispatcher.Dispatch(() => _video.InvalidateSurface());
        }
        finally
        {
            Interlocked.Exchange(ref _isDecoding, 0);
        }
    }

    private void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        //canvas.Clear(SKColors.Black);

        //SKBitmap? bitmapToDraw = null;
        lock (_bitmapLock)
        {
            if (_currentBitmap != null)
            {
                // Делаем поверхностную копию ссылки или рисуем внутри lock,
                // но чтобы не блокировать декодер надолго, лучше рисовать прямо под lock:
                DrawAspectFit(canvas, _currentBitmap, e.BackendRenderTarget.Width, e.BackendRenderTarget.Height);
            }
        }
    }

    private static void DrawAspectFit(SKCanvas canvas, SKBitmap bitmap, int viewWidth, int viewHeight)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0 || viewWidth <= 0 || viewHeight <= 0)
            return;

        // Аналог Aspect.AspectFit из MAUI Image
        float scale = Math.Min((float)viewWidth / bitmap.Width, (float)viewHeight / bitmap.Height);
        float destWidth = bitmap.Width * scale;
        float destHeight = bitmap.Height * scale;
        float left = (viewWidth - destWidth) / 2f;
        float top = (viewHeight - destHeight) / 2f;

        var destRect = new SKRect(left, top, left + destWidth, top + destHeight);
       // canvas.DrawBitmap(bitmap, destRect, new SKSamplingOptions(SKFilterMode.Linear));
        canvas.DrawBitmap(bitmap, destRect, new SKSamplingOptions(SKFilterMode.Nearest));
    }

    private void ClearFrame()
    {
        lock (_bitmapLock)
        {
            _currentBitmap?.Dispose();
            _currentBitmap = null;
        }
        _video.InvalidateSurface();
    }

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        if (_client.IsConnected)
        {
            _client.Disconnect();
            return;
        }
        if (!int.TryParse(_port.Text, out var port))
        {
            _status.Text = "Bad port";
            return;
        }

        _connect.IsEnabled = false;
        _status.Text = "Connecting...";
        try
        {
            await _client.ConnectAsync(_host.Text.Trim(), port);
        }
        catch (Exception ex)
        {
            _status.Text = "Error: " + ex.Message;
        }
        finally
        {
            _connect.IsEnabled = true;
        }
    }

    protected override void OnDisappearing()
    {
        _client.Disconnect();
        ClearFrame();
        base.OnDisappearing();
    }
}
