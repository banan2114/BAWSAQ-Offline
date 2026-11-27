@echo off
setlocal
rem ---------------------------------------------------------------------------
rem Build BawsaqE2aThreadProbe.dll with the in-box .NET Framework compiler.
rem Usage:
rem   build.cmd "C:\Path\To\GTA V Enhanced\ScriptHookVDotNet3.dll"
rem ---------------------------------------------------------------------------
set "SHVDN_DLL=%~1"
if "%SHVDN_DLL%"=="" set "SHVDN_DLL=%SHVDNE_DLL%"
if "%SHVDN_DLL%"=="" (
  echo Usage: build.cmd path\to\ScriptHookVDotNet3.dll
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
  /out:dist\BawsaqE2aThreadProbe.dll ^
  /r:"%SHVDN_DLL%" ^
  /r:System.dll ^
  /r:System.Core.dll ^
  /r:System.Windows.Forms.dll ^
  BawsaqE2aThreadProbe.cs Properties\AssemblyInfo.cs

if errorlevel 1 (
  echo [ERROR] build failed
  exit /b 1
)

echo [OK] dist\BawsaqE2aThreadProbe.dll
exit /b 0
