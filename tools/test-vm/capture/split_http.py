"""
Splits the raw streams written by relay.py into separate HTTP/1.1 messages.

For every <connection>-request.bin / -response.bin it writes, per message:
  <connection>-request-<n>.txt   start line + headers
  <connection>-request-<n>.body  the body, with chunked transfer encoding removed

Usage: python3 split_http.py <capture folder>
"""

import glob
import os
import sys


def read_chunked(data, position):
    body = bytearray()
    while True:
        line_end = data.index(b"\r\n", position)
        size = int(data[position:line_end].split(b";")[0], 16)
        position = line_end + 2
        if size == 0:
            # Skip optional trailers up to the empty line.
            return bytes(body), data.index(b"\r\n", position) + 2
        body += data[position:position + size]
        position += size + 2


def split(data):
    position = 0
    while position < len(data):
        header_end = data.find(b"\r\n\r\n", position)
        if header_end < 0:
            yield data[position:].decode("latin-1"), b""
            return

        head = data[position:header_end].decode("latin-1")
        headers = {line.split(":", 1)[0].strip().lower(): line.split(":", 1)[1].strip()
                   for line in head.split("\r\n")[1:] if ":" in line}
        position = header_end + 4

        if headers.get("transfer-encoding", "").lower() == "chunked":
            body, position = read_chunked(data, position)
        else:
            length = int(headers.get("content-length", "0"))
            body = data[position:position + length]
            position += length

        yield head, body


def main():
    folder = sys.argv[1]
    for stream in sorted(glob.glob(os.path.join(folder, "*-request.bin")) + glob.glob(os.path.join(folder, "*-response.bin"))):
        with open(stream, "rb") as file:
            data = file.read()

        base = stream[:-len(".bin")]
        for number, (head, body) in enumerate(split(data), start=1):
            with open(f"{base}-{number}.txt", "w") as file:
                file.write(head + "\n")
            with open(f"{base}-{number}.body", "wb") as file:
                file.write(body)
            print(f"{os.path.basename(base)}-{number}: {head.splitlines()[0] if head else '(empty)'} ({len(body)} bytes)")


main()
