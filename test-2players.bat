@echo off
REM ===== TEST ONLY - starts a match with just 2 players =====
REM Same as start-multiplayer.bat but forces MIN_TO_START=2 and a flat 3s
REM countdown. Use this for crew testing; use start-multiplayer.bat for real.

echo [TEST MODE] Starting server with 2-player start override...
start "SolChampions Server (TEST 2P)" cmd /k "cd /d %~dp0server && set MIN_TO_START=2&& set FILL_COUNTDOWN_MS=3000&& npx tsx src/index.ts"

echo Waiting for the server to come up...
timeout /t 4 /nobreak >nul

echo Starting public tunnel (the link will appear in the new window)...
start "SolChampions Tunnel - YOUR LINK IS HERE" cmd /k "C:\Users\Junius\Desktop\Masasa\backend\cloudflared.exe tunnel --url http://127.0.0.1:8787"

echo.
echo [TEST MODE] Match starts at 2 players, 3s countdown.
echo Look in the "SolChampions Tunnel" window for the
echo   https://....trycloudflare.com  link. Open it on both laptops.
echo.
pause
