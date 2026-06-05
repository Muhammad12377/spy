<#
Simple Android SDK setup script for Windows (PowerShell).

Usage (run as Administrator):
  .\setup-android-sdk.ps1

This script will:
- Download Android command-line tools (if not present).
- Extract them under the chosen SDK root (default: C:\Android\sdk).
- Add essential paths to the system PATH and set ANDROID_SDK_ROOT.
- Install core packages: platform-tools, platforms;android-33, build-tools;33.0.2

Notes:
- The command-line tools download URL may change; if download fails, visit
  https://developer.android.com/studio#command-tools to get the latest link.
- You may need to run `sdkmanager --licenses` manually to accept licenses.
#>

param(
    [string]$SdkRoot = "C:\Android\sdk",
    [string]$CmdlineToolsUrl = "https://dl.google.com/android/repository/commandlinetools-win-9477386_latest.zip"
)

$ErrorActionPreference = 'Stop'

Write-Host "Android SDK setup script"
Write-Host "SDK root: $SdkRoot"

if (-Not (Test-Path $SdkRoot)) {
    New-Item -ItemType Directory -Path $SdkRoot -Force | Out-Null
}

$cmdlineParent = Join-Path $SdkRoot "cmdline-tools"
$cmdlineDir = Join-Path $cmdlineParent "latest"

if (-Not (Test-Path $cmdlineDir)) {
    $tmp = Join-Path $env:TEMP "cmdline-tools.zip"
    Write-Host "Downloading command-line tools..."
    try {
        Invoke-WebRequest -Uri $CmdlineToolsUrl -OutFile $tmp -UseBasicParsing -ErrorAction Stop
    } catch {
        Write-Warning "Direct download failed, attempting to discover latest command-line tools URL..."
        try {
            $repoXmlUrl = 'https://dl.google.com/android/repository/repository2-1.xml'
            $xmlContent = Invoke-WebRequest -Uri $repoXmlUrl -UseBasicParsing -ErrorAction Stop
            $matches = [regex]::Matches($xmlContent.Content, 'commandlinetools-win[-0-9_]*?_latest\.zip')
            if ($matches.Count -gt 0) {
                $fileName = $matches[0].Value
                $CmdlineToolsUrl = "https://dl.google.com/android/repository/$fileName"
                Write-Host "Discovered: $CmdlineToolsUrl"
                Invoke-WebRequest -Uri $CmdlineToolsUrl -OutFile $tmp -UseBasicParsing -ErrorAction Stop
            } else {
                throw 'Could not locate commandlinetools zip in repository metadata.'
            }
        } catch {
            Write-Error "Failed to download command-line tools: $_"
            throw
        }
    }

    Write-Host "Extracting..."
    Expand-Archive -Path $tmp -DestinationPath $cmdlineParent -Force

    # Normalise layout: many zips extract to cmdline-tools/cmdline-tools/*
    $possible = Get-ChildItem -Path $cmdlineParent -Directory | Where-Object { $_.Name -ne 'latest' } | Select-Object -First 1
    if ($possible) {
        Move-Item -Path $possible.FullName -Destination $cmdlineDir -Force
    }

    Remove-Item $tmp -Force
} else {
    Write-Host "cmdline-tools already installed under $cmdlineDir"
}

Write-Host "Setting ANDROID_SDK_ROOT and updating PATH (machine-level)."
[Environment]::SetEnvironmentVariable("ANDROID_SDK_ROOT", $SdkRoot, [EnvironmentVariableTarget]::Machine)
$path = [Environment]::GetEnvironmentVariable("PATH", [EnvironmentVariableTarget]::Machine)
$newPaths = @(
    (Join-Path $SdkRoot "platform-tools"),
    (Join-Path $cmdlineDir "bin")
)
foreach ($p in $newPaths) {
    if ($path -notlike "*$p*") {
        $path = "$path;$p"
    }
}
[Environment]::SetEnvironmentVariable("PATH", $path, [EnvironmentVariableTarget]::Machine)

Write-Host "Installing platform-tools, build-tools and platforms (may require internet and time)."
$tools = @(
    "platform-tools",
    "platforms;android-33",
    "build-tools;33.0.2",
    "cmdline-tools;latest"
)

$sdkManager = Join-Path $cmdlineDir "bin\sdkmanager.bat"
if (-Not (Test-Path $sdkManager)) {
    $sdkManager = Join-Path $SdkRoot "cmdline-tools\bin\sdkmanager.bat"
}
if (-Not (Test-Path $sdkManager)) {
    Write-Error "sdkmanager not found. Please open a new terminal or reboot, then re-run this script."
    exit 1
}

& $sdkManager --sdk_root="$SdkRoot" --install $tools | Out-Host

Write-Host "If prompted about licenses, run the following command and accept licenses interactively:"
Write-Host "  $sdkManager --sdk_root=\"$SdkRoot\" --licenses"

Write-Host "Android SDK setup script finished. Restart your terminal or IDE to apply environment changes."
