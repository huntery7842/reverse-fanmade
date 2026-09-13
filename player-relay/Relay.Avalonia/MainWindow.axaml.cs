using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ReVerse.Relay;

namespace ReVerse.Relay.Desktop;

public sealed partial class MainWindow : Window
{
    private const string LaunchOptions = "/Rebe/HjmUriStr:http://127.0.0.1:5080";
    private static bool SupportsBackendHosting => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();
    private static readonly IBrush StoppedBrush = new SolidColorBrush(Color.Parse("#9FB0C8"));
    private static readonly IBrush WorkingBrush = new SolidColorBrush(Color.Parse("#F6C85F"));
    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#68D391"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FC8181"));
    private readonly BackendHostController backendHost = new();
    private RelaySettings settings = new();
    private RelayService? service;
    private bool busy;

    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => StopForShutdown();
        LoadSettings();
        BackendHostPanel.IsVisible = SupportsBackendHosting;
        if (SupportsBackendHosting)
        {
            backendHost.RunningChanged += OnBackendRunningChanged;
            Opened += async (_, _) => await DetectZeroTierAddressAsync();
        }
    }

    private void LoadSettings()
    {
        try
        {
            settings = RelaySettings.Load();
            UsernameEntry.Text = settings.Username;
            BackendEntry.Text = settings.BackendAddress;
            HostFolderEntry.Text = settings.BackendFolder;
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

    private void OnHostConfigurationChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateHostCommand();
    }

    private void OnBackendCommandChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateStartBackendAvailability();
    }

    private async void OnDetectAddress(object? sender, RoutedEventArgs e)
    {
        await DetectZeroTierAddressAsync();
    }

    private async Task DetectZeroTierAddressAsync()
    {
        DetectAddressButton.IsEnabled = false;
        BackendHostStatusText.Text = "Detecting ZeroTier address…";
        BackendHostStatusText.Foreground = WorkingBrush;
        try
        {
            var address = await ZeroTierHost.DetectAddressAsync();
            if (address is null)
                throw new InvalidOperationException("No active ZeroTier IPv4 address was found. Connect ZeroTier or enter the address manually.");
            ZeroTierAddressEntry.Text = address;
            BackendHostStatusText.Text = "ZeroTier address detected";
            BackendHostStatusText.Foreground = RunningBrush;
        }
        catch (Exception ex)
        {
            BackendHostStatusText.Text = ex.Message;
            BackendHostStatusText.Foreground = ErrorBrush;
        }
        finally
        {
            DetectAddressButton.IsEnabled = !backendHost.IsRunning;
            UpdateHostCommand();
        }
    }

    private async void OnBrowseBackendFolder(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select the backend folder",
                AllowMultiple = false
            });
            if (folders.Count == 0)
                return;
            HostFolderEntry.Text = folders[0].Path.LocalPath;
            settings.BackendFolder = HostFolderEntry.Text;
            settings.Save();
            UpdateHostCommand();
        }
        catch (Exception ex)
        {
            BackendHostStatusText.Text = ex.Message;
            BackendHostStatusText.Foreground = ErrorBrush;
        }
    }

    private void UpdateHostCommand()
    {
        var folder = HostFolderEntry.Text?.Trim() ?? "";
        var address = ZeroTierAddressEntry.Text?.Trim() ?? "";
        try
        {
            if (folder.Length == 0 || address.Length == 0)
            {
                BackendCommandEntry.Text = "";
                PlayerBackendUrlEntry.Text = "";
                CopyBackendUrlButton.IsEnabled = false;
                StartBackendButton.IsEnabled = false;
                return;
            }
            BackendCommandEntry.Text = ZeroTierHost.BuildCommand(folder, address);
            PlayerBackendUrlEntry.Text = ZeroTierHost.BuildBackendUrl(address);
            CopyBackendUrlButton.IsEnabled = true;
            UpdateStartBackendAvailability();
        }
        catch (Exception ex)
        {
            BackendCommandEntry.Text = "";
            PlayerBackendUrlEntry.Text = "";
            CopyBackendUrlButton.IsEnabled = false;
            BackendHostStatusText.Text = ex.Message;
            BackendHostStatusText.Foreground = ErrorBrush;
            StartBackendButton.IsEnabled = false;
        }
    }

    private async void OnCopyBackendUrlClicked(object? sender, RoutedEventArgs e)
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        var url = PlayerBackendUrlEntry.Text;
        if (clipboard is null || string.IsNullOrWhiteSpace(url))
            return;
        await clipboard.SetTextAsync(url);
        CopyBackendUrlButton.Content = "Copied";
        await Task.Delay(1200);
        CopyBackendUrlButton.Content = "Copy";
    }

    private void OnStartBackend(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folder = HostFolderEntry.Text?.Trim() ?? "";
            var command = BackendCommandEntry.Text ?? "";
            var backendUrl = PlayerBackendUrlEntry.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(backendUrl))
                throw new InvalidOperationException("Detect a valid ZeroTier address before starting the backend.");
            settings.BackendFolder = folder;
            settings.BackendAddress = backendUrl;
            settings.Save();
            backendHost.Start(folder, command);
            BackendEntry.Text = backendUrl;
            BackendHostStatusText.Text = "Backend running; local relay URL filled in automatically";
            BackendHostStatusText.Foreground = RunningBrush;
        }
        catch (Exception ex)
        {
            BackendHostStatusText.Text = ex.Message;
            BackendHostStatusText.Foreground = ErrorBrush;
        }
    }

    private async void OnStopBackend(object? sender, RoutedEventArgs e)
    {
        StopBackendButton.IsEnabled = false;
        BackendHostStatusText.Text = "Stopping backend…";
        BackendHostStatusText.Foreground = WorkingBrush;
        try
        {
            await backendHost.StopAsync();
            BackendHostStatusText.Text = "Backend stopped";
            BackendHostStatusText.Foreground = StoppedBrush;
        }
        catch (Exception ex)
        {
            BackendHostStatusText.Text = ex.Message;
            BackendHostStatusText.Foreground = ErrorBrush;
        }
        UpdateStartBackendAvailability();
    }

    private void OnBackendRunningChanged(bool running)
    {
        Dispatcher.UIThread.Post(() => SetBackendRunning(running));
    }

    private void SetBackendRunning(bool running)
    {
        HostFolderEntry.IsEnabled = !running;
        BrowseBackendButton.IsEnabled = !running;
        ZeroTierAddressEntry.IsEnabled = !running;
        DetectAddressButton.IsEnabled = !running;
        StartBackendButton.IsEnabled = !running;
        StopBackendButton.IsEnabled = running;
        if (!running)
        {
            BackendHostStatusText.Text = "Backend stopped";
            BackendHostStatusText.Foreground = StoppedBrush;
            UpdateStartBackendAvailability();
        }
    }

    private void UpdateStartBackendAvailability()
    {
        var folder = HostFolderEntry.Text?.Trim().Trim('"') ?? "";
        StartBackendButton.IsEnabled = !backendHost.IsRunning &&
                                       !string.IsNullOrWhiteSpace(BackendCommandEntry.Text) &&
                                       File.Exists(ZeroTierHost.GetBackendEntryPath(folder));
    }

    private void StopForShutdown()
    {
        var old = service;
        service = null;
        backendHost.RunningChanged -= OnBackendRunningChanged;
        Task.Run(async () =>
        {
            if (old is not null)
                await old.DisposeAsync();
            await backendHost.DisposeAsync();
        }).GetAwaiter().GetResult();
    }
}
