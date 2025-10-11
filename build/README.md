# Build and Packaging

This folder describes how to compile SteamBacklogPicker and produce signed installers for distribution. The pipeline targets Windows 10/11 and produces:

- A self-contained MSIX package for direct sideloading or submission to the Microsoft Store.
- A Squirrel.Windows release feed for delta updates.
- A Winget manifest that references the signed MSIX asset.

## Prerequisites

- Windows 11/Windows Server 2022 build agent with the **Windows 11 SDK** and **MSIX Packaging Tool** installed.
- Visual Studio 2022 with the **.NET desktop development** workload, or the .NET 8 SDK and `msbuild`.
- [WiX Toolset 3.14+](https://wixtoolset.org/) only if generating an alternative MSI installer.
- PowerShell 7 for running the automation scripts.
- Access to an AuthentiCode code-signing certificate in PFX format.

## Build layout

```text
build/
├── README.md                     # This document
├── package-msix.ps1              # End-to-end build and signing script
├── msix/
│   ├── AppxManifest.xml          # MSIX manifest template
│   └── assets/                   # Optional icon resources (placeholders)
├── squirrel/
│   └── squirrel.windows.yml      # Squirrel.Windows configuration
└── winget/
    └── manifest.yaml             # Sample Winget manifest referencing the MSIX
```

## MSIX packaging flow

1. Publish the WPF application:
   ```powershell
   dotnet publish ..\src\Presentation\SteamBacklogPicker.UI\SteamBacklogPicker.UI.csproj `
       -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true
   ```
2. Copy the output into `build\artifacts\msix\SteamBacklogPicker`.
3. Update `build/msix/AppxManifest.xml` with the correct version and certificate subject.
4. Run the packaging script:
   ```powershell
   pwsh .\build\package-msix.ps1 -Configuration Release -Runtime win-x64 `
       -CertificatePath C:\certs\MyCompany.pfx -CertificatePassword (Read-Host -AsSecureString)
   ```
   The script wraps `MakeAppx.exe` and `SignTool.exe` to produce `SteamBacklogPicker.msix` and a corresponding `.appinstaller` file.

## AuthentiCode signing

`package-msix.ps1` uses the following environment variables when the command-line switches are omitted:

- `SBP_CERTIFICATE_PATH` – Full path to the PFX file.
- `SBP_CERTIFICATE_PASSWORD` – Plain-text password (optional, use secrets management in CI).
- `SBP_TIMESTAMP_URL` – RFC 3161 timestamp server (default: `http://timestamp.digicert.com`).

The script signs the published binaries before packaging and signs the final MSIX with `SignTool.exe` to ensure Windows SmartScreen trust.

## Winget integration

The sample `build/winget/manifest.yaml` is a multi-file manifest compressed into a single YAML for convenience. Update the version number and SHA256 hash after each release. Use the [wingetcreate](https://github.com/microsoft/winget-create) tool to submit updates:

```powershell
wingetcreate submit .\build\winget\manifest.yaml --token <GITHUB_TOKEN>
```

## Squirrel updates

`build/squirrel/squirrel.windows.yml` configures the feed URL and installation experience. Run the following after publishing and signing binaries:

```powershell
Squirrel.exe --releasify build\artifacts\squirrel\SteamBacklogPicker.nuspec `
    --releaseDir build\artifacts\squirrel\releases `
    --loadingGif assets\splash.gif
```

Host the `releases` directory on HTTPS storage (Azure Blob Storage, GitHub Releases) and point the application to the feed URL via configuration.

## CI/CD recommendations

- Use GitHub Actions or Azure DevOps with a self-hosted Windows runner that has access to the code-signing certificate.
- Publish artifacts: `SteamBacklogPicker.msix`, `SteamBacklogPicker.msixbundle`, Squirrel `RELEASES` folder, and updated Winget manifest.
- Create release notes that include the SHA256 of the MSIX for Winget validation.

