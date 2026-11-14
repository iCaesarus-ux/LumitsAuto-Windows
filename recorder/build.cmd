@echo off
rem Builds ClickRecorder.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No SDK or Visual Studio needed.
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find csc.exe from .NET Framework 4.
  exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /out:ClickRecorder.exe ^
  /win32icon:..\assets\app.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  src\*.cs
if errorlevel 1 exit /b 1
echo Built ClickRecorder.exe
