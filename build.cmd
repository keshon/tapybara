@echo off
rem ---------------------------------------------------------------------------
rem  Tapybara build helper.
rem
rem    build            build Release
rem    build run        build Release and start the app
rem    build debug      build Debug and start the app
rem    build test       run the unit tests
rem    build publish    self-contained build into dist\ (no .NET needed to run)
rem    build stop       stop a running instance
rem
rem  Add cuda to publish (build publish cuda) to include the CUDA backend.
rem  It is 150 MB and needs the CUDA Toolkit on the target machine, so the
rem  default build ships Vulkan and CPU only.
rem
rem  Messages are English on purpose: a .cmd file with non-ASCII text is at the
rem  mercy of whatever code page the console happens to be in, and a build
rem  script that garbles its own output is worse than one that speaks English.
rem ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0"

set MODE=%~1
if "%MODE%"=="" set MODE=build

set APP=src\Tapybara.App\Tapybara.App.csproj
set TESTS=tests\Tapybara.Core.Tests\Tapybara.Core.Tests.csproj
set EXE=src\Tapybara.App\bin\Release\net10.0-windows\win-x64\Tapybara.exe
set DEBUG_EXE=src\Tapybara.App\bin\Debug\net10.0-windows\win-x64\Tapybara.exe

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: dotnet was not found. Install the .NET 10 SDK first.
    exit /b 1
)

if /i "%MODE%"=="stop" goto :stop_command
if /i "%MODE%"=="publish" goto :publish
if /i "%MODE%"=="debug" goto :debug
if /i "%MODE%"=="test" goto :test
if /i "%MODE%"=="run" goto :run
if /i "%MODE%"=="build" goto :build

echo ERROR: unknown mode "%MODE%".
echo Use: build ^| run ^| debug ^| test ^| publish ^| stop
exit /b 1

rem ---------------------------------------------------------------------------
rem  Stop a running instance.
rem
rem  Politely first. The app writes call recordings to disk and finalises the
rem  WAV headers on shutdown; killing it outright leaves an hour-long recording
rem  that no player will open. taskkill without /f posts WM_CLOSE, which the
rem  app handles. Only if that does not work within a few seconds do we force it.
rem ---------------------------------------------------------------------------
:stop_command
call :stop
exit /b 0

:stop
tasklist /fi "imagename eq Tapybara.exe" 2>nul | find /i "Tapybara.exe" >nul
if errorlevel 1 (
    echo Nothing was running.
    exit /b 0
)

echo Asking the running instance to close...
taskkill /im Tapybara.exe >nul 2>nul

rem Give it up to five seconds to finish writing files.
for /l %%i in (1,1,10) do (
    tasklist /fi "imagename eq Tapybara.exe" 2>nul | find /i "Tapybara.exe" >nul
    if errorlevel 1 (
        echo Stopped.
        exit /b 0
    )
    ping -n 1 -w 500 127.0.0.1 >nul
)

echo It did not close in time - forcing.
taskkill /im Tapybara.exe /f >nul 2>nul
ping -n 2 127.0.0.1 >nul
exit /b 0

rem ---------------------------------------------------------------------------
:build
call :stop
echo Building Release...
dotnet build -c Release
if errorlevel 1 exit /b 1
echo.
echo Done: %EXE%
exit /b 0

rem ---------------------------------------------------------------------------
:run
call :stop
echo Building Release...
dotnet build -c Release
if errorlevel 1 exit /b 1
echo Starting...
start "" "%EXE%"
echo The app is in the tray. Press Ctrl+Alt+D to dictate.
exit /b 0

rem ---------------------------------------------------------------------------
:debug
call :stop
echo Building Debug...
dotnet build
if errorlevel 1 exit /b 1
echo Starting...
start "" "%DEBUG_EXE%"
exit /b 0

rem ---------------------------------------------------------------------------
:test
echo Running tests...
dotnet test "%TESTS%" -c Release --nologo
exit /b %errorlevel%

rem ---------------------------------------------------------------------------
:publish
call :stop

rem Clean first. Publishing over an existing folder leaves files from previous
rem builds behind, and a renamed or deleted assembly then ships in the release.
if exist dist (
    echo Cleaning dist\ ...
    rmdir /s /q dist
)

set CUDA=
if /i "%~2"=="cuda" set CUDA=-p:WithCuda=true

echo Publishing self-contained build into dist\ ...
rem Not PublishSingleFile: the native whisper libraries live in runtimes\
rem subfolders and are loaded by path at run time, so a folder is the shape
rem that actually works. Self-contained means the machine needs no .NET.
dotnet publish "%APP%" -c Release -r win-x64 --self-contained true %CUDA% -o dist
if errorlevel 1 exit /b 1
echo.
echo Done: dist\Tapybara.exe
echo Models are not included - the app downloads them from Settings, Models.
echo For portable mode, create an empty file named portable.txt next to
echo the exe and it will use a Data folder beside itself.
exit /b 0
