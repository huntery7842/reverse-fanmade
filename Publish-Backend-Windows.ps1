$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$artifactsRoot = [IO.Path]::GetFullPath([IO.Path]::Combine($repositoryRoot, "artifacts"))
$outputDirectory = [IO.Path]::GetFullPath([IO.Path]::Combine($artifactsRoot, "backend"))

if ([IO.Path]::GetDirectoryName($outputDirectory) -ne $artifactsRoot) {
    throw "Invalid backend output directory: $outputDirectory"
}

if (Test-Path -LiteralPath $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}

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
    -o $outputDirectory

if ($LASTEXITCODE -ne 0) {
    throw "Backend publish failed with exit code $LASTEXITCODE"
}

Get-ChildItem -LiteralPath $outputDirectory -Filter "*.pdb" -File | Remove-Item -Force

$expectedExecutable = "ReVerse.Capture.exe"
$publishedFiles = @(Get-ChildItem -LiteralPath $outputDirectory -File)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne $expectedExecutable -or
    @(Get-ChildItem -LiteralPath $outputDirectory -Directory).Count -ne 0) {
    throw "Expected one published file named $expectedExecutable, found: $($publishedFiles.Name -join ', ')"
}

Write-Output $publishedFiles[0].FullName
