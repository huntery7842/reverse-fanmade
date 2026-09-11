namespace ReVerse.Relay;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var singleInstance = new Mutex(true, "Local\\ReVersePlayerRelay", out var created);
        if (!created) { MessageBox.Show("ReVerse Relay is already open."); return; }
        Application.Run(new MainForm());
    }
}
