#!/usr/bin/env bash
# Starts a Windows Server 2025 (evaluation) VM with IIS + Web Deploy for testing Pubwiz.
#
# The VM runs inside the dockurr/windows container (QEMU/KVM) with rootless podman.
# First start downloads the Windows ISO from Microsoft and installs it unattended (~30-60 min).
# Ports are bound to 127.0.0.1 only:
#   8006 -> web console (watch the install)     13389 -> RDP
#   8172 -> Web Deploy (WMSVC, msdeploy.axd)    8080 -> PubwizTest IIS site
set -euo pipefail

readonly container_name="pubwiz-iis"
readonly data_dir="${PUBWIZ_VM_DIR:-$HOME/.local/share/pubwiz-vm}"
readonly env_file="$data_dir/vm.env"
readonly script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

mkdir -p "$data_dir/storage" "$data_dir/oem"

# On btrfs, copy-on-write on the VM disk image causes problems with Windows Setup.
# +C only affects files created afterwards, so this must happen before the first start.
chattr +C "$data_dir/storage" 2>/dev/null || true

# Generate VM credentials once and keep them outside the repo.
if [[ ! -f "$env_file" ]]; then
    password="Pw-$(head -c 18 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 16)9a"
    printf 'USERNAME=pubwiz\nPASSWORD=%s\n' "$password" > "$env_file"
    chmod 600 "$env_file"
fi

# The OEM folder is copied to C:\OEM in the VM; install.bat runs after setup.
cp "$script_dir"/oem/* "$data_dir/oem/"

if podman container exists "$container_name"; then
    podman start "$container_name"
    exit 0
fi

podman run --detach --name "$container_name" \
    --env-file "$env_file" \
    --env VERSION=2025 \
    --env RAM_SIZE=8G \
    --env CPU_CORES=4 \
    --env DISK_SIZE=64G \
    --env USER_PORTS=8172,8080 \
    --device /dev/kvm \
    --device /dev/net/tun \
    --cap-add NET_ADMIN \
    --publish 127.0.0.1:8006:8006 \
    --publish 127.0.0.1:13389:3389/tcp \
    --publish 127.0.0.1:8172:8172 \
    --publish 127.0.0.1:8080:8080 \
    --volume "$data_dir/storage:/storage:Z" \
    --volume "$data_dir/oem:/oem:Z" \
    --stop-timeout 120 \
    docker.io/dockurr/windows

echo "VM starting. Watch the install at http://127.0.0.1:8006 (credentials in $env_file)"
