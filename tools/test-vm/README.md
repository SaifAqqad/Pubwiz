# Test VM (IIS + Web Deploy)

A local Windows Server 2025 **evaluation** VM for testing Pubwiz's Web Deploy, IIS and FTP targets.
It runs in the open-source [dockur/windows](https://github.com/dockur/windows) container (QEMU/KVM) with rootless podman, so the host system isn't changed.

```bash
./tools/test-vm/start.sh                 # create or start the VM
podman logs -f pubwiz-iis                # follow progress
podman stop pubwiz-iis                   # stop (state is kept)
```

| Port (127.0.0.1) | Purpose |
|---|---|
| 8006 | Web console (install progress, desktop) |
| 13389 | RDP (host 3389 is often taken by GNOME Remote Desktop) |
| 8172 | Web Deploy endpoint: `https://localhost:8172/msdeploy.axd?site=PubwizTest` |
| 8080 | The `PubwizTest` IIS site |
| 2222 | SSH, key only: `ssh -i ~/.local/share/pubwiz-vm/id_ed25519 -p 2222 pubwiz@127.0.0.1` |

- Data and the generated credentials are in `~/.local/share/pubwiz-vm/` (`vm.env`, mode 600). Override the folder with `PUBWIZ_VM_DIR`.
- `oem/setup-iis.ps1` runs once after Windows is installed. Its log is `C:\OEM\setup-iis.log`, and `C:\OEM\setup-complete.txt` appears when it finishes.
- The Windows evaluation license lasts 180 days.
