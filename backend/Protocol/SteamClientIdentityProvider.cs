using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using ReVerse.Capture.Configuration;

namespace ReVerse.Capture.Protocol;







public sealed class SteamClientIdentityProvider(
    SteamOptions options,
    ILogger<SteamClientIdentityProvider> logger) : IDisposable
{
    private readonly object gate = new();
    private SteamNativeApi? api;
    private DateTimeOffset retryAfter;
    private bool disposed;

    public bool TryGetCurrent(out SteamIdentity identity)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (api is null)
            {
                if (DateTimeOffset.UtcNow < retryAfter)
                {
                    identity = null!;
                    return false;
                }

                if (!TryInitialize())
                {
                    identity = null!;
                    return false;
                }
            }

            var currentApi = api;
            if (currentApi is null)
            {
                identity = null!;
                return false;
            }

            try
            {
                currentApi.RunCallbacks();
                var steamId = currentApi.GetSteamId();
                var nickname = currentApi.GetPersonaName();
                if (steamId == 0 || string.IsNullOrWhiteSpace(nickname))
                {
                    identity = null!;
                    return false;
                }

                identity = new SteamIdentity(steamId.ToString(CultureInfo.InvariantCulture), nickname);
                return true;
            }
            catch (Exception exception) when (exception is InvalidOperationException or SEHException)
            {
                logger.LogWarning("Steam client identity read failed: {ErrorType}", exception.GetType().Name);
                DisposeApi();
                retryAfter = DateTimeOffset.UtcNow.AddSeconds(5);
                identity = null!;
                return false;
            }
        }
    }

    private bool TryInitialize()
    {
        var path = FindNativeApiPath(options.NativeApiPath);
        if (path is null)
        {
            logger.LogWarning("Steam client identity unavailable: steam_api64.dll was not found");
            retryAfter = DateTimeOffset.UtcNow.AddSeconds(5);
            return false;
        }

        try
        {


            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SteamAppId")))
                Environment.SetEnvironmentVariable("SteamAppId", options.AppId.ToString(CultureInfo.InvariantCulture));

            var candidate = SteamNativeApi.Load(path);
            if (!candidate.Initialize())
            {
                candidate.Dispose();
                logger.LogWarning("Steam client identity unavailable: SteamAPI_Init returned false");
                retryAfter = DateTimeOffset.UtcNow.AddSeconds(5);
                return false;
            }

            api = candidate;
            logger.LogInformation("Steam client identity provider initialized from {Path}", path);
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or
            BadImageFormatException or InvalidOperationException or SEHException)
        {
            logger.LogWarning("Steam client identity unavailable: {ErrorType}", exception.GetType().Name);
            retryAfter = DateTimeOffset.UtcNow.AddSeconds(5);
            return false;
        }
    }

    private static string? FindNativeApiPath(string configuredPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath))
            candidates.Add(configuredPath);

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "steam_api64.dll"));

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            candidates.Add(Path.Combine(programFilesX86, "Steam", "steamapps", "common",
                "Resident Evil ReVerse", "steam_api64.dll"));
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "Steam", "steamapps", "common",
                "Resident Evil ReVerse", "steam_api64.dll"));
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var registryPath in new[]
            {
                @"Software\Valve\Steam",
                @"Software\WOW6432Node\Valve\Steam"
            })
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(registryPath);
                    var steamPath = key?.GetValue("SteamPath") as string;
                    if (!string.IsNullOrWhiteSpace(steamPath))
                        candidates.Add(Path.Combine(steamPath, "steamapps", "common",
                            "Resident Evil ReVerse", "steam_api64.dll"));
                }
                catch (Exception exception) when (exception is PlatformNotSupportedException or IOException or SecurityException)
                {

                }
            }
        }

        return candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    private void DisposeApi()
    {
        api?.Dispose();
        api = null;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;
            DisposeApi();
        }
    }

    private sealed class SteamNativeApi : IDisposable
    {
        private readonly nint library;
        private readonly SteamApiInitDelegate steamApiInit;
        private readonly SteamApiShutdownDelegate steamApiShutdown;
        private readonly SteamApiRunCallbacksDelegate steamApiRunCallbacks;
        private readonly GetSteamHandleDelegate getHSteamUser;
        private readonly GetSteamHandleDelegate getHSteamPipe;
        private readonly CreateInterfaceDelegate createInterface;
        private readonly GetSteamUserInterfaceDelegate getSteamUserInterface;
        private readonly GetSteamFriendsInterfaceDelegate getSteamFriendsInterface;
        private readonly GetSteamIdDelegate getSteamId;
        private readonly GetPersonaNameDelegate getPersonaName;
        private nint client;
        private nint user;
        private nint friends;
        private bool initialized;
        private bool disposed;

        private SteamNativeApi(nint library)
        {
            this.library = library;
            steamApiInit = GetDelegate<SteamApiInitDelegate>(library, "SteamAPI_Init");
            steamApiShutdown = GetDelegate<SteamApiShutdownDelegate>(library, "SteamAPI_Shutdown");
            steamApiRunCallbacks = GetDelegate<SteamApiRunCallbacksDelegate>(library, "SteamAPI_RunCallbacks");
            getHSteamUser = GetDelegate<GetSteamHandleDelegate>(library, "SteamAPI_GetHSteamUser");
            getHSteamPipe = GetDelegate<GetSteamHandleDelegate>(library, "SteamAPI_GetHSteamPipe");
            createInterface = GetDelegate<CreateInterfaceDelegate>(library, "SteamInternal_CreateInterface");
            getSteamUserInterface = GetDelegate<GetSteamUserInterfaceDelegate>(library,
                "SteamAPI_ISteamClient_GetISteamUser");
            getSteamFriendsInterface = GetDelegate<GetSteamFriendsInterfaceDelegate>(library,
                "SteamAPI_ISteamClient_GetISteamFriends");
            getSteamId = GetDelegate<GetSteamIdDelegate>(library, "SteamAPI_ISteamUser_GetSteamID");
            getPersonaName = GetDelegate<GetPersonaNameDelegate>(library, "SteamAPI_ISteamFriends_GetPersonaName");
        }

        public static SteamNativeApi Load(string path)
        {
            var library = NativeLibrary.Load(path);
            try
            {
                return new SteamNativeApi(library);
            }
            catch
            {
                NativeLibrary.Free(library);
                throw;
            }
        }

        public bool Initialize()
        {
            if (steamApiInit() == 0)
                return false;

            initialized = true;
            var steamUser = getHSteamUser();
            var steamPipe = getHSteamPipe();
            client = FindClientInterface();
            user = FindUserInterface(client, steamUser, steamPipe);
            friends = FindFriendsInterface(client, steamUser, steamPipe);
            return client != 0 && user != 0 && friends != 0;
        }

        public void RunCallbacks() => steamApiRunCallbacks();

        public ulong GetSteamId() => getSteamId(user);

        public string? GetPersonaName()
        {
            var pointer = getPersonaName(friends);
            return pointer == 0 ? null : Marshal.PtrToStringUTF8(pointer);
        }

        private nint FindClientInterface()
        {
            for (var version = 100; version >= 1; version--)
            {
                var pointer = createInterface($"SteamClient{version:000}");
                if (pointer != 0)
                    return pointer;
            }

            return 0;
        }

        private nint FindUserInterface(nint clientInterface, int steamUser, int steamPipe)
        {
            if (clientInterface == 0)
                return 0;

            for (var version = 100; version >= 1; version--)
            {
                var pointer = getSteamUserInterface(clientInterface, steamUser, steamPipe,
                    $"SteamUser{version:000}");
                if (pointer != 0)
                    return pointer;
            }

            return 0;
        }

        private nint FindFriendsInterface(nint clientInterface, int steamUser, int steamPipe)
        {
            if (clientInterface == 0)
                return 0;

            for (var version = 100; version >= 1; version--)
            {
                var pointer = getSteamFriendsInterface(clientInterface, steamUser, steamPipe,
                    $"SteamFriends{version:000}");
                if (pointer != 0)
                    return pointer;
            }

            return 0;
        }

        private static T GetDelegate<T>(nint library, string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            if (initialized)
                steamApiShutdown();
            NativeLibrary.Free(library);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte SteamApiInitDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SteamApiShutdownDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SteamApiRunCallbacksDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetSteamHandleDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nint CreateInterfaceDelegate([MarshalAs(UnmanagedType.LPStr)] string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nint GetSteamUserInterfaceDelegate(
            nint self, int steamUser, int steamPipe, [MarshalAs(UnmanagedType.LPStr)] string version);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nint GetSteamFriendsInterfaceDelegate(
            nint self, int steamUser, int steamPipe, [MarshalAs(UnmanagedType.LPStr)] string version);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ulong GetSteamIdDelegate(nint self);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nint GetPersonaNameDelegate(nint self);
    }
}
