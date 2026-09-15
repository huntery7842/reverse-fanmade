using System.Globalization;
using ReVerse.Relay;

namespace ReVerse.Relay.Desktop;

public interface ITranslation
{
    string Code { get; }
    string LanguageName { get; }
    string WindowTitle { get; }
    string BrandPrefix { get; }
    string BrandAccent { get; }
    string BrandSuffix { get; }
    string Tagline { get; }
    string LanguageSelectorTooltip { get; }
    string HostBackendButton { get; }
    string RelayOnlyButton { get; }
    string HostBackendTooltip { get; }
    string RelayOnlyTooltip { get; }
    string PlayerRelayTitle { get; }
    string PlayerRelayDescription { get; }
    string UsernameLabel { get; }
    string UsernamePlaceholder { get; }
    string BackendServerLabel { get; }
    string BackendServerPlaceholder { get; }
    string BackendServerTooltip { get; }
    string SteamLaunchOptionLabel { get; }
    string SteamLaunchOptionTooltip { get; }
    string CopyButton { get; }
    string CopiedButton { get; }
    string CopyLaunchOptionTooltip { get; }
    string StartRelayButton { get; }
    string StopRelayButton { get; }
    string StartRelayTooltip { get; }
    string StopRelayTooltip { get; }
    string StatusStopped { get; }
    string StatusSigningIn { get; }
    string StatusRunningWaitingForGame { get; }
    string StatusRunningConnectedToBackend { get; }
    string StatusBackendConnectionFailed { get; }
    string StatusSessionExpired { get; }
    string StatusStartFailed { get; }
    string RelayPorts { get; }
    string BackendHostTitle { get; }
    string BackendHostDescription { get; }
    string BackendFolderLabel { get; }
    string BackendFolderTooltip { get; }
    string BackendFolderPlaceholder { get; }
    string BrowseButton { get; }
    string BrowseBackendTooltip { get; }
    string ZeroTierAddressLabel { get; }
    string ZeroTierAddressTooltip { get; }
    string ZeroTierAddressPlaceholder { get; }
    string DetectButton { get; }
    string DetectAddressTooltip { get; }
    string PlayersLabel { get; }
    string PlayersTooltip { get; }
    string PlayerConnectionUrlLabel { get; }
    string PlayerConnectionUrlPlaceholder { get; }
    string CopyPlayerUrlTooltip { get; }
    string PlayerConnectionUrlDescription { get; }
    string AdvancedCommandHeader { get; }
    string StartBackendButton { get; }
    string StartBackendTooltip { get; }
    string StopBackendButton { get; }
    string StopBackendTooltip { get; }
    string BackendStoppedStatus { get; }
    string BackendRunningStatus { get; }
    string BackendStoppingStatus { get; }
    string BackendPorts { get; }
    string PleaseWait { get; }
    string SettingsLoadFailureStatus { get; }
    string DetectingZeroTierStatus { get; }
    string ZeroTierDetectedStatus { get; }
    string SelectBackendFolderDialogTitle { get; }
    string BackendFolderMissingFormat { get; }
    string RelayStartErrorSuffix { get; }
    string ErrorInvalidUsername { get; }
    string ErrorInvalidSecretKey { get; }
    string ErrorInvalidBackendUrl { get; }
    string ErrorSavedSettingsEmpty { get; }
    string ErrorRelayAlreadyRunning { get; }
    string ErrorBackendPointsToRelay { get; }
    string ErrorAccountSignInFailedFormat { get; }
    string ErrorBackendEmptyAccountResponse { get; }
    string ErrorBackendInvalidAccountResponse { get; }
    string ErrorDetectionTimedOut { get; }
    string ErrorIpconfigCouldNotStart { get; }
    string ErrorIpconfigFailed { get; }
    string ErrorIpconfigFailedFormat { get; }
    string ErrorNoZeroTierAddress { get; }
    string ErrorInvalidPlayerCount { get; }
    string ErrorInvalidZeroTierAddress { get; }
    string ErrorBackendFolderDoesNotExist { get; }
    string ErrorBackendExecutableMissingFormat { get; }
    string ErrorBackendCommandRequired { get; }
    string ErrorBackendAlreadyRunning { get; }
    string ErrorBackendProcessCouldNotStart { get; }
    string ErrorBackendExecutablePermission { get; }
    string ErrorBackendUrlRequired { get; }
    string GetRelayStatusText(RelayStatus status);
    string GetRelayErrorText(RelayErrorCode code, int? statusCode);
    string GetDesktopErrorText(DesktopErrorCode code, string? detail, string? entryName);
    string GetBackendFolderMissingText(string entryName);
    string GetRelayStartErrorText(string details);
}

public interface ILocalizationService
{
    ITranslation Current { get; }
    IReadOnlyList<ITranslation> AvailableLanguages { get; }
    event EventHandler? LanguageChanged;
    bool SetLanguage(string? code);
}

public sealed class TranslationService : ILocalizationService
{
    private static readonly IReadOnlyList<ITranslation> Languages =
        new ITranslation[]
        {
            new EnglishTranslation(),
            new UkrainianTranslation(),
            new ChineseTranslation(),
            new SpanishTranslation()
        };

    public ITranslation Current { get; private set; } = Languages[0];
    public IReadOnlyList<ITranslation> AvailableLanguages => Languages;
    public event EventHandler? LanguageChanged;

