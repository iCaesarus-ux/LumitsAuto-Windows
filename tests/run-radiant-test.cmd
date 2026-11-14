@echo off
rem Tests catching radiants in the real ClickEngine, in a pretend game window off-screen. The real pointer doesn't move.
rem   run-radiant-test.cmd radiant.jpg normal-fight.png caught.png fainted.png
rem The pictures are 1920 x 1025 screens of the game. Nothing is written to your own settings, counts or catch log.
setlocal
cd /d "%~dp0.."

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
for %%I in ("%CSC%") do set "FW=%%~dpI"
set "WM=%WINDIR%\System32\WinMetadata"
set "OUT=%TEMP%\RadiantCatchTest.exe"

"%CSC%" /nologo /target:exe /optimize+ /nowarn:1701,1702 /main:AutoClicker.RadiantCatchTest /out:"%OUT%" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:"%FW%System.Runtime.dll" ^
  /r:"%WM%\Windows.Foundation.winmd" /r:"%WM%\Windows.Media.winmd" /r:"%WM%\Windows.Graphics.winmd" ^
  /r:"%WM%\Windows.Globalization.winmd" /r:"%WM%\Windows.Storage.winmd" ^
  src\*.cs tests\RadiantCatchTest.cs
if errorlevel 1 exit /b 1
"%OUT%" %*
set "CODE=%ERRORLEVEL%"
del "%OUT%" 2>nul
exit /b %CODE%
