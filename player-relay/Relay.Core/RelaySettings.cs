using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReVerse.Relay;

public sealed class RelaySettings
{
    public string Username { get; set; } = "";
    public string BackendAddress { get; set; } = "";
    public string BackendFolder { get; set; } = "";
    [JsonIgnore]
    public string SecretKey { get; set; } = "";

    public Uri Validate()
    {
        Username = Username.Trim();
        BackendAddress = BackendAddress.Trim().TrimEnd('/');
        if (Username.Length is < 1 or > 64 || Username.Any(char.IsControl))
            throw new ArgumentException("Enter a username of 1–64 characters without control characters.");
        if (SecretKey.Length is < 1 or > 1024)
            throw new ArgumentException("Enter a secret key of 1–1024 characters.");
        if (!Uri.TryCreate(BackendAddress, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.AbsolutePath != "/" ||
            uri.Query != "" || uri.Fragment != "" || uri.UserInfo != "")
            throw new ArgumentException("Enter a backend URL such as https://server.example.com or http://192.168.1.10:5080, without a path.");
        return uri;
    }

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReVerseRelay");

    public static RelaySettings Load()
    {
        var path = Path.Combine(DataDirectory, "settings.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize<RelaySettings>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Saved settings are empty.")
            : new RelaySettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        var path = Path.Combine(DataDirectory, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}
