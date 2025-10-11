param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$CertificatePath = $env:SBP_CERTIFICATE_PATH,
    [System.Security.SecureString]$CertificatePassword = $(if ($env:SBP_CERTIFICATE_PASSWORD) { ConvertTo-SecureString $env:SBP_CERTIFICATE_PASSWORD -AsPlainText -Force } else { $null }),
    [string]$TimestampUrl = $(if ($env:SBP_TIMESTAMP_URL) { $env:SBP_TIMESTAMP_URL } else { "http://timestamp.digicert.com" })
)

$ErrorActionPreference = "Stop"

if (-not $CertificatePath) {
    throw "A code-signing certificate must be provided via -CertificatePath or SBP_CERTIFICATE_PATH."
}

$certificatePasswordPlain = $null
if ($CertificatePassword) {
    $certificatePasswordPlain = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR([System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertificatePassword))
}

$root = Resolve-Path -Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src/Presentation/SteamBacklogPicker.UI/SteamBacklogPicker.UI.csproj'
$publishDir = Join-Path $PSScriptRoot "artifacts/publish"
$packageDir = Join-Path $PSScriptRoot "artifacts/msix"
$appFolder = Join-Path $packageDir "SteamBacklogPicker"
$msixPath = Join-Path $packageDir "SteamBacklogPicker.msix"
$bundlePath = Join-Path $packageDir "SteamBacklogPicker.msixbundle"
$appInstallerPath = Join-Path $packageDir "SteamBacklogPicker.appinstaller"

New-Item -ItemType Directory -Force -Path $publishDir, $packageDir | Out-Null

Write-Host "Publishing application..."
dotnet publish $project -c $Configuration -r $Runtime -p:PublishSingleFile=true -p:SelfContained=true -o $publishDir

Write-Host "Copying payload into staging folder..."
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $appFolder
Copy-Item -Recurse -Force $publishDir $appFolder

Write-Host "Signing binaries..."
Get-ChildItem -Path $appFolder -Recurse -Include *.exe, *.dll | ForEach-Object {
    & signtool.exe sign /fd SHA256 /a /f $CertificatePath $(if ($certificatePasswordPlain) { @("/p", $certificatePasswordPlain) } else { @() }) /tr $TimestampUrl /td SHA256 $_.FullName
}

$manifest = Join-Path $PSScriptRoot 'msix/AppxManifest.xml'
if (-not (Test-Path $manifest)) {
    throw "MSIX manifest not found at $manifest"
}

Write-Host "Creating MSIX package..."
& makeappx.exe pack /d $appFolder /p $msixPath /m $manifest | Out-Null

Write-Host "Signing package..."
& signtool.exe sign /fd SHA256 /a /f $CertificatePath $(if ($certificatePasswordPlain) { @("/p", $certificatePasswordPlain) } else { @() }) /tr $TimestampUrl /td SHA256 $msixPath

Write-Host "Creating MSIX bundle..."
& makeappx.exe bundle /d $packageDir /p $bundlePath | Out-Null

$installerTemplate = @"
<AppInstaller Uri="{0}">
  <MainPackage Uri="{0}" Version="{1}" />
</AppInstaller>
"@

$version = (Get-Content $manifest | Select-String -Pattern 'Version="([0-9\.]+)"').Matches[0].Groups[1].Value
$msixUri = "https://example.com/downloads/SteamBacklogPicker_$version.msix"
$installerTemplate -f $msixUri, $version | Set-Content -Path $appInstallerPath

Write-Host "Artifacts created in $packageDir"
