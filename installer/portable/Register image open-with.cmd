@echo off
set "SCRIPT=%~dp0Register-PicMarkFileTypes.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
