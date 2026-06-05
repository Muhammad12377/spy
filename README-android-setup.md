Android SDK setup and build notes
=================================

If you need to build the MAUI Android project locally, follow these steps.

1) Option A — Use the provided PowerShell helper (Windows, run as Administrator)

   Open an elevated PowerShell and run:

   ```powershell
   cd scripts
   .\setup-android-sdk.ps1
   ```

   After the script finishes, restart your terminal/IDE and run `dotnet build`.

2) Option B — Manual install

   - Install the Android SDK (via Visual Studio Mobile workload or command-line tools).
   - Ensure `ANDROID_SDK_ROOT` points to the SDK root (e.g. `C:\Android\sdk`).
   - Ensure `platform-tools` and `cmdline-tools\latest\bin` are on your PATH.
   - Run `sdkmanager --install "platform-tools" "platforms;android-33" "build-tools;33.0.2"`.
   - Run `sdkmanager --licenses` and accept all.

3) Build the project

   From the repository root run:

   ```powershell
   dotnet restore "DeviceTracker\DeviceTracker.csproj"
   dotnet build "DeviceTracker\DeviceTracker.csproj" -f net9.0-android
   ```

   If you cannot set the environment variable globally, you can pass the SDK path to MSBuild:

   ```powershell
   dotnet build "DeviceTracker\DeviceTracker.csproj" -f net9.0-android /p:AndroidSdkDirectory="C:\Android\sdk"
   ```

Notes
-----
- The helper script downloads command-line tools from Google; if the URL in the script becomes outdated, download the latest command-line tools zip from:
  https://developer.android.com/studio#command-tools
- Building Android MAUI apps requires the Android SDK and appropriate build-tools/platforms. If you prefer not to install them locally, you can build on a CI runner with Android support.
