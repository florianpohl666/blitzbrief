@echo off
setlocal
set "APP=%~dp0publish\BlitzBrief.Windows-self-contained\BlitzBrief.Windows.exe"

if not exist "%APP%" (
  echo BlitzBrief wurde noch nicht gebaut: "%APP%"
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "$shell = New-Object -ComObject WScript.Shell; $shortcut = $shell.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\BlitzBrief.lnk'); $shortcut.TargetPath = '%APP%'; $shortcut.WorkingDirectory = Split-Path '%APP%'; $shortcut.Description = 'BlitzBrief Windows MVP'; $shortcut.Save()"
echo Desktop-Verknuepfung wurde erstellt: BlitzBrief.lnk
pause
