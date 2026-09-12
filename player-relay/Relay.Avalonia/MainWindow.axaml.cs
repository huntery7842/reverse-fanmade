using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ReVerse.Relay;

namespace ReVerse.Relay.Desktop;

public sealed partial class MainWindow : Window
{
    private const string LaunchOptions = "/Rebe/HjmUriStr:http://127.0.0.1:5080";
    private static readonly IBrush StoppedBrush = new SolidColorBrush(Color.Parse("#9FB0C8"));
    private static readonly IBrush WorkingBrush = new SolidColorBrush(Color.Parse("#F6C85F"));
    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#68D391"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FC8181"));
    private RelaySettings settings = new();
    private RelayService? service;
    private bool busy;

    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => StopForShutdown();
        LoadSettings();
    }

    private void LoadSettings()
    {
        try
        {
            settings = RelaySettings.Load();
            UsernameEntry.Text = settings.Username;
            BackendEntry.Text = settings.BackendAddress;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            ToggleButton.IsEnabled = false;
            SetStatus("Settings could not be loaded", ErrorBrush);
        }
    }

    private async void OnToggleClicked(object? sender, RoutedEventArgs e)
    {
        await ToggleAsync();
    }

    private async void OnBackendKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && service is null)
            await ToggleAsync();
    }

    private async Task ToggleAsync()
    {
        if (busy)
            return;
        busy = true;
        ToggleButton.IsEnabled = false;
        BusyOverlay.IsVisible = true;
        HideError();
        try
        {
            if (service is not null)
            {
                await StopAsync();
                return;
            }

            settings.Username = UsernameEntry.Text ?? "";
            settings.SecretKey = SecretEntry.Text ?? "";
            settings.BackendAddress = BackendEntry.Text ?? "";
            settings.Validate();
            settings.Save();
            SetInputsEnabled(false);
            SetStatus("Signing in…", WorkingBrush);

            var current = new RelayService();
            service = current;
            current.StatusChanged += message => Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(service, current))
                    SetStatus(message, StatusBrush(message));
            });
            await current.StartAsync(settings);
            ToggleButton.Content = "Stop relay";
        }
        catch (Exception ex)
        {
            await StopAsync();
            SetStatus("Could not start relay", ErrorBrush);
            ShowError(ex.Message + " Check the server address and make sure ports 5080 and 5081 are free.");
        }
        finally
        {
            busy = false;
            BusyOverlay.IsVisible = false;
            ToggleButton.IsEnabled = true;
        }
    }

    private async Task StopAsync()
    {
        var old = service;
        service = null;
        if (old is not null)
            await old.DisposeAsync();
        SetInputsEnabled(true);
        ToggleButton.Content = "Start relay";
        SetStatus("Stopped", StoppedBrush);
    }

    private void SetInputsEnabled(bool enabled)
    {
        UsernameEntry.IsEnabled = enabled;
        SecretEntry.IsEnabled = enabled;
        BackendEntry.IsEnabled = enabled;
    }

    private void SetStatus(string message, IBrush brush)
    {
        StatusText.Text = message;
        StatusDot.Background = brush;
    }

    private static IBrush StatusBrush(string message)
    {
        if (message.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("expired", StringComparison.OrdinalIgnoreCase))
            return ErrorBrush;
        if (message.StartsWith("Running", StringComparison.OrdinalIgnoreCase))
            return RunningBrush;
        return WorkingBrush;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    private void HideError()
    {
        ErrorText.Text = "";
        ErrorText.IsVisible = false;
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;
        await clipboard.SetTextAsync(LaunchOptions);
        CopyButton.Content = "Copied";
        await Task.Delay(1200);
        CopyButton.Content = "Copy";
    }

    private void StopForShutdown()
    {
        var old = service;
        service = null;
        if (old is not null)
            Task.Run(async () => await old.DisposeAsync()).GetAwaiter().GetResult();
    }
}
