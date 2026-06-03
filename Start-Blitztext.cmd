@echo off
setlocal
set "APP=%~dp0publish\Blitztext.Windows-self-contained\Blitztext.Windows.exe"

taskkill /IM Blitztext.Windows.exe /F >nul 2>nul

if not exist "%APP%" (
  echo Blitztext wurde noch nicht gebaut: "%APP%"
  echo Bitte zuerst dotnet publish ausfuehren.
  pause
  exit /b 1
)

start "" "%APP%"
