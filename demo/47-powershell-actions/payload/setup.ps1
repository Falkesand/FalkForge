# setup.ps1 -- Post-install configuration script
# This script runs as a deferred custom action with elevated privileges.

$installDir = Join-Path $env:ProgramFiles 'Demo\PowerShellDemo'

# Create a log file recording the installation
$logPath = Join-Path $installDir 'install.log'
$timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
"Installation completed at $timestamp" | Out-File -FilePath $logPath -Encoding UTF8

# Register the application in Add/Remove Programs metadata
$regPath = 'HKLM:\SOFTWARE\Demo\PowerShellDemo'
if (-not (Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null
}
Set-ItemProperty -Path $regPath -Name 'InstallDate' -Value $timestamp
Set-ItemProperty -Path $regPath -Name 'Version' -Value '1.0.0'
