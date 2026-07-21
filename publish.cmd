@echo off
rem Erstellt self-contained Builds (keine .NET-Installation auf dem Zielrechner noetig)

echo === Agent (fuer den Zweitrechner) ===
dotnet publish src\DisplayPad.Agent -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish\Agent

echo === Host (fuer den Rechner mit DisplayPad) ===
dotnet publish src\DisplayPad.Host -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish\Host

echo.
echo Fertig: publish\Agent\DisplayPad.Agent.exe und publish\Host\DisplayPad.Host.exe
