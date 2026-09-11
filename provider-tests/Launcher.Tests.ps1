
$ErrorActionPreference = 'Stop'
$launcherPath = Join-Path $PSScriptRoot '../backend/Start-FieldTest.ps1'
$launcherOriginalField = [Environment]::GetEnvironmentVariable('Signaling__RegistrationFieldB', 'Process')
$launcherOriginalDirectory = (Get-Location).Path
$launcherOriginalExitCode = $global:LASTEXITCODE
$launcherOriginalNegative = [Environment]::GetEnvironmentVariable('Signaling__NegativeControlReply', 'Process')
$launcherOriginalDiagnostics = [Environment]::GetEnvironmentVariable('Signaling__PeerDiagnostics', 'Process')
$global:fieldTestLauncherMock = @{ Observed = $null; ExitCode = 0; Calls = [System.Collections.Generic.List[string]]::new() }
function dotnet {
    $global:fieldTestLauncherMock.Calls.Add(($args -join ' '))
    $global:fieldTestLauncherMock.Observed = @{
        Arguments = @($args)
        Directory = (Get-Location).Path
        Host = $env:Signaling__PublicHost
        Field = $env:Signaling__RegistrationFieldB
        PeerField = $env:Signaling__RegistrationFieldBIsPeerNumber
        Replies = $env:Signaling__ExperimentalReplies
        TwoPlayers = $env:Matchmaking__Rulesets__match_master
        Udp = $env:Signaling__Port
        Negative = $env:Signaling__NegativeControlReply
        Diagnostics = $env:Signaling__PeerDiagnostics
    }
    $global:LASTEXITCODE = $global:fieldTestLauncherMock.ExitCode
}
function Assert-Launcher([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
try {
    [Environment]::SetEnvironmentVariable('Signaling__RegistrationFieldB', '123', 'Process')
    [Environment]::SetEnvironmentVariable('Signaling__NegativeControlReply', '21', 'Process')
    [Environment]::SetEnvironmentVariable('Signaling__PeerDiagnostics', 'true', 'Process')
    & $launcherPath -PublicHost '127.0.0.1' -WarningAction SilentlyContinue
    $launcherObserved = $global:fieldTestLauncherMock.Observed
    Assert-Launcher ($launcherObserved.Host -eq '127.0.0.1') 'Mock launcher was not invoked'
    Assert-Launcher ([string]::IsNullOrEmpty($launcherObserved.Field)) 'Peer-number mode inherited a fixed field'
    Assert-Launcher ($launcherObserved.PeerField -eq 'true' -and $launcherObserved.Replies -eq 'true') 'Default experiment flags'
    Assert-Launcher ($launcherObserved.TwoPlayers -eq '2' -and $launcherObserved.Udp -eq '5070') 'Field-test defaults'
    Assert-Launcher ($launcherObserved.Diagnostics -eq 'false' -and $env:Signaling__PeerDiagnostics -eq 'true') 'Diagnostics inherited or not restored'
    & $launcherPath -PublicHost '127.0.0.1' -PeerDiagnostics -WarningAction SilentlyContinue
    Assert-Launcher ($global:fieldTestLauncherMock.Observed.Diagnostics -eq 'true') 'Diagnostics opt-in lost'
    $global:fieldTestLauncherMock.Calls.Clear()
    & $launcherPath -PublicHost '127.0.0.1' -Build -PeerDiagnostics -Configuration Release -WarningAction SilentlyContinue
    Assert-Launcher ($global:fieldTestLauncherMock.Calls.Count -eq 2) 'Build launch did not build then run'
    Assert-Launcher ($global:fieldTestLauncherMock.Calls[0] -eq 'build --configuration Release --disable-build-servers -p:UseSharedCompilation=false') 'Build settings incorrect'
    Assert-Launcher ($global:fieldTestLauncherMock.Calls[1] -eq 'run --no-build --no-launch-profile --configuration Release') 'Built configuration not used'
    Assert-Launcher ([string]::IsNullOrEmpty($launcherObserved.Negative) -and $env:Signaling__NegativeControlReply -eq '21') 'Normal launch inherited a negative control or failed to restore it'
    Assert-Launcher (($launcherObserved.Arguments -join ' ') -eq 'run --no-build --no-launch-profile --configuration Debug') 'Launcher unexpectedly builds or restores'
    Assert-Launcher ($env:Signaling__RegistrationFieldB -eq '123' -and (Get-Location).Path -eq $launcherOriginalDirectory) 'Launcher did not restore environment/location'
    & $launcherPath -PublicHost '127.0.0.1' -RegistrationFieldB 0 -ObserveOnly -UdpPort 15070 -Configuration Release -WarningAction SilentlyContinue
    $launcherObserved = $global:fieldTestLauncherMock.Observed
    Assert-Launcher ($launcherObserved.Field -eq '0' -and $launcherObserved.PeerField -eq 'false') 'Fixed field override'
    Assert-Launcher ($launcherObserved.Replies -eq 'false' -and $launcherObserved.Udp -eq '15070') 'Observe-only overrides'
    foreach ($launcherReply in @(19, 21)) {
        & $launcherPath -PublicHost '127.0.0.1' -NegativeControlReply $launcherReply -WarningAction SilentlyContinue
        Assert-Launcher ($global:fieldTestLauncherMock.Observed.Negative -eq [string]$launcherReply) 'Negative-control selection was lost'
    }
    foreach ($launcherInvalid in @(@{ NegativeControlReply = 20 }, @{ Reply19 = 0 }, @{ Reply21 = 0 }, @{ NegativeControlReply = 19; ObserveOnly = $true })) {
        $global:fieldTestLauncherMock.Observed = $null
        $launcherRejected = $false
        try { & $launcherPath -PublicHost '127.0.0.1' @launcherInvalid -WarningAction SilentlyContinue }
        catch { $launcherRejected = $true }
        Assert-Launcher ($launcherRejected -and $null -eq $global:fieldTestLauncherMock.Observed) 'Invalid test settings launched the backend'
    }
    $global:fieldTestLauncherMock.ExitCode = 7
    $global:fieldTestLauncherMock.Calls.Clear()
    $launcherBuildRejected = $false
    try { & $launcherPath -PublicHost '127.0.0.1' -Build -PeerDiagnostics -WarningAction SilentlyContinue }
    catch { $launcherBuildRejected = $_.Exception.Message -eq 'Backend build failed with code 7. Backend was not started.' }
    Assert-Launcher ($launcherBuildRejected -and $global:fieldTestLauncherMock.Calls.Count -eq 1) 'Failed build started the backend'
    Assert-Launcher ($env:Signaling__PeerDiagnostics -eq 'true' -and (Get-Location).Path -eq $launcherOriginalDirectory) 'Failed build did not restore environment/location'
    $launcherRejected = $false
    try { & $launcherPath -PublicHost '127.0.0.1' -WarningAction SilentlyContinue }
    catch { $launcherRejected = $_.Exception.Message -eq 'Backend exited with code 7.' }
    Assert-Launcher $launcherRejected 'Failed backend exit was hidden'
    Assert-Launcher ($env:Signaling__RegistrationFieldB -eq '123' -and (Get-Location).Path -eq $launcherOriginalDirectory) 'Failed launch did not restore environment/location'
    Write-Output 'PASS Windows launcher defaults, overrides, observing mode, failure and environment/location restoration (dotnet mocked)'
}
finally {
    [Environment]::SetEnvironmentVariable('Signaling__RegistrationFieldB', $launcherOriginalField, 'Process')
    [Environment]::SetEnvironmentVariable('Signaling__NegativeControlReply', $launcherOriginalNegative, 'Process')
    [Environment]::SetEnvironmentVariable('Signaling__PeerDiagnostics', $launcherOriginalDiagnostics, 'Process')
    Remove-Item Function:dotnet
    Remove-Variable fieldTestLauncherMock -Scope Global
    $global:LASTEXITCODE = $launcherOriginalExitCode
}
