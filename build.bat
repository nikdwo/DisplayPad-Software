@echo off
setlocal EnableExtensions EnableDelayedExpansion
pushd "%~dp0"

set "OUTPUT_DIR=%~dp0build"
set "HOST_EXE=%OUTPUT_DIR%\DisplayPad.Host.exe"
set "AGENT_EXE=%OUTPUT_DIR%\DisplayPad.Agent.exe"
set "CERT_THUMBPRINT=403E60809E646CC7524DE3429ED34C059A9700F1"
set "TIMESTAMP_URL=http://timestamp.digicert.com"

where dotnet.exe >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Das .NET 8 SDK wurde nicht gefunden.
    popd
    exit /b 1
)

echo [1/4] Alte Build-Ausgabe entfernen...
if exist "%OUTPUT_DIR%" rmdir /s /q "%OUTPUT_DIR%"
if exist "%OUTPUT_DIR%" (
    echo [ERROR] Der Ausgabeordner konnte nicht geleert werden: %OUTPUT_DIR%
    popd
    exit /b 1
)
mkdir "%OUTPUT_DIR%"
if errorlevel 1 (
    echo [ERROR] Der Ausgabeordner konnte nicht erstellt werden: %OUTPUT_DIR%
    popd
    exit /b 1
)

echo [2/4] Host und Agent als portable Einzeldateien bauen...
dotnet publish "src\DisplayPad.Host\DisplayPad.Host.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "%OUTPUT_DIR%" --nologo
if errorlevel 1 (
    echo [ERROR] Der Host-Build ist fehlgeschlagen.
    popd
    exit /b 1
)

dotnet publish "src\DisplayPad.Agent\DisplayPad.Agent.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "%OUTPUT_DIR%" --nologo
if errorlevel 1 (
    echo [ERROR] Der Agent-Build ist fehlgeschlagen.
    popd
    exit /b 1
)

if not exist "%HOST_EXE%" (
    echo [ERROR] Erwartete Datei fehlt: %HOST_EXE%
    popd
    exit /b 1
)
if not exist "%AGENT_EXE%" (
    echo [ERROR] Erwartete Datei fehlt: %AGENT_EXE%
    popd
    exit /b 1
)

if /i "%SKIP_SIGNING%"=="1" (
    echo [3/4] Signieren wurde explizit uebersprungen.
) else (
    echo [3/4] Host und Agent signieren...
    where signtool.exe >nul 2>&1
    if errorlevel 1 if not defined VSCMD_VER (
        set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
        if exist "!VSWHERE!" for /f "usebackq tokens=*" %%i in (`"!VSWHERE!" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do call "%%i\VC\Auxiliary\Build\vcvars64.bat"
    )
    where signtool.exe >nul 2>&1
    if errorlevel 1 (
        echo [ERROR] Windows SDK SignTool wurde nicht gefunden.
        popd
        exit /b 1
    )

    signtool.exe sign /sha1 %CERT_THUMBPRINT% /s My /fd SHA256 /tr %TIMESTAMP_URL% /td SHA256 "%HOST_EXE%" "%AGENT_EXE%"
    if errorlevel 1 (
        echo [ERROR] Signieren fehlgeschlagen.
        popd
        exit /b 1
    )

    signtool.exe verify /pa "%HOST_EXE%" >nul
    set "HOST_VERIFY=!errorlevel!"
    signtool.exe verify /pa "%AGENT_EXE%" >nul
    set "AGENT_VERIFY=!errorlevel!"
    if not "!HOST_VERIFY!"=="0" echo [WARNUNG] Host ist signiert, aber die lokale Vertrauenskette ist nicht gueltig.
    if not "!AGENT_VERIFY!"=="0" echo [WARNUNG] Agent ist signiert, aber die lokale Vertrauenskette ist nicht gueltig.
)

echo [4/4] Build-Ausgabe pruefen...
for %%F in ("%HOST_EXE%" "%AGENT_EXE%") do if %%~zF LEQ 0 (
    echo [ERROR] Build-Datei ist leer: %%~fF
    popd
    exit /b 1
)

echo.
echo [OK] DisplayPad wurde gebaut.
echo [OK] Host:  %HOST_EXE%
echo [OK] Agent: %AGENT_EXE%
popd
exit /b 0
