@echo off
REM ===== Solana Champions - start multiplayer server + public tunnel =====
REM Double-click this file. Two windows open:
REM   1) the game server (serves the built web app + WebSocket lobby on :8787)
REM   2) the cloudflared tunnel - it prints a https://....trycloudflare.com URL
REM Open that URL on both laptops. Keep both windows open while you play.
REM Close the windows to stop. Re-run this .bat any time to get a fresh link.

echo Starting Solana Champions server...
start "SolChampions Server" cmd /k "cd /d %~dp0server && npx tsx src/index.ts"

echo Waiting for the server to come up...
timeout /t 4 /nobreak >nul

echo Starting public tunnel (the link will appear in the new window)...
start "SolChampions Tunnel - YOUR LINK IS HERE" cmd /k "C:\Users\Junius\Desktop\Masasa\backend\cloudflared.exe tunnel --url http://127.0.0.1:8787"

echo.
echo Two windows opened. Look in the "SolChampions Tunnel" window for the
echo   https://....trycloudflare.com  link. Open it on both laptops.
echo.
pause
