@echo off
setlocal
echo Starting the backend field test with peer diagnostics enabled.
echo If asked for PublicHost, enter your public UDP IP address or hostname.
echo Do not enter an HTTP URL. Default UDP port: 5070.
echo Your existing HTTP tunnel and Steam launch options stay separate.
echo Optional: pass -Players N (2-10) to choose the match_master player count.
echo Stop any already-running backend before continuing.
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-FieldTest.ps1" -Build -PeerDiagnostics %*
set "backendExitCode=%ERRORLEVEL%"
if not "%backendExitCode%"=="0" (
    echo.
    echo Backend startup or execution failed. See the error above.
    pause
)
exit /b %backendExitCode%
