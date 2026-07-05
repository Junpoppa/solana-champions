<#
Sets Unity AI MCP "direct" connection policy to NOT require per-connection approval.

Why: Unity's EditorPrefs policy has direct (named-pipe) connections at
requiresApproval=true, so every MCP reconnect needs a fresh manual Approve click and
otherwise returns "Connection revoked". This flips it to false so the connection from
Claude Code is auto-allowed. RUN WITH UNITY CLOSED (Unity rewrites EditorPrefs on exit).

Security note: this disables the approval prompt for local direct MCP clients on THIS
machine/user. Intended for a trusted single-user dev setup.
#>
$ErrorActionPreference = 'Stop'
$path = 'HKCU:\Software\Unity Technologies\Unity Editor 5.x'
$sub  = 'Software\Unity Technologies\Unity Editor 5.x'

$k = Get-Item $path
$name = $k.GetValueNames() | Where-Object { $_ -match 'Unity\.AI\.MCP\.ProjectSettings\.v2' }
if (-not $name) { Write-Error 'MCP settings value not found in EditorPrefs'; exit 1 }

$raw = $k.GetValue($name)
$isBytes = $raw -is [byte[]]
if ($isBytes) {
    $hadNull = ($raw[-1] -eq 0)
    $txt = [System.Text.Encoding]::UTF8.GetString($raw).TrimEnd([char]0)
} else {
    $hadNull = $false
    $txt = [string]$raw
}

if ($txt -notmatch '"requiresApproval": true') {
    Write-Output 'Already set (no "requiresApproval": true found). Nothing to change.'
    exit 0
}

$new = $txt -replace '"requiresApproval": true', '"requiresApproval": false'

$rk = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($sub, $true)
if ($isBytes) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($new)
    if ($hadNull) { $bytes = [byte[]]($bytes + [byte]0) }
    $rk.SetValue($name, $bytes, [Microsoft.Win32.RegistryValueKind]::Binary)
} else {
    $rk.SetValue($name, $new, $k.GetValueKind($name))
}
$rk.Flush(); $rk.Close()

Write-Output 'DONE - direct.requiresApproval set to false. Start Unity, then reconnect MCP.'
