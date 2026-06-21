@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Launch tModLoader Debug.ps1"

if errorlevel 1 (
    echo.
    echo Debug launcher failed. Read the message above, then press any key to close this window.
    pause >nul
)
