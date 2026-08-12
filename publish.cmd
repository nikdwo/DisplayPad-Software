@echo off
rem Erstellt self-contained Builds (keine .NET-Installation auf dem Zielrechner noetig)

echo === Agent (fuer den Zweitrechner) ===
dotnet publish src\DisplayPad.Agent -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish\Agent
if errorlevel 1 exit /b %errorlevel%

echo === Host (fuer den Rechner mit DisplayPad) ===
dotnet publish src\DisplayPad.Host -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish\Host
if errorlevel 1 exit /b %errorlevel%

if not exist publish\Agent\DisplayPad.Agent.exe exit /b 1
if not exist publish\Host\DisplayPad.Host.exe exit /b 1

echo.
echo Fertig: publish\Agent\DisplayPad.Agent.exe und publish\Host\DisplayPad.Host.exe
exit /b 0
