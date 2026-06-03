@echo off
setlocal
set "APP=%~dp0publish\Blitztext.Windows-self-contained\Blitztext.Windows.exe"

if not exist "%APP%" (
  echo Blitztext wurde noch nicht gebaut: "%APP%"
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "$shell = New-Object -ComObject WScript.Shell; $shortcut = $shell.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\Blitztext.lnk'); $shortcut.TargetPath = '%APP%'; $shortcut.WorkingDirectory = Split-Path '%APP%'; $shortcut.Description = 'Blitztext Windows MVP'; $shortcut.Save()"
echo Desktop-Verknuepfung wurde erstellt: Blitztext.lnk
pause
