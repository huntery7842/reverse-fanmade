[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublicHost,
    [ValidateRange(1, 65535)][int]$UdpPort = 5070,
    [ValidateRange(1, 65535)][int]$PublicPort = 5070,
    [ValidateRange(2, 10)][int]$Players = 2,
    [string]$HttpUrl = 'http://127.0.0.1:6080',
    [switch]$ObserveOnly,
    [switch]$PeerDiagnostics,
    [switch]$Build,
    [ValidateRange(1, 4294967295)][uint32]$Reply19 = 1,
    [ValidateRange(1, 4294967295)][uint32]$Reply21 = 1,
    [ValidateSet(19, 21)][int]$NegativeControlReply,
    [ValidateRange(-1, 65535)][int]$RegistrationFieldB = -1,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$fieldTestExecutable = Join-Path $PSScriptRoot 'ReVerse.Capture.exe'
$fieldTestPublished = Test-Path -LiteralPath $fieldTestExecutable -PathType Leaf
if ($ObserveOnly -and $PSBoundParameters.ContainsKey('NegativeControlReply')) {
    throw 'NegativeControlReply cannot be combined with ObserveOnly.'
}
$fieldTestVariables = @{
    Relay__Enabled = 'true'
    Steam__Mode = 'fallback'
    Matchmaking__ExperimentalSessionProtocol = 'true'
    Matchmaking__Rulesets__match_master = [string]$Players
    Matchmaking__IgnorePlayerAttributes = 'true'
    ASPNETCORE_URLS = $HttpUrl
    Signaling__Enabled = 'true'
    Signaling__PeerDiagnostics = $PeerDiagnostics.ToString().ToLowerInvariant()
    Signaling__BindAddress = '0.0.0.0'
    Signaling__PublicHost = $PublicHost
    Signaling__Port = [string]$UdpPort
    Signaling__PublicPort = [string]$PublicPort
    Signaling__ExperimentalReplies = (-not $ObserveOnly).ToString().ToLowerInvariant()
    Signaling__Reply19 = [string]$Reply19
    Signaling__Reply21 = [string]$Reply21

    Signaling__NegativeControlReply = $(if ($PSBoundParameters.ContainsKey('NegativeControlReply')) { [string]$NegativeControlReply } else { $null })
    Signaling__RegistrationFieldB = $(if ($RegistrationFieldB -lt 0) { $null } else { [string]$RegistrationFieldB })
    Signaling__RegistrationFieldBIsPeerNumber = ($RegistrationFieldB -lt 0).ToString().ToLowerInvariant()
}
$fieldTestPrevious = @{}
foreach ($fieldTestName in $fieldTestVariables.Keys) {
    $fieldTestPrevious[$fieldTestName] = [Environment]::GetEnvironmentVariable($fieldTestName, 'Process')
}
Push-Location $PSScriptRoot
try {
    if ($Build -and -not $fieldTestPublished) {
        & dotnet build --configuration $Configuration --disable-build-servers '-p:UseSharedCompilation=false'
        if ($LASTEXITCODE -ne 0) { throw "Backend build failed with code $LASTEXITCODE. Backend was not started." }
    }
    foreach ($fieldTestName in $fieldTestVariables.Keys) {
        [Environment]::SetEnvironmentVariable($fieldTestName, $fieldTestVariables[$fieldTestName], 'Process')
    }
    Write-Warning "Field-test provider: UDP $PublicHost`:$PublicPort must reach this PC's UDP $UdpPort. Match size: $Players player(s) for match_master. HTTP/ngrok is separate. No firewall/router settings are changed."
    if (-not $ObserveOnly) {
        Write-Warning "UNVERIFIED reply values: 19=$Reply19, 21=$Reply21, fieldB=$(if ($RegistrationFieldB -lt 0) { 'peer number' } else { $RegistrationFieldB }). These are experimental hypotheses; playable multiplayer is not verified."
    }
    else { Write-Warning 'Observe-only: DTLS and recovered startup are enabled, but the exchange stops at unresolved startup replies.' }
    if ($PSBoundParameters.ContainsKey('NegativeControlReply')) {
        Write-Warning "NEGATIVE CONTROL: reply $NegativeControlReply will be zero for every new association. Startup must fail; this is not a playability test."
    }

    if ($fieldTestPublished) { & $fieldTestExecutable }
    else { & dotnet run --no-build --no-launch-profile --configuration $Configuration }
    if ($LASTEXITCODE -ne 0) { throw "Backend exited with code $LASTEXITCODE." }
}
finally {
    foreach ($fieldTestName in $fieldTestPrevious.Keys) {
        [Environment]::SetEnvironmentVariable($fieldTestName, $fieldTestPrevious[$fieldTestName], 'Process')
    }
    Pop-Location
}
