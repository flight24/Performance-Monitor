# 构建后端 exe
# 需要先安装: pip install pyinstaller psutil gputil pythonnet wmi
param(
    [switch]$Tkinter
)

$root = Split-Path -Parent $PSScriptRoot

if ($Tkinter) {
    Write-Host "Building Tkinter standalone exe..." -ForegroundColor Cyan
    Set-Location -LiteralPath "$root\python\tkinter"
    pyinstaller SystemMonitor.spec
    Write-Host "Done: python\tkinter\dist\SystemMonitor.exe" -ForegroundColor Green
} else {
    Write-Host "Building Electron backend exe..." -ForegroundColor Cyan
    Set-Location -LiteralPath "$root\python"
    pyinstaller --distpath dist monitor.spec
    Write-Host "Done: python\dist\monitor_backend.exe" -ForegroundColor Green
}