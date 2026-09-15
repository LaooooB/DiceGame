@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\verify.ps1" %*
set result=%ERRORLEVEL%
echo.
if not "%result%"=="0" echo Verification failed. Read Artifacts\local-verification.json and the logs.
pause
exit /b %result%
