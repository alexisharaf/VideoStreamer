using StreamingPlayerLib;

namespace Player;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private readonly VideoPlayerControl _player = new() { Dock = DockStyle.Fill };
    private readonly TextBox _host = new() { Text = "127.0.0.1", Width = 140 };
    private readonly TextBox _port = new() { Text = StreamProtocol.DefaultPort.ToString(), Width = 60 };
    private readonly Button _connect = new() { Text = "Connect", AutoSize = true };
    private readonly Label _status = new() { Text = "Disconnected", AutoSize = true, Padding = new Padding(6, 8, 0, 0) };

    public MainForm()
    {
        Text = "Video Stream Player";
        ClientSize = new Size(900, 600);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36 };
        bar.Controls.Add(new Label { Text = "Host:", AutoSize = true, Padding = new Padding(6, 8, 0, 0) });
        bar.Controls.Add(_host);
        bar.Controls.Add(new Label { Text = "Port:", AutoSize = true, Padding = new Padding(6, 8, 0, 0) });
        bar.Controls.Add(_port);
        bar.Controls.Add(_connect);
        bar.Controls.Add(_status);

        Controls.Add(_player);
        Controls.Add(bar);

        _player.StatusChanged += (_, s) => SafeUi(() =>
        {
            _status.Text = s;
            if (!_player.IsConnected) _connect.Text = "Connect";
        });
        _player.ErrorOccurred += (_, ex) => SafeUi(() => _status.Text = "Error: " + ex.Message);

        _connect.Click += async (_, _) =>
        {
            if (_player.IsConnected)
            {
                _player.Disconnect();
                _connect.Text = "Connect";
                return;
            }
            if (!int.TryParse(_port.Text, out var p)) { _status.Text = "Bad port"; return; }
            _connect.Enabled = false;
            _status.Text = "Connecting...";
            try
            {
                await _player.ConnectAsync(_host.Text.Trim(), p);
                _connect.Text = "Disconnect";
            }
            catch (Exception ex)
            {
                _status.Text = "Error: " + ex.Message;
            }
            finally
            {
                _connect.Enabled = true;
            }
        };

        FormClosing += (_, _) => _player.Disconnect();
    }

    private void SafeUi(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(action); } catch { } }
        else action();
    }
}
