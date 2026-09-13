$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$artifactsRoot = [IO.Path]::GetFullPath([IO.Path]::Combine($repositoryRoot, "artifacts"))
$outputDirectory = [IO.Path]::GetFullPath([IO.Path]::Combine($artifactsRoot, "relay"))

if ([IO.Path]::GetDirectoryName($outputDirectory) -ne $artifactsRoot) {
    throw "Invalid relay output directory: $outputDirectory"
}

if (Test-Path -LiteralPath $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}

dotnet publish "$repositoryRoot\player-relay\Relay.Avalonia\Relay.Avalonia.csproj" `
    --disable-build-servers `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:UseSharedCompilation=false `
    -p:NuGetAudit=false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $outputDirectory

if ($LASTEXITCODE -ne 0) {
    throw "Relay publish failed with exit code $LASTEXITCODE"
}

Get-ChildItem -LiteralPath $outputDirectory -Filter "*.pdb" -File | Remove-Item -Force

$publishedFiles = @(Get-ChildItem -LiteralPath $outputDirectory -File)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne "Relay.Avalonia.exe") {
    throw "Expected one published file named Relay.Avalonia.exe, found: $($publishedFiles.Name -join ', ')"
}

Write-Output $publishedFiles[0].FullName
