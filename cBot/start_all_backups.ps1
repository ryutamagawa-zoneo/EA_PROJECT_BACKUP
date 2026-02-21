# start_all_backups.ps1（確定版：重複起動防止／隠し起動／軽量）
# 置き場所：D:\ChatGPT EA Development\プロジェクト\cBot\start_all_backups.ps1
# 前提：各Botに <Bot>\BackUp\watch_backup_prev.ps1 が存在する

$base = Split-Path -Parent $MyInvocation.MyCommand.Path

$bots = @(
  "ATFunded",
  "COPY_TOOL",
  "EA_DEV_FIRST",
  "EA_HL_DEV",
  "HIVE_DEV_TEST"
)

function AlreadyRunning([string]$watcherPath) {
  $procs = Get-CimInstance Win32_Process -Filter "Name='powershell.exe'"
  foreach ($p in $procs) {
    if ($p.CommandLine -and $p.CommandLine -like "*$watcherPath*") { return $true }
  }
  return $false
}

foreach ($bot in $bots) {
  $watcher = Join-Path $base "$bot\BackUp\watch_backup_prev.ps1"
  if (-not (Test-Path $watcher)) { continue }

  # すでに動いているなら起動しない（増殖防止の核心）
  if (AlreadyRunning $watcher) { continue }

  Start-Process -FilePath "powershell.exe" `
    -ArgumentList "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$watcher`"" `
    -WindowStyle Hidden
}