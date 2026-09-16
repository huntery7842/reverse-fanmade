[CmdletBinding()]
param(
    [ValidatePattern('^backend(?:-[A-Za-z0-9-]+)?$')][string]$OutputName = 'backend'
)

$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$artifactsRoot = [IO.Path]::GetFullPath([IO.Path]::Combine($repositoryRoot, "artifacts"))
$outputDirectory = [IO.Path]::GetFullPath([IO.Path]::Combine($artifactsRoot, $OutputName))
$stagingDirectory = [IO.Path]::GetFullPath([IO.Path]::Combine($artifactsRoot, "backend-publish-$([Guid]::NewGuid().ToString('N'))"))

if ([IO.Path]::GetDirectoryName($outputDirectory) -ne $artifactsRoot) {
    throw "Invalid backend output directory: $outputDirectory"
}

if ([IO.Path]::GetDirectoryName($stagingDirectory) -ne $artifactsRoot) {
    throw "Invalid backend staging directory: $stagingDirectory"
}

try {
    dotnet publish "$repositoryRoot\backend\ReVerse.Capture.csproj" `
        --disable-build-servers `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:UseSharedCompilation=false `
        -p:NuGetAudit=false `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $stagingDirectory

    if ($LASTEXITCODE -ne 0) {
        throw "Backend publish failed with exit code $LASTEXITCODE"
    }

    Get-ChildItem -LiteralPath $stagingDirectory -Filter "*.pdb" -File | Remove-Item -Force

    $expectedExecutable = "ReVerse.Capture.exe"
    $publishedFiles = @(Get-ChildItem -LiteralPath $stagingDirectory -File)
    if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne $expectedExecutable -or
        @(Get-ChildItem -LiteralPath $stagingDirectory -Directory).Count -ne 0) {
        throw "Expected one published file named $expectedExecutable, found: $($publishedFiles.Name -join ', ')"
    }

    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    Copy-Item -LiteralPath $publishedFiles[0].FullName -Destination (Join-Path $outputDirectory $expectedExecutable) -Force
    Write-Output (Join-Path $outputDirectory $expectedExecutable)
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
