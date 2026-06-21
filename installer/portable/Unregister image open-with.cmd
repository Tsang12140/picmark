@echo off
set "SCRIPT=%~dp0Unregister-PicMarkFileTypes.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
