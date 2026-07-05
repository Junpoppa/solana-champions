@echo off
REM Internal spin-up: launches server + tunnel as DETACHED windows (persist independently),
REM logging their output so the URL can be read back. Returns immediately after launching.
start "SolChampions Server" /min cmd /k "cd /d %~dp0server && npx tsx src/index.ts > "%TEMP%\solchamp_srv.log" 2>&1"
ping -n 6 127.0.0.1 >nul
start "SolChampions Tunnel" cmd /k "C:\Users\Junius\Desktop\Masasa\backend\cloudflared.exe tunnel --url http://127.0.0.1:8787 > "%TEMP%\solchamp_tun.log" 2>&1"
