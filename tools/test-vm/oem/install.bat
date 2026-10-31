@echo off
rem Called by dockur/windows at the end of the unattended Windows install.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\OEM\setup-iis.ps1"
