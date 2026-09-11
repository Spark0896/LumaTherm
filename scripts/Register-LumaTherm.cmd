@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "PAYLOAD=%~dp0"
if "%PAYLOAD:~-1%"=="\" set "PAYLOAD=%PAYLOAD:~0,-1%"
"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%PAYLOAD%\Register-LumaTherm.ps1" -PortableDirectory "%PAYLOAD%" -ConfirmCertificateImport
exit /b %ERRORLEVEL%
