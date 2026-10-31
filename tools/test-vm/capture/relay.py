"""
Logging TLS relay for studying the Web Deploy (WMSVC) protocol.

Runs inside the test VM's container. The VM uses passt networking: from Windows, the gateway
(host.lan) is the container itself, and the container's port 8172 is forwarded to WMSVC in the VM.

msdeploy.exe in the VM connects to this relay instead of WMSVC
(computerName=https://host.lan:18172/msdeploy.axd?site=...), and every decrypted byte is written to
<output>/<connection>-request.bin and -response.bin while being forwarded to <upstream>:8172.

Usage: python3 relay.py <output folder> <cert.pem> <key.pem> [upstream host]
"""

import asyncio
import itertools
import os
import ssl
import sys

LISTEN_PORT = 18172
UPSTREAM_PORT = 8172


async def pipe(reader, writer, log_file):
    try:
        while data := await reader.read(65536):
            log_file.write(data)
            log_file.flush()
            writer.write(data)
            await writer.drain()
    finally:
        writer.close()


async def main():
    output, cert, key = sys.argv[1], sys.argv[2], sys.argv[3]
    upstream = sys.argv[4] if len(sys.argv) > 4 else "127.0.0.1"
    os.makedirs(output, exist_ok=True)

    server_tls = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    server_tls.load_cert_chain(cert, key)

    # WMSVC uses a self-signed certificate.
    upstream_tls = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    upstream_tls.check_hostname = False
    upstream_tls.verify_mode = ssl.CERT_NONE

    connection_ids = itertools.count(1)

    async def handle(client_reader, client_writer):
        connection = next(connection_ids)
        print(f"connection {connection} from {client_writer.get_extra_info('peername')}", flush=True)
        upstream_reader, upstream_writer = await asyncio.open_connection(upstream, UPSTREAM_PORT, ssl=upstream_tls)

        with open(f"{output}/{connection:03}-request.bin", "wb") as requests, open(f"{output}/{connection:03}-response.bin", "wb") as responses:
            await asyncio.gather(
                pipe(client_reader, upstream_writer, requests),
                pipe(upstream_reader, client_writer, responses),
                return_exceptions=True)

    server = await asyncio.start_server(handle, "0.0.0.0", LISTEN_PORT, ssl=server_tls)
    print(f"listening on {LISTEN_PORT}, forwarding to {upstream}:{UPSTREAM_PORT}, writing to {output}", flush=True)
    async with server:
        await server.serve_forever()


asyncio.run(main())
