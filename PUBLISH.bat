@echo off
set "options=--nologo --configuration Release -p:PublishSingleFile=true --self-contained false"
set "runtime=win10-x64"
set "framework=net6.0-windows"

dotnet publish BetterJoyForCemu %options% --runtime %runtime% --framework %framework% -o build/%runtime%
pause

