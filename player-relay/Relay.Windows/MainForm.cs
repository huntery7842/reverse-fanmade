namespace ReVerse.Relay;

public sealed class MainForm : Form
{
    private readonly TextBox username = new() { Dock = DockStyle.Fill, MaxLength = 64 };
    private readonly TextBox secret = new() { Dock = DockStyle.Fill, MaxLength = 1024, UseSystemPasswordChar = true };
    private readonly TextBox backend = new() { Dock = DockStyle.Fill, PlaceholderText = "https://server.example.com" };
    private readonly Button toggle = new() { Text = "Start service", AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label status = new() { Text = "Stopped", AutoSize = true, Dock = DockStyle.Fill };
    private RelaySettings settings = new();
    private RelayService? service;
    private bool closing;
    private bool busy;

    public MainForm()
    {
        Text = "ReVerse Relay";
        ClientSize = new Size(480, 390);
        MinimumSize = new Size(420, 430);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 10 };
        layout.Controls.Add(new Label { Text = "Username", AutoSize = true });
        layout.Controls.Add(username);
        layout.Controls.Add(new Label { Text = "Secret key", AutoSize = true, Margin = new Padding(3, 12, 3, 3) });
        layout.Controls.Add(secret);
        layout.Controls.Add(new Label { Text = "Backend server address", AutoSize = true, Margin = new Padding(3, 12, 3, 3) });
        layout.Controls.Add(backend);
        layout.Controls.Add(toggle);
        layout.Controls.Add(status);
        layout.Controls.Add(new Label { Text = "Game launch options:", AutoSize = true });
        layout.Controls.Add(new TextBox { Text = "/Rebe/HjmUriStr:http://127.0.0.1:5080", ReadOnly = true, Dock = DockStyle.Fill });
        toggle.Margin = new Padding(3, 16, 3, 10);
        Controls.Add(layout);
        try { settings = RelaySettings.Load(); }
        catch (Exception ex) { status.Text = "Settings could not be loaded"; Shown += (_, _) => MessageBox.Show(this, ex.Message, "Settings error"); toggle.Enabled = false; }
        username.Text = settings.Username;
        backend.Text = settings.BackendAddress;
        toggle.Click += async (_, _) => await ToggleAsync();
        FormClosing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true;
            if (busy) return;
            busy = true; toggle.Enabled = false;
            try { await StopAsync(); }
            finally { closing = true; Close(); }
        };
    }

    private async Task ToggleAsync()
    {
        if (busy) return;
        busy = true; toggle.Enabled = false;
        try
        {
            if (service is not null) await StopAsync();
            else
            {
                settings.Username = username.Text;
                settings.SecretKey = secret.Text;
                settings.BackendAddress = backend.Text;
                settings.Validate(); settings.Save();
                username.Enabled = secret.Enabled = backend.Enabled = false;
                status.Text = "Signing in…";
                service = new RelayService();
                var current = service;
                service.StatusChanged += message =>
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(() => { if (ReferenceEquals(service, current)) status.Text = message; });
                };
                await service.StartAsync(settings);
                toggle.Text = "Stop service";
            }
        }
        catch (Exception ex)
        {
            await StopAsync();
            status.Text = "Could not start service";
            MessageBox.Show(this, ex.Message + "\n\nCheck the server address and that ports 5080 and 5081 are free.", "Relay error");
        }
        finally { busy = false; toggle.Enabled = true; }
    }

    private async Task StopAsync()
    {
        var old = service; service = null;
        if (old is not null) await old.DisposeAsync();
        username.Enabled = secret.Enabled = backend.Enabled = true;
        toggle.Text = "Start service"; status.Text = "Stopped";
    }
}
