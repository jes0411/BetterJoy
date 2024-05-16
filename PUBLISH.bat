@echo off
set "options=--nologo --configuration Release -p:PublishSingleFile=true -p:DebugType=None -p:SelfContained=false -p:TieredPGO=true"
set "runtime=win-x64"
set "framework=net8.0-windows"

dotnet publish BetterJoy %options% --runtime %runtime% --framework %framework% -o build/%runtime%
pause

