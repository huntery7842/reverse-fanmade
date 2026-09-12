using ReVerse.Relay.Desktop;

static void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);
    Console.WriteLine("PASS " + name);
}

const string sample = """
Windows IP Configuration

Ethernet adapter Ethernet:

   IPv4 Address. . . . . . . . . . . : 192.168.1.20

Ethernet adapter ZeroTier One [154a350c8623f717]:

   Link-local IPv6 Address . . . . . : fe80::6728:2a3c:b3e3:57ae%68
   IPv4 Address. . . . . . . . . . . : 10.205.138.26
   Subnet Mask . . . . . . . . . . . : 255.255.255.0

Ethernet adapter Another Adapter:

   IPv4 Address. . . . . . . . . . . : 172.26.112.1
""";

Check(ZeroTierHost.ParseAddress(sample) == "10.205.138.26", "ZeroTier IPv4 parsed from ipconfig");
Check(ZeroTierHost.ParseAddress(sample.Replace("ZeroTier", "Overlay")) is null, "non-ZeroTier adapter ignored");
Check(ZeroTierHost.BuildBackendUrl("10.205.138.26") == "http://10.205.138.26:6080", "player backend URL generated");

var folder = Path.Combine(Path.GetTempPath(), "Backend Folder");
var launcher = Path.Combine(Path.GetFullPath(folder), "Start-Backend.cmd");
var command = ZeroTierHost.BuildCommand(folder, "10.205.138.26");
Check(command == $"\"{launcher}\" -PublicHost 10.205.138.26 -HttpUrl http://10.205.138.26:6080", "backend command generated and quoted");

if (args.Contains("--live", StringComparer.OrdinalIgnoreCase))
    Check(await ZeroTierHost.DetectAddressAsync() is not null, "live ZeroTier address detected through ipconfig");
