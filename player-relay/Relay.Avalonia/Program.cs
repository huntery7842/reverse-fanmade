using Avalonia;

namespace ReVerse.Relay.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var instanceName = OperatingSystem.IsWindows() ? "Local\\ReVersePlayerRelay" : "ReVersePlayerRelay";
        using var singleInstance = new Mutex(true, instanceName, out var created);
        if (!created)
            return;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
