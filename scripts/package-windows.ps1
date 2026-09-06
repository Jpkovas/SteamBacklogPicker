#requires -Version 5.1
<#
.SYNOPSIS
Builds an unsigned, self-contained Windows x64 portable ZIP and its SHA-256 checksum.
.EXAMPLE
./scripts/package-windows.ps1 -Version 1.0.0 -OutputDirectory ./artifacts/windows-1.0.0
.EXAMPLE
./scripts/package-windows.ps1 -Version 1.0.0 -OutputDirectory ./artifacts/windows-1.0.0 -DotnetPath ./artifacts/audit/runtime/dotnet/dotnet.exe
#>
[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$')]
    [string] $Version = '0.0.0',

    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory = './artifacts/windows',

    [ValidateNotNullOrEmpty()]
    [string] $DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Build this WPF package on Windows using the .NET 8 SDK.'
}

$parsedVersion = $null
if (-not [version]::TryParse($Version, [ref] $parsedVersion) -or
    $parsedVersion.Major -gt 65534 -or $parsedVersion.Minor -gt 65534 -or
    $parsedVersion.Build -gt 65534 -or $parsedVersion.Revision -gt 65534) {
    throw 'Use three or four numeric version components, each between 0 and 65534.'
}
$Version = $parsedVersion.ToString()

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'src/Presentation/SteamBacklogPicker.UI/SteamBacklogPicker.UI.csproj'
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)

if (Test-Path -LiteralPath $outputPath) {
    $existingOutput = Get-Item -LiteralPath $outputPath -Force
    if (-not $existingOutput.PSIsContainer -or
        ($existingOutput.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        @(Get-ChildItem -LiteralPath $outputPath -Force | Select-Object -First 1).Count -ne 0) {
        throw "Output must be a new or empty directory, not a link: $outputPath. Existing files are never deleted."
    }
}

if (Test-Path -LiteralPath $DotnetPath -PathType Leaf) {
    $dotnetExecutable = (Get-Item -LiteralPath $DotnetPath).FullName
}
else {
    $dotnetCommand = Get-Command -Name $DotnetPath -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $dotnetExecutable = $dotnetCommand.Source
}

$packageName = "SteamBacklogPicker-$Version-win-x64"
$packagePath = Join-Path $outputPath $packageName
$archivePath = Join-Path $outputPath "$packageName.zip"
$checksumPath = "$archivePath.sha256"
$framework = 'net8.0-windows10.0.18362.0'

[IO.Directory]::CreateDirectory($outputPath) | Out-Null
[IO.Directory]::CreateDirectory($packagePath) | Out-Null

Push-Location -LiteralPath $repositoryRoot
try {
    # Restore the exact RID/self-contained assets before publishing. A prior ordinary build is insufficient.
    & $dotnetExecutable restore $projectPath --runtime win-x64 '-p:SelfContained=true' "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { throw "Windows RID restore failed (exit $LASTEXITCODE)." }

    & $dotnetExecutable publish $projectPath --configuration Release --framework $framework `
        --runtime win-x64 --self-contained true --no-restore --output $packagePath `
        "-p:Version=$Version" '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' `
        '-p:DebugType=None' '-p:DebugSymbols=false'
    if ($LASTEXITCODE -ne 0) { throw "Windows publish failed (exit $LASTEXITCODE)." }
}
finally {
    Pop-Location
}

$executablePath = Join-Path $packagePath 'SteamBacklogPicker.UI.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $packagePath 'coreclr.dll') -PathType Leaf)) {
    throw 'Publish did not produce the application and bundled .NET runtime. No ZIP was generated.'
}

$packageInstructions = @"
Steam Backlog Picker $Version - Windows x64

Extract the whole ZIP, then run SteamBacklogPicker.UI.exe from this folder.
The .NET desktop runtime is included; no separate .NET installation is required.
Steam must be installed for local discovery and steam:// launch/install actions.
Settings and caches are stored in your user profile, not beside this executable.

This portable package is not Authenticode signed and has no Windows auto-update.
Install a newer build by extracting its ZIP into a separate folder.
The SHA-256 file detects changed download bytes; it is not a publisher signature.
"@
[IO.File]::WriteAllText((Join-Path $packagePath 'PORTABLE-README.txt'), $packageInstructions, [Text.UTF8Encoding]::new($false))

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($packagePath, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $true)
$digest = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($checksumPath, "$digest  $packageName.zip`n", [Text.UTF8Encoding]::new($false))

Write-Output "Portable folder: $packagePath"
Write-Output "ZIP: $archivePath"
Write-Output "SHA-256: $checksumPath"
Write-Output 'Unsigned development/distribution artifact; no Authenticode certificate was applied.'
