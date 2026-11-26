# BAWSAQ Offline

Offline stock-market simulation for GTA V. The solution is split into two projects:

- **BawsaqOffline.Core** — market logic, trading, persistence and related models. It does not depend on GTA or ScriptHookVDotNet.
- **BawsaqOffline.GTA** — GTA V integration and in-game script. It references the Core project and ScriptHookVDotNet3.

## Requirements

- Windows
- Visual Studio 2022 (or a compatible MSBuild installation)
- .NET Framework 4.8 Developer Pack / targeting pack
- GTA V and a compatible installation of ScriptHookV + ScriptHookVDotNet3

## Configure the ScriptHookVDotNet reference

The GTA project needs the path to `ScriptHookVDotNet3.dll`. This path is machine-specific, so the local configuration file is intentionally excluded from Git.

1. In the repository, open the `BawsaqOffline.GTA` folder.
2. Copy `Local.props.example` and name the copy `Local.props`.
3. Open `Local.props` and change `ScriptHookVDotNet3Path` to the full path of your own `ScriptHookVDotNet3.dll`.
4. Save the file.

Example:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <ScriptHookVDotNet3Path>C:\Path\To\GTA V\ScriptHookVDotNet3.dll</ScriptHookVDotNet3Path>
  </PropertyGroup>
</Project>
```

The GTA project imports `Local.props` when it exists and uses `$(ScriptHookVDotNet3Path)` as the reference path. Do not commit your personal `Local.props`; it is covered by the root `.gitignore`.

## Build

1. Open `BAWSAQ Offline.sln` in Visual Studio.
2. Make sure the solution platform is set to **x64**.
3. Restore any required NuGet packages if Visual Studio prompts you.
4. Build or rebuild the solution.

The solution should build both `BawsaqOffline.Core` and `BawsaqOffline.GTA`. The GTA project's output is written under `BawsaqOffline.GTA\bin\x64\Release\` for the Release|x64 configuration.

## Notes

- `Local.props` is local developer configuration and should remain untracked.
- Build output folders such as `bin` and `obj` are ignored by Git.
- A successful compile confirms the projects build; in-game behavior still needs to be tested with the matching GTA V and ScriptHookVDotNet versions.
