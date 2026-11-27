@echo off
setlocal
rem ---------------------------------------------------------------------------
rem Build BawsaqE1Diagnostics.dll with the in-box .NET Framework compiler.
rem No SDK, no NuGet, no internet required - only the SHVDNE reference DLL
rem that is already installed in the game folder.
rem
rem Usage:
rem   build.cmd "C:\Path\To\GTA V\ScriptHookVDotNet3.dll"
rem   (the DLL must be the one from SHVDNE v1.1.0.6 that the game loads)
rem ---------------------------------------------------------------------------
set "SHVDN_DLL=%~1"
if "%SHVDN_DLL%"=="" set "SHVDN_DLL=%SHVDNE_DLL%"
if "%SHVDN_DLL%"=="" (
  echo Usage: build.cmd path\to\ScriptHookVDotNet3.dll
  echo Example: build.cmd "C:\Games\GTA V\ScriptHookVDotNet3.dll"
  exit /b 2
)
if not exist "%SHVDN_DLL%" (
  echo [ERROR] reference DLL not found: %SHVDN_DLL%
  exit /b 2
)

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] csc.exe not found under %WINDIR%\Microsoft.NET
  exit /b 3
)

if not exist "dist" mkdir dist

"%CSC%" /nologo /target:library /optimize+ ^
  /out:dist\BawsaqE1Diagnostics.dll ^
  /r:"%SHVDN_DLL%" ^
  /r:System.dll ^
  /r:System.Core.dll ^
  /r:System.Windows.Forms.dll ^
  BawsaqE1Diagnostics.cs Properties\AssemblyInfo.cs

if errorlevel 1 (
  echo [ERROR] build failed
  exit /b 1
)

echo [OK] dist\BawsaqE1Diagnostics.dll
echo Install: copy dist\BawsaqE1Diagnostics.dll into "<GTA V>\scripts\"
exit /b 0
