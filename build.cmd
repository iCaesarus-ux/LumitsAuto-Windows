@echo off
rem Builds AutoClicker.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No SDK or Visual Studio needed.
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find csc.exe from .NET Framework 4.
  exit /b 1
)
for %%I in ("%CSC%") do set "FW=%%~dpI"
rem Windows' own text recognition (for reading lumit names) is described by these metadata files.
set "WM=%WINDIR%\System32\WinMetadata"

"%CSC%" /nologo /target:winexe /optimize+ /out:AutoClicker.exe ^
  /win32icon:assets\app.ico /resource:assets\app.ico,AutoClicker.app.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:"%FW%System.Runtime.dll" ^
  /r:"%~dp0lib\OpenCvSharp.dll" ^
  /r:"%WM%\Windows.Foundation.winmd" /r:"%WM%\Windows.Media.winmd" /r:"%WM%\Windows.Graphics.winmd" ^
  /r:"%WM%\Windows.Globalization.winmd" /r:"%WM%\Windows.Storage.winmd" ^
  src\*.cs
if errorlevel 1 exit /b 1
copy /y "%~dp0lib\OpenCvSharp.dll" . >nul
copy /y "%~dp0lib\OpenCvSharpExtern.dll" . >nul
echo Built AutoClicker.exe
