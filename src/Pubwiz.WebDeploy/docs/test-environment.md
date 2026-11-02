# Test environment

A Windows Server 2025 evaluation VM with IIS 10, WMSVC and Web Deploy 4.0, running in the open-source
[dockur/windows](https://github.com/dockur/windows) container (QEMU/KVM) under rootless podman on Linux. The scripts
that build it were kept out of the repository (`tools/test-vm` is local only); this describes them so they can be
rebuilt.

## The VM

```bash
podman run --detach --name pubwiz-iis \
  --env VERSION=2025 --env RAM_SIZE=8G --env CPU_CORES=4 --env DISK_SIZE=64G \
  --env USERNAME=pubwiz --env PASSWORD=<generated> \
  --env USER_PORTS=8172,8080,22 \
  --device /dev/kvm --device /dev/net/tun --cap-add NET_ADMIN \
  --publish 127.0.0.1:8006:8006 --publish 127.0.0.1:13389:3389/tcp \
  --publish 127.0.0.1:8172:8172 --publish 127.0.0.1:8080:8080 --publish 127.0.0.1:2222:22 \
  --volume <data>/storage:/storage:Z --volume <data>/oem:/oem:Z \
  docker.io/dockurr/windows
```

| Port (127.0.0.1) | Purpose |
|---|---|
| 8006 | Web console: install progress and the desktop |
| 13389 | RDP |
| 8172 | WMSVC: `https://localhost:8172/msdeploy.axd?site=PubwizTest` |
| 8080 | The `PubwizTest` site |
| 2222 | SSH (key only), to run `msdeploy.exe` and PowerShell in the VM |

- Keep the generated password in a file with mode 600, not in the repository.
- On btrfs, run `chattr +C` on the storage folder before the first start (the disk image is large and copy-on-write slows it).
- dockur deletes the downloaded ISO after installing, so a reinstall downloads it again.
- The evaluation license lasts 180 days.

## Setup inside Windows

dockur runs `oem/install.bat` once after Windows is installed, which runs a PowerShell script that:

1. Installs IIS and the management service: `Install-WindowsFeature Web-Server, Web-Mgmt-Service, Web-Mgmt-Console, Web-Scripting-Tools`.
2. Sets `EnableRemoteManagement = 1` in `HKLM:\SOFTWARE\Microsoft\WebManagement\Server` and starts `WMSVC` automatically.
3. Installs Web Deploy 4.0 from `https://download.microsoft.com/download/webdeploy_amd64_en-US.msi`
   (`msiexec /i ... /qn ADDLOCAL=ALL`). WMSVC must already be installed at this point.
4. Creates the `PubwizTest` site on port 8080 at `C:\inetpub\pubwiz`, and opens ports 8172 and 8080 in the firewall.
5. Installs OpenSSH Server with PowerShell as the default shell, key-only login (the public key goes in
   `C:\ProgramData\ssh\administrators_authorized_keys`, readable only by Administrators and SYSTEM).

Basic sign-in to WMSVC works with the VM's administrator account.

## Capturing traffic

The VM uses passt networking: from Windows, `host.lan` is the container. A small TLS relay runs in the container on
port 18172 with its own self-signed certificate. It forwards to WMSVC on 8172 and writes every decrypted byte to
`<connection>-request.bin` and `<connection>-response.bin`. A second script splits those streams into HTTP messages
(headers in `.txt`, bodies in `.body` with chunked encoding removed).

To capture, run `msdeploy.exe` in the VM (over SSH) against the relay:

```
msdeploy.exe -verb:sync -source:iisApp=C:\pubwiz-src
  "-dest:iisApp=PubwizTest,computerName=https://host.lan:18172/msdeploy.axd?site=PubwizTest,userName=...,password=...,authType=Basic"
  -allowUntrusted
```

Use small, known source folders, so each capture shows one change: one new file, one changed file, one deleted file,
a new folder.
