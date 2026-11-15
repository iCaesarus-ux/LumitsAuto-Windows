@echo off
rem Tests radiant checks in PC 2v2s with the real ClickEngine, in a pretend game window off-screen. The real pointer doesn't move.
rem   run-duo-test.cmd [folder]   (the folder of PC pictures; tests\pictures\pc if left out)
rem Nothing is written to your own settings, counts or catch log. It takes about 10 seconds.
setlocal
cd /d "%~dp0.."

set "PICS=%~1"
if "%PICS%"=="" set "PICS=tests\pictures\pc"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
for %%I in ("%CSC%") do set "FW=%%~dpI"
set "WM=%WINDIR%\System32\WinMetadata"
rem The captcha's code uses OpenCvSharp (lib\, as for build.cmd), so the test runs beside a copy of it.
set "OUTDIR=%TEMP%\LumitsAutoTests"
if not exist "%OUTDIR%" mkdir "%OUTDIR%"
copy /y lib\OpenCvSharp.dll "%OUTDIR%" >nul || exit /b 1
copy /y lib\OpenCvSharpExtern.dll "%OUTDIR%" >nul || exit /b 1
set "OUT=%OUTDIR%\DuoTest.exe"

"%CSC%" /nologo /target:exe /optimize+ /nowarn:1701,1702 /main:AutoClicker.DuoTest /out:"%OUT%" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:"%FW%System.Runtime.dll" /r:lib\OpenCvSharp.dll ^
  /r:"%WM%\Windows.Foundation.winmd" /r:"%WM%\Windows.Media.winmd" /r:"%WM%\Windows.Graphics.winmd" ^
  /r:"%WM%\Windows.Globalization.winmd" /r:"%WM%\Windows.Storage.winmd" ^
  src\*.cs tests\DuoTest.cs
if errorlevel 1 exit /b 1
"%OUT%" "%PICS%"
set "CODE=%ERRORLEVEL%"
del "%OUT%" 2>nul
exit /b %CODE%
