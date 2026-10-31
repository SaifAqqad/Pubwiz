# Runs once inside the Windows VM at the end of the unattended install.
# Installs IIS, the Web Management Service (WMSVC) and Web Deploy 4.0,
# creates an empty "PubwizTest" site on port 8080 to deploy into,
# and enables OpenSSH (key login only) so tests can run commands in the VM.

$ErrorActionPreference = 'Stop'
Start-Transcript -Path 'C:\OEM\setup-iis.log'

# IIS + management service. WMSVC must exist before Web Deploy is installed,
# otherwise the Web Deploy installer skips its WMSVC handler (msdeploy.axd).
Install-WindowsFeature Web-Server, Web-Mgmt-Service, Web-Mgmt-Console, Web-Scripting-Tools -IncludeManagementTools

# Allow remote (non-localhost) connections to WMSVC on port 8172.
Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\WebManagement\Server' -Name 'EnableRemoteManagement' -Value 1
Set-Service -Name 'WMSVC' -StartupType Automatic

# Web Deploy 4.0, official Microsoft download.
$webDeployMsi = 'C:\OEM\webdeploy_amd64_en-US.msi'
Invoke-WebRequest -Uri 'https://download.microsoft.com/download/webdeploy_amd64_en-US.msi' -OutFile $webDeployMsi -UseBasicParsing
Start-Process -FilePath 'msiexec.exe' -Wait -ArgumentList '/i', $webDeployMsi, '/qn', '/norestart', 'ADDLOCAL=ALL', '/l*v', 'C:\OEM\webdeploy-install.log'

# Test site that Pubwiz publishes into.
Import-Module WebAdministration
New-Item -Path 'C:\inetpub\pubwiz' -ItemType Directory -Force | Out-Null
New-Website -Name 'PubwizTest' -Port 8080 -PhysicalPath 'C:\inetpub\pubwiz' -Force | Out-Null

New-NetFirewallRule -DisplayName 'Web Management Service (8172)' -Direction Inbound -Protocol TCP -LocalPort 8172 -Action Allow | Out-Null
New-NetFirewallRule -DisplayName 'Pubwiz test site (8080)' -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow | Out-Null

Restart-Service -Name 'WMSVC'

# OpenSSH server, PowerShell as the default shell, key-only login.
if (-not (Get-Service -Name 'sshd' -ErrorAction SilentlyContinue)) {
    Add-WindowsCapability -Online -Name 'OpenSSH.Server~~~~0.0.1.0' | Out-Null
}
New-Item -Path 'HKLM:\SOFTWARE\OpenSSH' -Force | Out-Null
New-ItemProperty -Path 'HKLM:\SOFTWARE\OpenSSH' -Name 'DefaultShell' -Value 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -PropertyType String -Force | Out-Null
Set-Service -Name 'sshd' -StartupType Automatic
Start-Service -Name 'sshd'

# Administrators read their keys from administrators_authorized_keys, which must only be accessible to Administrators and SYSTEM.
$authorizedKeys = 'C:\ProgramData\ssh\administrators_authorized_keys'
Copy-Item -Path 'C:\OEM\authorized_key.pub' -Destination $authorizedKeys -Force
icacls.exe $authorizedKeys /inheritance:r /grant 'Administrators:F' /grant 'SYSTEM:F' | Out-Null

$sshdConfig = 'C:\ProgramData\ssh\sshd_config'
(Get-Content $sshdConfig) -replace '^#?PasswordAuthentication .*', 'PasswordAuthentication no' | Set-Content $sshdConfig
Restart-Service -Name 'sshd'
New-NetFirewallRule -DisplayName 'OpenSSH (22)' -Direction Inbound -Protocol TCP -LocalPort 22 -Action Allow -ErrorAction SilentlyContinue | Out-Null

Set-Content -Path 'C:\OEM\setup-complete.txt' -Value (Get-Date -Format o)

Stop-Transcript
