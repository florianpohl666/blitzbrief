@echo off
setlocal
set "APP=%~dp0publish\BlitzBrief.Windows-self-contained\BlitzBrief.Windows.exe"

taskkill /IM BlitzBrief.Windows.exe /F >nul 2>nul

if not exist "%APP%" (
  echo BlitzBrief wurde noch nicht gebaut: "%APP%"
  echo Bitte zuerst dotnet publish ausfuehren.
  pause
  exit /b 1
)

start "" "%APP%"
