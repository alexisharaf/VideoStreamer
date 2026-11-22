using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using StreamingPlayerLib;

namespace MauiPlayer;

public sealed class PlayerPage : ContentPage
{
    private readonly StreamClient _client = new();
    private readonly Entry _host = new() { Text = "127.0.0.1", Placeholder = "Host", WidthRequest = 150 };
    private readonly Entry _port = new() { Text = StreamProtocol.DefaultPort.ToString(), Placeholder = "Port", Keyboard = Keyboard.Numeric, WidthRequest = 70 };
    private readonly Button _connect = new() { Text = "Connect" };
    private readonly Label _status = new() { Text = "Disconnected", VerticalTextAlignment = TextAlignment.Center };
    private readonly Image _video = new() { BackgroundColor = Colors.Black, Aspect = Aspect.AspectFit };

    private readonly object _frameLock = new();
    private byte[]? _frame;
    private int _framePending;

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

        _connect.Clicked += OnConnectClicked;
        _client.StatusChanged += status => Dispatcher.Dispatch(() =>
        {
            _status.Text = status;
            _connect.Text = _client.IsConnected ? "Disconnect" : "Connect";
            if (status == "Disconnected") _video.Source = null;
        });
        _client.ErrorOccurred += ex => Dispatcher.Dispatch(() => _status.Text = "Error: " + ex.Message);
        _client.VideoFrame += OnVideoFrame;
    }

    private void OnVideoFrame(byte[] jpeg)
    {
        // Keep only the newest frame: at 15 fps an async ImageSource load can fall behind.
        lock (_frameLock) _frame = (byte[])jpeg.Clone();
        if (Interlocked.Exchange(ref _framePending, 1) != 0) return;
        Dispatcher.Dispatch(() =>
        {
            byte[] frame;
            lock (_frameLock) { frame = _frame!; _frame = null; }
            Interlocked.Exchange(ref _framePending, 0);
            _video.Source = ImageSource.FromStream(() => new MemoryStream(frame, writable: false));
        });
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
        base.OnDisappearing();
    }
}