    public bool SetLanguage(string? code)
    {
        var language = Languages.FirstOrDefault(item =>
            string.Equals(item.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (language is null || ReferenceEquals(language, Current))
            return false;

        Current = language;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
}

public sealed class EnglishTranslation : ITranslation
{
    public string Code => "en";
    public string LanguageName => "English";
    public string WindowTitle => "ReVerse Relay";
    public string BrandPrefix => "RE";
    public string BrandAccent => "VERSE";
    public string BrandSuffix => " RELAY";
    public string Tagline => "COMMUNITY CONNECTION UTILITY";
    public string LanguageSelectorTooltip => "Select the interface language.";
    public string HostBackendButton => "HOST BACKEND  ›";
    public string RelayOnlyButton => "‹  RELAY ONLY";
    public string HostBackendTooltip => "Expand the window to show backend hosting controls.";
    public string RelayOnlyTooltip => "Collapse the window to show only relay controls.";
    public string PlayerRelayTitle => "Player relay";
    public string PlayerRelayDescription => "Connect this computer to the host.";
    public string UsernameLabel => "USERNAME";
    public string UsernamePlaceholder => "Enter a player name";
    public string BackendServerLabel => "BACKEND SERVER";
    public string BackendServerPlaceholder => "http://10.x.x.x:6080";
    public string BackendServerTooltip => "This address is provided by the host. Ask them for the Player connection URL. Do not enter localhost or 127.0.0.1 here.";
    public string SteamLaunchOptionLabel => "STEAM LAUNCH OPTION";
    public string SteamLaunchOptionTooltip => "Paste this into the game's Steam launch options. The local address is intentional: the game talks to this relay first.";
    public string CopyButton => "COPY";
    public string CopiedButton => "COPIED";
    public string CopyLaunchOptionTooltip => "Copy the Steam launch option.";
    public string StartRelayButton => "START RELAY";
    public string StopRelayButton => "STOP RELAY";
    public string StartRelayTooltip => "Sign in and start the local relay.";
    public string StopRelayTooltip => "Stop the local relay.";
    public string StatusStopped => "Stopped";
    public string StatusSigningIn => "Signing in…";
    public string StatusRunningWaitingForGame => "Running — waiting for game";
    public string StatusRunningConnectedToBackend => "Running — connected to backend";
    public string StatusBackendConnectionFailed => "Running — backend connection failed";
    public string StatusSessionExpired => "Session expired — stop and start service";
    public string StatusStartFailed => "Could not start relay";
    public string RelayPorts => "PORTS 5080 / 5081";
    public string BackendHostTitle => "Backend host";
    public string BackendHostDescription => "Run the shared server for your group.";
    public string BackendFolderLabel => "BACKEND FOLDER";
    public string BackendFolderTooltip => "Select the extracted backend folder containing ReVerse.Capture.exe on Windows or ReVerse.Capture on Linux.";
    public string BackendFolderPlaceholder => "Select the extracted backend folder";
    public string BrowseButton => "BROWSE";
    public string BrowseBackendTooltip => "Choose the extracted backend folder.";
    public string ZeroTierAddressLabel => "ZEROTIER ADDRESS";
    public string ZeroTierAddressTooltip => "Use this host computer's managed ZeroTier IPv4 address. Detect can find it from an active ZeroTier adapter.";
    public string ZeroTierAddressPlaceholder => "10.x.x.x";
    public string DetectButton => "DETECT";
    public string DetectAddressTooltip => "Detect the active ZeroTier IPv4 address.";
    public string PlayersLabel => "PLAYERS";
    public string PlayersTooltip => "The exact number of players required before matchmaking starts, from 2 to 10.";
    public string PlayerConnectionUrlLabel => "PLAYER CONNECTION URL";
    public string PlayerConnectionUrlPlaceholder => "Detect ZeroTier to generate the connection URL";
    public string CopyPlayerUrlTooltip => "Copy the Player connection URL to share with players.";
    public string PlayerConnectionUrlDescription => "Give this Player connection URL to every player. Starting the backend also fills the Backend server field in your relay section automatically.";
    public string AdvancedCommandHeader => "ADVANCED COMMAND";
    public string StartBackendButton => "START BACKEND";
    public string StartBackendTooltip => "Launch the backend with this configuration.";
    public string StopBackendButton => "STOP";
    public string StopBackendTooltip => "Stop only the hosted backend process.";
    public string BackendStoppedStatus => "Backend stopped";
    public string BackendRunningStatus => "Backend running; local relay URL filled in automatically";
    public string BackendStoppingStatus => "Stopping backend…";
    public string BackendPorts => "TCP 6080 / UDP 5070";
    public string PleaseWait => "PLEASE WAIT";
    public string SettingsLoadFailureStatus => "Settings could not be loaded";
    public string DetectingZeroTierStatus => "Detecting ZeroTier address…";
    public string ZeroTierDetectedStatus => "ZeroTier address detected";
    public string SelectBackendFolderDialogTitle => "Select the backend folder";
    public string BackendFolderMissingFormat => "{0} was not found in this folder.";
    public string RelayStartErrorSuffix => "Check the server address and make sure ports 5080 and 5081 are free.";
    public string ErrorInvalidUsername => "Enter a username of 1–64 characters without control characters.";
    public string ErrorInvalidSecretKey => "Enter a secret key of 1–1024 characters.";
    public string ErrorInvalidBackendUrl => "Enter a backend URL such as https://server.example.com or http://192.168.1.10:5080, without a path.";
    public string ErrorSavedSettingsEmpty => "Saved settings are empty.";
    public string ErrorRelayAlreadyRunning => "Relay is already running.";
    public string ErrorBackendPointsToRelay => "The backend address points back to this relay. Use the shared server's address and port.";
    public string ErrorAccountSignInFailedFormat => "Account sign-in failed (HTTP {0}). Check that the backend supports relay accounts and has Relay__Enabled=true.";
    public string ErrorBackendEmptyAccountResponse => "Backend returned an empty account response.";
    public string ErrorBackendInvalidAccountResponse => "Backend returned an invalid account response.";
    public string ErrorDetectionTimedOut => "Could not detect the ZeroTier IP address within 5 seconds.";
    public string ErrorIpconfigCouldNotStart => "Could not start ipconfig.";
    public string ErrorIpconfigFailed => "ipconfig failed.";
    public string ErrorIpconfigFailedFormat => "ipconfig failed: {0}";
    public string ErrorNoZeroTierAddress => "No active ZeroTier IPv4 address was found. Connect ZeroTier or enter the address manually.";
    public string ErrorInvalidPlayerCount => "Player count must be between 2 and 10.";
    public string ErrorInvalidZeroTierAddress => "Enter a valid ZeroTier IPv4 address.";
    public string ErrorBackendFolderDoesNotExist => "The selected backend folder does not exist.";
    public string ErrorBackendExecutableMissingFormat => "{0} was not found in the selected backend folder.";
    public string ErrorBackendCommandRequired => "Enter a backend command.";
    public string ErrorBackendAlreadyRunning => "The backend is already running.";
    public string ErrorBackendProcessCouldNotStart => "The backend process could not be started.";
    public string ErrorBackendExecutablePermission => "ReVerse.Capture could not be made executable. Run chmod +x ReVerse.Capture in the backend folder.";
    public string ErrorBackendUrlRequired => "Detect a valid ZeroTier address before starting the backend.";

    public string GetRelayStatusText(RelayStatus status) => status switch
    {
        RelayStatus.SigningIn => StatusSigningIn,
        RelayStatus.RunningWaitingForGame => StatusRunningWaitingForGame,
        RelayStatus.RunningConnectedToBackend => StatusRunningConnectedToBackend,
        RelayStatus.BackendConnectionFailed => StatusBackendConnectionFailed,
        RelayStatus.SessionExpired => StatusSessionExpired,
        RelayStatus.StartFailed => StatusStartFailed,
        RelayStatus.SettingsLoadFailed => SettingsLoadFailureStatus,
        _ => StatusStopped
    };

    public string GetRelayErrorText(RelayErrorCode code, int? statusCode) => code switch
    {
        RelayErrorCode.InvalidUsername => ErrorInvalidUsername,
        RelayErrorCode.InvalidSecretKey => ErrorInvalidSecretKey,
        RelayErrorCode.InvalidBackendUrl => ErrorInvalidBackendUrl,
        RelayErrorCode.SavedSettingsEmpty => ErrorSavedSettingsEmpty,
        RelayErrorCode.RelayAlreadyRunning => ErrorRelayAlreadyRunning,
        RelayErrorCode.BackendPointsToRelay => ErrorBackendPointsToRelay,
        RelayErrorCode.AccountSignInFailed => string.Format(CultureInfo.InvariantCulture, ErrorAccountSignInFailedFormat, statusCode ?? 0),
        RelayErrorCode.BackendEmptyAccountResponse => ErrorBackendEmptyAccountResponse,
        RelayErrorCode.BackendInvalidAccountResponse => ErrorBackendInvalidAccountResponse,
        _ => code.ToString()
    };

    public string GetDesktopErrorText(DesktopErrorCode code, string? detail, string? entryName) => code switch
    {
        DesktopErrorCode.DetectionTimedOut => ErrorDetectionTimedOut,
        DesktopErrorCode.IpconfigCouldNotStart => ErrorIpconfigCouldNotStart,
        DesktopErrorCode.IpconfigFailed => string.IsNullOrWhiteSpace(detail)
            ? ErrorIpconfigFailed
            : string.Format(CultureInfo.InvariantCulture, ErrorIpconfigFailedFormat, detail),
        DesktopErrorCode.NoZeroTierAddress => ErrorNoZeroTierAddress,
        DesktopErrorCode.InvalidPlayerCount => ErrorInvalidPlayerCount,
        DesktopErrorCode.InvalidZeroTierAddress => ErrorInvalidZeroTierAddress,
        DesktopErrorCode.BackendFolderDoesNotExist => ErrorBackendFolderDoesNotExist,
        DesktopErrorCode.BackendExecutableMissing => string.Format(CultureInfo.InvariantCulture, ErrorBackendExecutableMissingFormat, entryName ?? "ReVerse.Capture"),
        DesktopErrorCode.BackendCommandRequired => ErrorBackendCommandRequired,
        DesktopErrorCode.BackendAlreadyRunning => ErrorBackendAlreadyRunning,
        DesktopErrorCode.BackendProcessCouldNotStart => ErrorBackendProcessCouldNotStart,
        DesktopErrorCode.BackendExecutablePermission => ErrorBackendExecutablePermission,
        DesktopErrorCode.BackendUrlRequired => ErrorBackendUrlRequired,
        _ => code.ToString()
    };

    public string GetBackendFolderMissingText(string entryName) =>
        string.Format(CultureInfo.InvariantCulture, BackendFolderMissingFormat, entryName);

    public string GetRelayStartErrorText(string details) =>
        string.IsNullOrWhiteSpace(details) ? RelayStartErrorSuffix : $"{details} {RelayStartErrorSuffix}";
}

public sealed class ChineseTranslation : ITranslation
{
    public string Code => "zh-CN";
    public string LanguageName => "中文";
    public string WindowTitle => "ReVerse Relay";
    public string BrandPrefix => "RE";
    public string BrandAccent => "VERSE";
    public string BrandSuffix => " RELAY";
    public string Tagline => "社区连接工具";
    public string LanguageSelectorTooltip => "选择界面语言。";
    public string HostBackendButton => "托管后端  ›";
    public string RelayOnlyButton => "‹  仅中继";
    public string HostBackendTooltip => "展开窗口以显示后端托管控件。";
    public string RelayOnlyTooltip => "收起窗口，仅显示中继控件。";
    public string PlayerRelayTitle => "玩家中继";
    public string PlayerRelayDescription => "将此电脑连接到主机。";
    public string UsernameLabel => "用户名";
    public string UsernamePlaceholder => "输入玩家名称";
    public string BackendServerLabel => "后端服务器";
    public string BackendServerPlaceholder => "http://10.x.x.x:6080";
    public string BackendServerTooltip => "此地址由主机提供。向主机索要玩家连接 URL。请勿在此输入 localhost 或 127.0.0.1。";
    public string SteamLaunchOptionLabel => "STEAM 启动选项";
    public string SteamLaunchOptionTooltip => "将此内容粘贴到游戏的 Steam 启动选项中。本地地址是有意设置的：游戏会先连接到此中继。";
    public string CopyButton => "复制";
    public string CopiedButton => "已复制";
    public string CopyLaunchOptionTooltip => "复制 Steam 启动选项。";
    public string StartRelayButton => "启动中继";
    public string StopRelayButton => "停止中继";
    public string StartRelayTooltip => "登录并启动本地中继。";
    public string StopRelayTooltip => "停止本地中继。";
    public string StatusStopped => "已停止";
    public string StatusSigningIn => "正在登录…";
    public string StatusRunningWaitingForGame => "运行中 — 等待游戏";
    public string StatusRunningConnectedToBackend => "运行中 — 已连接到后端";
    public string StatusBackendConnectionFailed => "运行中 — 后端连接失败";
    public string StatusSessionExpired => "会话已过期 — 停止并重新启动服务";
    public string StatusStartFailed => "无法启动中继";
    public string RelayPorts => "端口 5080 / 5081";
    public string BackendHostTitle => "后端主机";
    public string BackendHostDescription => "为你的团队运行共享服务器。";
    public string BackendFolderLabel => "后端文件夹";
    public string BackendFolderTooltip => "选择解压后的后端文件夹。Windows 中应包含 ReVerse.Capture.exe，Linux 中应包含 ReVerse.Capture。";
    public string BackendFolderPlaceholder => "选择解压后的后端文件夹";
    public string BrowseButton => "浏览";
    public string BrowseBackendTooltip => "选择解压后的后端文件夹。";
    public string ZeroTierAddressLabel => "ZEROTIER 地址";
    public string ZeroTierAddressTooltip => "使用这台主机电脑的托管 ZeroTier IPv4 地址。检测功能可从活动的 ZeroTier 适配器中找到它。";
    public string ZeroTierAddressPlaceholder => "10.x.x.x";
    public string DetectButton => "检测";
    public string DetectAddressTooltip => "检测活动的 ZeroTier IPv4 地址。";
    public string PlayersLabel => "玩家";
    public string PlayersTooltip => "开始匹配前所需的准确玩家数量，范围为 2 到 10。";
    public string PlayerConnectionUrlLabel => "玩家连接 URL";
    public string PlayerConnectionUrlPlaceholder => "检测 ZeroTier 以生成连接 URL";
    public string CopyPlayerUrlTooltip => "复制玩家连接 URL 并分享给玩家。";
    public string PlayerConnectionUrlDescription => "将此玩家连接 URL 提供给每位玩家。启动后端后，中继部分的后端服务器字段也会自动填充。";
    public string AdvancedCommandHeader => "高级命令";
    public string StartBackendButton => "启动后端";
    public string StartBackendTooltip => "使用此配置启动后端。";
    public string StopBackendButton => "停止";
    public string StopBackendTooltip => "仅停止托管的后端进程。";
    public string BackendStoppedStatus => "后端已停止";
    public string BackendRunningStatus => "后端运行中；本地中继 URL 已自动填充";
    public string BackendStoppingStatus => "正在停止后端…";
    public string BackendPorts => "TCP 6080 / UDP 5070";
    public string PleaseWait => "请稍候";
    public string SettingsLoadFailureStatus => "无法加载设置";
    public string DetectingZeroTierStatus => "正在检测 ZeroTier 地址…";
    public string ZeroTierDetectedStatus => "已检测到 ZeroTier 地址";
    public string SelectBackendFolderDialogTitle => "选择后端文件夹";
    public string BackendFolderMissingFormat => "在此文件夹中未找到 {0}。";
    public string RelayStartErrorSuffix => "请检查服务器地址，并确保端口 5080 和 5081 空闲。";
    public string ErrorInvalidUsername => "请输入 1–64 个字符的用户名，且不得包含控制字符。";
    public string ErrorInvalidSecretKey => "请输入长度为 1–1024 个字符的密钥。";
    public string ErrorInvalidBackendUrl => "请输入后端 URL，例如 https://server.example.com 或 http://192.168.1.10:5080，且不要包含路径。";
    public string ErrorSavedSettingsEmpty => "保存的设置为空。";
    public string ErrorRelayAlreadyRunning => "中继已在运行。";
    public string ErrorBackendPointsToRelay => "后端地址指向此中继。请使用共享服务器的地址和端口。";
    public string ErrorAccountSignInFailedFormat => "账户登录失败（HTTP {0}）。请确认后端支持中继账户，并已设置 Relay__Enabled=true。";
    public string ErrorBackendEmptyAccountResponse => "后端返回了空账户响应。";
    public string ErrorBackendInvalidAccountResponse => "后端返回了无效账户响应。";
    public string ErrorDetectionTimedOut => "在 5 秒内无法检测到 ZeroTier IP 地址。";
    public string ErrorIpconfigCouldNotStart => "无法启动 ipconfig。";
    public string ErrorIpconfigFailed => "ipconfig 失败。";
    public string ErrorIpconfigFailedFormat => "ipconfig 失败：{0}";
    public string ErrorNoZeroTierAddress => "未找到活动的 ZeroTier IPv4 地址。请连接 ZeroTier 或手动输入地址。";
    public string ErrorInvalidPlayerCount => "玩家数量必须在 2 到 10 之间。";
    public string ErrorInvalidZeroTierAddress => "请输入有效的 ZeroTier IPv4 地址。";
    public string ErrorBackendFolderDoesNotExist => "所选后端文件夹不存在。";
    public string ErrorBackendExecutableMissingFormat => "在所选后端文件夹中未找到 {0}。";
    public string ErrorBackendCommandRequired => "请输入后端命令。";
    public string ErrorBackendAlreadyRunning => "后端已在运行。";
    public string ErrorBackendProcessCouldNotStart => "无法启动后端进程。";
    public string ErrorBackendExecutablePermission => "无法使 ReVerse.Capture 可执行。请在后端文件夹中运行 chmod +x ReVerse.Capture。";
    public string ErrorBackendUrlRequired => "启动后端前，请检测有效的 ZeroTier 地址。";

    public string GetRelayStatusText(RelayStatus status) => status switch
    {
        RelayStatus.SigningIn => StatusSigningIn,
        RelayStatus.RunningWaitingForGame => StatusRunningWaitingForGame,
        RelayStatus.RunningConnectedToBackend => StatusRunningConnectedToBackend,
        RelayStatus.BackendConnectionFailed => StatusBackendConnectionFailed,
        RelayStatus.SessionExpired => StatusSessionExpired,
        RelayStatus.StartFailed => StatusStartFailed,
        RelayStatus.SettingsLoadFailed => SettingsLoadFailureStatus,
        _ => StatusStopped
    };

    public string GetRelayErrorText(RelayErrorCode code, int? statusCode) => code switch
    {
        RelayErrorCode.InvalidUsername => ErrorInvalidUsername,
        RelayErrorCode.InvalidSecretKey => ErrorInvalidSecretKey,
        RelayErrorCode.InvalidBackendUrl => ErrorInvalidBackendUrl,
        RelayErrorCode.SavedSettingsEmpty => ErrorSavedSettingsEmpty,
        RelayErrorCode.RelayAlreadyRunning => ErrorRelayAlreadyRunning,
        RelayErrorCode.BackendPointsToRelay => ErrorBackendPointsToRelay,
        RelayErrorCode.AccountSignInFailed => string.Format(CultureInfo.InvariantCulture, ErrorAccountSignInFailedFormat, statusCode ?? 0),
        RelayErrorCode.BackendEmptyAccountResponse => ErrorBackendEmptyAccountResponse,
        RelayErrorCode.BackendInvalidAccountResponse => ErrorBackendInvalidAccountResponse,
        _ => code.ToString()
    };

    public string GetDesktopErrorText(DesktopErrorCode code, string? detail, string? entryName) => code switch
    {
        DesktopErrorCode.DetectionTimedOut => ErrorDetectionTimedOut,
        DesktopErrorCode.IpconfigCouldNotStart => ErrorIpconfigCouldNotStart,
        DesktopErrorCode.IpconfigFailed => string.IsNullOrWhiteSpace(detail)
            ? ErrorIpconfigFailed
            : string.Format(CultureInfo.InvariantCulture, ErrorIpconfigFailedFormat, detail),
        DesktopErrorCode.NoZeroTierAddress => ErrorNoZeroTierAddress,
        DesktopErrorCode.InvalidPlayerCount => ErrorInvalidPlayerCount,
        DesktopErrorCode.InvalidZeroTierAddress => ErrorInvalidZeroTierAddress,
        DesktopErrorCode.BackendFolderDoesNotExist => ErrorBackendFolderDoesNotExist,
        DesktopErrorCode.BackendExecutableMissing => string.Format(CultureInfo.InvariantCulture, ErrorBackendExecutableMissingFormat, entryName ?? "ReVerse.Capture"),
        DesktopErrorCode.BackendCommandRequired => ErrorBackendCommandRequired,
        DesktopErrorCode.BackendAlreadyRunning => ErrorBackendAlreadyRunning,
        DesktopErrorCode.BackendProcessCouldNotStart => ErrorBackendProcessCouldNotStart,
        DesktopErrorCode.BackendExecutablePermission => ErrorBackendExecutablePermission,
        DesktopErrorCode.BackendUrlRequired => ErrorBackendUrlRequired,
        _ => code.ToString()
    };

    public string GetBackendFolderMissingText(string entryName) =>
        string.Format(CultureInfo.InvariantCulture, BackendFolderMissingFormat, entryName);

    public string GetRelayStartErrorText(string details) =>
        string.IsNullOrWhiteSpace(details) ? RelayStartErrorSuffix : $"{details} {RelayStartErrorSuffix}";
}

public sealed class SpanishTranslation : ITranslation
{
    public string Code => "es";
    public string LanguageName => "Español";
    public string WindowTitle => "ReVerse Relay";
    public string BrandPrefix => "RE";
    public string BrandAccent => "VERSE";
    public string BrandSuffix => " RELAY";
    public string Tagline => "UTILIDAD DE CONEXIÓN DE LA COMUNIDAD";
    public string LanguageSelectorTooltip => "Selecciona el idioma de la interfaz.";
    public string HostBackendButton => "ALOJAR BACKEND  ›";
    public string RelayOnlyButton => "‹  SOLO RELAY";
    public string HostBackendTooltip => "Amplía la ventana para mostrar los controles del backend.";
    public string RelayOnlyTooltip => "Reduce la ventana para mostrar solo los controles del relay.";
    public string PlayerRelayTitle => "Relay del jugador";
    public string PlayerRelayDescription => "Conecta este equipo al anfitrión.";
    public string UsernameLabel => "NOMBRE DE USUARIO";
    public string UsernamePlaceholder => "Introduce un nombre de jugador";
    public string BackendServerLabel => "SERVIDOR BACKEND";
    public string BackendServerPlaceholder => "http://10.x.x.x:6080";
    public string BackendServerTooltip => "Esta dirección la proporciona el anfitrión. Pídele la URL de conexión del jugador. No introduzcas aquí localhost ni 127.0.0.1.";
    public string SteamLaunchOptionLabel => "OPCIÓN DE LANZAMIENTO DE STEAM";
    public string SteamLaunchOptionTooltip => "Pega esto en las configuracione de lanzamiento del juego en Steam. La dirección local es intencionada: el juego se conecta primero a este relay.";
    public string CopyButton => "COPIAR";
    public string CopiedButton => "COPIADO";
    public string CopyLaunchOptionTooltip => "Opción de lanzamiento de Configuración en Steam";
    public string StartRelayButton => "INICIAR RELAY";
    public string StopRelayButton => "DETENER RELAY";
    public string StartRelayTooltip => "Inicia sesión y arranca el relay local.";
    public string StopRelayTooltip => "Detén el relay local.";
    public string StatusStopped => "Detenido";
    public string StatusSigningIn => "Iniciando sesión…";
    public string StatusRunningWaitingForGame => "En ejecución — esperando al juego";
    public string StatusRunningConnectedToBackend => "En ejecución — conectado al backend";
    public string StatusBackendConnectionFailed => "En ejecución — conexión con el backend fallida";
    public string StatusSessionExpired => "Sesión caducada — detén y vuelve a iniciar el servicio";
    public string StatusStartFailed => "No se pudo iniciar el relay";
    public string RelayPorts => "PUERTOS 5080 / 5081";
    public string BackendHostTitle => "Anfitrión del backend";
    public string BackendHostDescription => "Ejecuta el servidor compartido para tu grupo.";
    public string BackendFolderLabel => "CARPETA DEL BACKEND";
    public string BackendFolderTooltip => "Selecciona la carpeta del backend extraído que contiene ReVerse.Capture.exe en Windows o ReVerse.Capture en Linux.";
    public string BackendFolderPlaceholder => "Selecciona la carpeta del backend extraído";
    public string BrowseButton => "EXAMINAR";
    public string BrowseBackendTooltip => "Elige la carpeta del backend extraído.";
    public string ZeroTierAddressLabel => "DIRECCIÓN ZEROTIER";
    public string ZeroTierAddressTooltip => "Usa la dirección IPv4 de ZeroTier administrada de este equipo anfitrión. Detectar puede encontrarla en un adaptador de ZeroTier activo.";
    public string ZeroTierAddressPlaceholder => "10.x.x.x";
    public string DetectButton => "DETECTAR";
    public string DetectAddressTooltip => "Detecta la dirección IPv4 activa de ZeroTier.";
    public string PlayersLabel => "JUGADORES";
    public string PlayersTooltip => "El número exacto de jugadores necesarios antes de iniciar el emparejamiento, de 2 a 10.";
    public string PlayerConnectionUrlLabel => "URL DE CONEXIÓN DEL JUGADOR";
    public string PlayerConnectionUrlPlaceholder => "Detecta ZeroTier para generar la URL de conexión";
    public string CopyPlayerUrlTooltip => "Copia la URL de conexión del jugador para compartirla.";
    public string PlayerConnectionUrlDescription => "Entrega esta URL de conexión del jugador a cada jugador. Al iniciar el backend, el campo del servidor backend de la sección del relay también se rellenará automáticamente.";
    public string AdvancedCommandHeader => "COMANDO AVANZADO";
    public string StartBackendButton => "INICIAR BACKEND";
    public string StartBackendTooltip => "Inicia el backend con esta configuración.";
    public string StopBackendButton => "DETENER";
    public string StopBackendTooltip => "Detén solo el proceso del backend alojado.";
    public string BackendStoppedStatus => "Backend detenido";
    public string BackendRunningStatus => "Backend en ejecución; la URL del relay local se ha rellenado automáticamente";
    public string BackendStoppingStatus => "Deteniendo backend…";
    public string BackendPorts => "TCP 6080 / UDP 5070";
    public string PleaseWait => "ESPERA";
    public string SettingsLoadFailureStatus => "No se pudieron cargar los ajustes";
    public string DetectingZeroTierStatus => "Detectando la dirección de ZeroTier…";
    public string ZeroTierDetectedStatus => "Dirección de ZeroTier detectada";
    public string SelectBackendFolderDialogTitle => "Selecciona la carpeta del backend";
    public string BackendFolderMissingFormat => "No se encontró {0} en esta carpeta.";
    public string RelayStartErrorSuffix => "Comprueba la dirección del servidor y asegúrate de que los puertos 5080 y 5081 estén libres.";
    public string ErrorInvalidUsername => "Introduce un nombre de usuario de 1–64 caracteres sin caracteres de control.";
    public string ErrorInvalidSecretKey => "Introduce una clave secreta de 1–1024 caracteres.";
    public string ErrorInvalidBackendUrl => "Introduce una URL de backend, como https://server.example.com o http://192.168.1.10:5080, sin una ruta.";
    public string ErrorSavedSettingsEmpty => "Los ajustes guardados están vacíos.";
    public string ErrorRelayAlreadyRunning => "El relay ya está en ejecución.";
    public string ErrorBackendPointsToRelay => "La dirección del backend apunta a este relay. Usa la dirección y el puerto del servidor compartido.";
    public string ErrorAccountSignInFailedFormat => "No se pudo iniciar sesión en la cuenta (HTTP {0}). Comprueba que el backend admita cuentas de relay y tenga Relay__Enabled=true.";
    public string ErrorBackendEmptyAccountResponse => "El backend devolvió una respuesta de cuenta vacía.";
    public string ErrorBackendInvalidAccountResponse => "El backend devolvió una respuesta de cuenta no válida.";
    public string ErrorDetectionTimedOut => "No se pudo detectar la dirección IP de ZeroTier en 5 segundos.";
    public string ErrorIpconfigCouldNotStart => "No se pudo iniciar ipconfig.";
    public string ErrorIpconfigFailed => "ipconfig falló.";
    public string ErrorIpconfigFailedFormat => "ipconfig falló: {0}";
    public string ErrorNoZeroTierAddress => "No se encontró una dirección IPv4 activa de ZeroTier. Conecta ZeroTier o introduce la dirección manualmente.";
    public string ErrorInvalidPlayerCount => "El número de jugadores debe estar entre 2 y 10.";
    public string ErrorInvalidZeroTierAddress => "Introduce una dirección IPv4 válida de ZeroTier.";
    public string ErrorBackendFolderDoesNotExist => "La carpeta del backend seleccionada no existe.";
    public string ErrorBackendExecutableMissingFormat => "No se encontró {0} en la carpeta del backend seleccionada.";
    public string ErrorBackendCommandRequired => "Introduce un comando de backend.";
    public string ErrorBackendAlreadyRunning => "El backend ya está en ejecución.";
    public string ErrorBackendProcessCouldNotStart => "No se pudo iniciar el proceso del backend.";
    public string ErrorBackendExecutablePermission => "No se pudo hacer ejecutable ReVerse.Capture. Ejecuta chmod +x ReVerse.Capture en la carpeta del backend.";
    public string ErrorBackendUrlRequired => "Detecta una dirección válida de ZeroTier antes de iniciar el backend.";

    public string GetRelayStatusText(RelayStatus status) => status switch
    {
        RelayStatus.SigningIn => StatusSigningIn,
        RelayStatus.RunningWaitingForGame => StatusRunningWaitingForGame,
        RelayStatus.RunningConnectedToBackend => StatusRunningConnectedToBackend,
        RelayStatus.BackendConnectionFailed => StatusBackendConnectionFailed,
        RelayStatus.SessionExpired => StatusSessionExpired,
        RelayStatus.StartFailed => StatusStartFailed,
        RelayStatus.SettingsLoadFailed => SettingsLoadFailureStatus,
        _ => StatusStopped
    };

    public string GetRelayErrorText(RelayErrorCode code, int? statusCode) => code switch
    {
        RelayErrorCode.InvalidUsername => ErrorInvalidUsername,
        RelayErrorCode.InvalidSecretKey => ErrorInvalidSecretKey,
        RelayErrorCode.InvalidBackendUrl => ErrorInvalidBackendUrl,
        RelayErrorCode.SavedSettingsEmpty => ErrorSavedSettingsEmpty,
        RelayErrorCode.RelayAlreadyRunning => ErrorRelayAlreadyRunning,
        RelayErrorCode.BackendPointsToRelay => ErrorBackendPointsToRelay,
        RelayErrorCode.AccountSignInFailed => string.Format(CultureInfo.InvariantCulture, ErrorAccountSignInFailedFormat, statusCode ?? 0),
        RelayErrorCode.BackendEmptyAccountResponse => ErrorBackendEmptyAccountResponse,
        RelayErrorCode.BackendInvalidAccountResponse => ErrorBackendInvalidAccountResponse,
        _ => code.ToString()
    };

    public string GetDesktopErrorText(DesktopErrorCode code, string? detail, string? entryName) => code switch
    {
        DesktopErrorCode.DetectionTimedOut => ErrorDetectionTimedOut,
        DesktopErrorCode.IpconfigCouldNotStart => ErrorIpconfigCouldNotStart,
        DesktopErrorCode.IpconfigFailed => string.IsNullOrWhiteSpace(detail)
            ? ErrorIpconfigFailed
            : string.Format(CultureInfo.InvariantCulture, ErrorIpconfigFailedFormat, detail),
        DesktopErrorCode.NoZeroTierAddress => ErrorNoZeroTierAddress,
        DesktopErrorCode.InvalidPlayerCount => ErrorInvalidPlayerCount,
        DesktopErrorCode.InvalidZeroTierAddress => ErrorInvalidZeroTierAddress,
        DesktopErrorCode.BackendFolderDoesNotExist => ErrorBackendFolderDoesNotExist,
        DesktopErrorCode.BackendExecutableMissing => string.Format(CultureInfo.InvariantCulture, ErrorBackendExecutableMissingFormat, entryName ?? "ReVerse.Capture"),
        DesktopErrorCode.BackendCommandRequired => ErrorBackendCommandRequired,
        DesktopErrorCode.BackendAlreadyRunning => ErrorBackendAlreadyRunning,
        DesktopErrorCode.BackendProcessCouldNotStart => ErrorBackendProcessCouldNotStart,
        DesktopErrorCode.BackendExecutablePermission => ErrorBackendExecutablePermission,
        DesktopErrorCode.BackendUrlRequired => ErrorBackendUrlRequired,
        _ => code.ToString()
    };

    public string GetBackendFolderMissingText(string entryName) =>
        string.Format(CultureInfo.InvariantCulture, BackendFolderMissingFormat, entryName);

    public string GetRelayStartErrorText(string details) =>
        string.IsNullOrWhiteSpace(details) ? RelayStartErrorSuffix : $"{details} {RelayStartErrorSuffix}";
}

public sealed class UkrainianTranslation : ITranslation
{
    public string Code => "uk";
    public string LanguageName => "Українська";
    public string WindowTitle => "ReVerse Relay";
    public string BrandPrefix => "RE";
    public string BrandAccent => "VERSE";
    public string BrandSuffix => " RELAY";
    public string Tagline => "УТИЛІТА ДЛЯ З’ЄДНАННЯ СПІЛЬНОТИ";
    public string LanguageSelectorTooltip => "Виберіть мову інтерфейсу.";
    public string HostBackendButton => "ХОСТ БЕКЕНДУ  ›";
    public string RelayOnlyButton => "‹  ЛИШЕ РЕЛЕ";
    public string HostBackendTooltip => "Розгорнути вікно, щоб показати елементи керування бекендом.";
    public string RelayOnlyTooltip => "Згорнути вікно, щоб залишити лише елементи керування ретранслятором.";
    public string PlayerRelayTitle => "Ретранслятор гравця";
    public string PlayerRelayDescription => "Підключіть цей комп’ютер до хоста.";
    public string UsernameLabel => "ІМ’Я КОРИСТУВАЧА";
    public string UsernamePlaceholder => "Введіть ім’я гравця";
    public string BackendServerLabel => "СЕРВЕР БЕКЕНДУ";
    public string BackendServerPlaceholder => "http://10.x.x.x:6080";
    public string BackendServerTooltip => "Цю адресу надає хост. Попросіть URL підключення гравця. Не вводьте тут localhost або 127.0.0.1.";
    public string SteamLaunchOptionLabel => "ПАРАМЕТР ЗАПУСКУ STEAM";
    public string SteamLaunchOptionTooltip => "Вставте це в параметри запуску гри в Steam. Локальна адреса вказана навмисно: гра спочатку підключається до цього ретранслятора.";
    public string CopyButton => "КОПІЮВАТИ";
    public string CopiedButton => "СКОПІЙОВАНО";
    public string CopyLaunchOptionTooltip => "Скопіювати параметр запуску Steam.";
    public string StartRelayButton => "ЗАПУСТИТИ РЕТРАНСЛЯТОР";
    public string StopRelayButton => "ЗУПИНИТИ РЕТРАНСЛЯТОР";
    public string StartRelayTooltip => "Увійти та запустити локальний ретранслятор.";
    public string StopRelayTooltip => "Зупинити локальний ретранслятор.";
    public string StatusStopped => "Зупинено";
    public string StatusSigningIn => "Виконується вхід…";
    public string StatusRunningWaitingForGame => "Працює — очікування гри";
    public string StatusRunningConnectedToBackend => "Працює — підключено до бекенду";
    public string StatusBackendConnectionFailed => "Працює — не вдалося підключитися до бекенду";
    public string StatusSessionExpired => "Сеанс завершено — зупиніть і запустіть службу знову";
    public string StatusStartFailed => "Не вдалося запустити ретранслятор";
    public string RelayPorts => "ПОРТИ 5080 / 5081";
    public string BackendHostTitle => "Хост бекенду";
    public string BackendHostDescription => "Запустіть спільний сервер для своєї групи.";
    public string BackendFolderLabel => "ПАПКА БЕКЕНДУ";
    public string BackendFolderTooltip => "Виберіть розпаковану папку бекенду, що містить ReVerse.Capture.exe у Windows або ReVerse.Capture у Linux.";
    public string BackendFolderPlaceholder => "Виберіть розпаковану папку бекенду";
    public string BrowseButton => "ВИБРАТИ";
    public string BrowseBackendTooltip => "Виберіть розпаковану папку бекенду.";
    public string ZeroTierAddressLabel => "АДРЕСА ZEROTIER";
    public string ZeroTierAddressTooltip => "Використовуйте керовану IPv4-адресу ZeroTier цього комп’ютера-хоста. Виявлення може знайти її через активний адаптер ZeroTier.";
    public string ZeroTierAddressPlaceholder => "10.x.x.x";
    public string DetectButton => "ВИЯВИТИ";
    public string DetectAddressTooltip => "Виявити активну IPv4-адресу ZeroTier.";
    public string PlayersLabel => "ГРАВЦІ";
    public string PlayersTooltip => "Точна кількість гравців, потрібна до початку пошуку матчу, — від 2 до 10.";
    public string PlayerConnectionUrlLabel => "URL ПІДКЛЮЧЕННЯ ГРАВЦЯ";
    public string PlayerConnectionUrlPlaceholder => "Виявіть ZeroTier, щоб створити URL підключення";
    public string CopyPlayerUrlTooltip => "Скопіювати URL підключення гравця, щоб поділитися ним з гравцями.";
    public string PlayerConnectionUrlDescription => "Передайте цей URL підключення гравця кожному гравцеві. Після запуску бекенду поле сервера бекенду в секції ретранслятора заповниться автоматично.";
    public string AdvancedCommandHeader => "РОЗШИРЕНА КОМАНДА";
    public string StartBackendButton => "ЗАПУСТИТИ БЕКЕНД";
    public string StartBackendTooltip => "Запустити бекенд із цією конфігурацією.";
    public string StopBackendButton => "ЗУПИНИТИ";
    public string StopBackendTooltip => "Зупинити лише процес запущеного бекенду.";
    public string BackendStoppedStatus => "Бекенд зупинено";
    public string BackendRunningStatus => "Бекенд працює; локальний URL ретранслятора заповнено автоматично";
    public string BackendStoppingStatus => "Зупинення бекенду…";
    public string BackendPorts => "TCP 6080 / UDP 5070";
    public string PleaseWait => "ЗАЧЕКАЙТЕ";
    public string SettingsLoadFailureStatus => "Не вдалося завантажити налаштування";
    public string DetectingZeroTierStatus => "Виявлення адреси ZeroTier…";
    public string ZeroTierDetectedStatus => "Адресу ZeroTier виявлено";
    public string SelectBackendFolderDialogTitle => "Виберіть папку бекенду";
    public string BackendFolderMissingFormat => "У цій папці не знайдено {0}.";
    public string RelayStartErrorSuffix => "Перевірте адресу сервера та переконайтеся, що порти 5080 і 5081 вільні.";
    public string ErrorInvalidUsername => "Введіть ім’я користувача довжиною від 1 до 64 символів без керівних символів.";
    public string ErrorInvalidSecretKey => "Введіть секретний ключ довжиною від 1 до 1024 символів.";
    public string ErrorInvalidBackendUrl => "Введіть URL бекенду, наприклад https://server.example.com або http://192.168.1.10:5080, без шляху.";
    public string ErrorSavedSettingsEmpty => "Збережені налаштування порожні.";
    public string ErrorRelayAlreadyRunning => "Ретранслятор уже працює.";
    public string ErrorBackendPointsToRelay => "Адреса бекенду вказує назад на цей ретранслятор. Використайте адресу та порт спільного сервера.";
    public string ErrorAccountSignInFailedFormat => "Не вдалося увійти до облікового запису (HTTP {0}). Переконайтеся, що бекенд підтримує облікові записи ретранслятора та має Relay__Enabled=true.";
    public string ErrorBackendEmptyAccountResponse => "Бекенд повернув порожню відповідь облікового запису.";
    public string ErrorBackendInvalidAccountResponse => "Бекенд повернув недійсну відповідь облікового запису.";
    public string ErrorDetectionTimedOut => "Не вдалося виявити IP-адресу ZeroTier протягом 5 секунд.";
    public string ErrorIpconfigCouldNotStart => "Не вдалося запустити ipconfig.";
    public string ErrorIpconfigFailed => "Помилка ipconfig.";
    public string ErrorIpconfigFailedFormat => "Помилка ipconfig: {0}";
    public string ErrorNoZeroTierAddress => "Активну IPv4-адресу ZeroTier не знайдено. Підключіть ZeroTier або введіть адресу вручну.";
    public string ErrorInvalidPlayerCount => "Кількість гравців має бути від 2 до 10.";
    public string ErrorInvalidZeroTierAddress => "Введіть дійсну IPv4-адресу ZeroTier.";
    public string ErrorBackendFolderDoesNotExist => "Вибраної папки бекенду не існує.";
    public string ErrorBackendExecutableMissingFormat => "У вибраній папці бекенду не знайдено {0}.";
    public string ErrorBackendCommandRequired => "Введіть команду бекенду.";
    public string ErrorBackendAlreadyRunning => "Бекенд уже працює.";
    public string ErrorBackendProcessCouldNotStart => "Не вдалося запустити процес бекенду.";
    public string ErrorBackendExecutablePermission => "Не вдалося зробити ReVerse.Capture виконуваним. Виконайте chmod +x ReVerse.Capture у папці бекенду.";
    public string ErrorBackendUrlRequired => "Виявіть дійсну адресу ZeroTier перед запуском бекенду.";

    public string GetRelayStatusText(RelayStatus status) => status switch
    {
        RelayStatus.SigningIn => StatusSigningIn,
        RelayStatus.RunningWaitingForGame => StatusRunningWaitingForGame,
        RelayStatus.RunningConnectedToBackend => StatusRunningConnectedToBackend,
        RelayStatus.BackendConnectionFailed => StatusBackendConnectionFailed,
        RelayStatus.SessionExpired => StatusSessionExpired,
        RelayStatus.StartFailed => StatusStartFailed,
        RelayStatus.SettingsLoadFailed => SettingsLoadFailureStatus,
        _ => StatusStopped
    };

    public string GetRelayErrorText(RelayErrorCode code, int? statusCode) => code switch
    {
        RelayErrorCode.InvalidUsername => ErrorInvalidUsername,
        RelayErrorCode.InvalidSecretKey => ErrorInvalidSecretKey,
        RelayErrorCode.InvalidBackendUrl => ErrorInvalidBackendUrl,
        RelayErrorCode.SavedSettingsEmpty => ErrorSavedSettingsEmpty,
        RelayErrorCode.RelayAlreadyRunning => ErrorRelayAlreadyRunning,
        RelayErrorCode.BackendPointsToRelay => ErrorBackendPointsToRelay,
        RelayErrorCode.AccountSignInFailed => string.Format(CultureInfo.InvariantCulture, ErrorAccountSignInFailedFormat, statusCode ?? 0),
        RelayErrorCode.BackendEmptyAccountResponse => ErrorBackendEmptyAccountResponse,
        RelayErrorCode.BackendInvalidAccountResponse => ErrorBackendInvalidAccountResponse,
        _ => code.ToString()
    };

    public string GetDesktopErrorText(DesktopErrorCode code, string? detail, string? entryName) => code switch
    {
        DesktopErrorCode.DetectionTimedOut => ErrorDetectionTimedOut,
        DesktopErrorCode.IpconfigCouldNotStart => ErrorIpconfigCouldNotStart,
        DesktopErrorCode.IpconfigFailed => string.IsNullOrWhiteSpace(detail)
            ? ErrorIpconfigFailed
            : string.Format(CultureInfo.InvariantCulture, ErrorIpconfigFailedFormat, detail),
        DesktopErrorCode.NoZeroTierAddress => ErrorNoZeroTierAddress,
        DesktopErrorCode.InvalidPlayerCount => ErrorInvalidPlayerCount,
        DesktopErrorCode.InvalidZeroTierAddress => ErrorInvalidZeroTierAddress,
        DesktopErrorCode.BackendFolderDoesNotExist => ErrorBackendFolderDoesNotExist,
        DesktopErrorCode.BackendExecutableMissing => string.Format(CultureInfo.InvariantCulture, ErrorBackendExecutableMissingFormat, entryName ?? "ReVerse.Capture"),
        DesktopErrorCode.BackendCommandRequired => ErrorBackendCommandRequired,
        DesktopErrorCode.BackendAlreadyRunning => ErrorBackendAlreadyRunning,
        DesktopErrorCode.BackendProcessCouldNotStart => ErrorBackendProcessCouldNotStart,
        DesktopErrorCode.BackendExecutablePermission => ErrorBackendExecutablePermission,
        DesktopErrorCode.BackendUrlRequired => ErrorBackendUrlRequired,
        _ => code.ToString()
    };

    public string GetBackendFolderMissingText(string entryName) =>
        string.Format(CultureInfo.InvariantCulture, BackendFolderMissingFormat, entryName);

    public string GetRelayStartErrorText(string details) =>
        string.IsNullOrWhiteSpace(details) ? RelayStartErrorSuffix : $"{details} {RelayStartErrorSuffix}";
}
