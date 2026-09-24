@echo off
chcp 65001 >nul
setlocal

rem ============================================================
rem SCADA one-click dev launcher (bat entry) -> invoke run-dev.ps1
rem Usage: double-click this file, or run `run-dev.bat` in cmd.
rem Prereq: env vars SystemDbConfig__Password and Jwt__Key must be
rem         set already; the ps1 will print a warning and exit if
rem         any of them is missing.
rem ============================================================

pushd "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-dev.ps1"

popd

rem Keep the window open to show script output / missing-config hint.
echo.
pause