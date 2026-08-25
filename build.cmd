@echo off
rem ---------------------------------------------------------------------------
rem  TapRecorder build helper.
rem
rem    build            build Release
rem    build run        build Release and start the app
rem    build debug      build Debug and start the app
rem    build publish    self-contained build into dist\ (no .NET needed to run)
rem    build stop       stop a running instance
rem
rem  Messages are English on purpose: a .cmd file with non-ASCII text is at the
rem  mercy of whatever code page the console happens to be in, and a build
rem  script that garbles its own output is worse than one that speaks English.
rem ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0"

set MODE=%~1
if "%MODE%"=="" set MODE=build

set APP=src\TapRecorder.App\TapRecorder.App.csproj

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: dotnet was not found. Install the .NET 10 SDK first.
    exit /b 1
)

if /i "%MODE%"=="stop" goto :stop
if /i "%MODE%"=="publish" goto :publish
if /i "%MODE%"=="debug" goto :debug
if /i "%MODE%"=="run" goto :run
if /i "%MODE%"=="build" goto :build

echo ERROR: unknown mode "%MODE%".
echo Use: build ^| run ^| debug ^| publish ^| stop
exit /b 1

rem ---------------------------------------------------------------------------
:stop
rem The app lives in the tray, so a previous instance is easy to forget about
rem and will hold the output files locked. Every mode stops it first.
taskkill /im TapRecorder.App.exe /f >nul 2>nul
if errorlevel 1 (
    echo Nothing was running.
) else (
    echo Stopped a running instance.
)
if /i "%~1"=="stop" exit /b 0
rem Give Windows a moment to release the file handles.
ping -n 2 127.0.0.1 >nul
exit /b 0

rem ---------------------------------------------------------------------------
:build
call :stop
echo Building Release...
dotnet build -c Release
if errorlevel 1 exit /b 1
echo.
echo Done: src\TapRecorder.App\bin\Release\net10.0-windows\TapRecorder.App.exe
exit /b 0

rem ---------------------------------------------------------------------------
:run
call :stop
echo Building Release...
dotnet build -c Release
if errorlevel 1 exit /b 1
echo Starting...
start "" "src\TapRecorder.App\bin\Release\net10.0-windows\TapRecorder.App.exe"
echo The app is in the tray. Press Ctrl+Alt+D to dictate.
exit /b 0

rem ---------------------------------------------------------------------------
:debug
call :stop
echo Building Debug...
dotnet build
if errorlevel 1 exit /b 1
echo Starting...
start "" "src\TapRecorder.App\bin\Debug\net10.0-windows\TapRecorder.App.exe"
exit /b 0

rem ---------------------------------------------------------------------------
:publish
call :stop
echo Publishing self-contained build into dist\ ...
rem Not PublishSingleFile: the native whisper libraries live in runtimes\
rem subfolders and are loaded by path at run time, so a folder is the shape
rem that actually works. Self-contained means the machine needs no .NET.
dotnet publish "%APP%" -c Release -r win-x64 --self-contained true -o dist
if errorlevel 1 exit /b 1
echo.
echo Done: dist\TapRecorder.App.exe
echo Models are not included; put them in %%APPDATA%%\TapRecorder\models
echo or create an empty file named TapRecorder.portable next to the exe
echo and use a Data folder beside it instead.
exit /b 0
