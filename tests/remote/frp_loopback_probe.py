#!/usr/bin/env python3
"""Exercise actual frps/frpc TCP and UDP forwarding in both tunnel modes."""

from __future__ import annotations
import argparse
import pathlib
import socket
import socketserver
import subprocess
import tempfile
import threading
import time

def port(kind: int) -> int:
    with socket.socket(socket.AF_INET, kind) as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]

class TcpEcho(socketserver.BaseRequestHandler):
    def handle(self) -> None:
        data = self.request.recv(4096)
        self.request.sendall(data)

def udp_echo(stop: threading.Event, local: int) -> None:
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        sock.bind(("127.0.0.1", local))
        sock.settimeout(0.25)
        while not stop.is_set():
            try:
                data, addr = sock.recvfrom(4096)
            except socket.timeout:
                continue
            sock.sendto(data, addr)

def check_tcp(remote: int) -> bool:
    try:
        with socket.create_connection(("127.0.0.1", remote), timeout=1) as sock:
            sock.sendall(b"skyroof-tcp")
            return sock.recv(100) == b"skyroof-tcp"
    except (OSError, TimeoutError):
        return False

def check_udp(remote: int) -> bool:
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
            sock.settimeout(1.2)
            sock.sendto(b"skyroof-udp", ("127.0.0.1", remote))
            return sock.recvfrom(4096)[0] == b"skyroof-udp"
    except (OSError, TimeoutError):
        return False

def wait(check, seconds: float = 18) -> None:
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        if check():
            return
        time.sleep(0.3)
    raise RuntimeError("FRP proxy failed its transport round trip")

def stop_process(proc: subprocess.Popen) -> None:
    if proc.poll() is not None:
        return
    proc.terminate()
    try:
        proc.wait(timeout=4)
    except subprocess.TimeoutExpired:
        proc.kill()
        proc.wait(timeout=4)

def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--frps", required=True)
    parser.add_argument("--frpc", required=True)
    args = parser.parse_args()
    tcp_control, quic_control = port(socket.SOCK_STREAM), port(socket.SOCK_DGRAM)
    local_tcp, local_udp = port(socket.SOCK_STREAM), port(socket.SOCK_DGRAM)
    remote_tcp, remote_udp = port(socket.SOCK_STREAM), port(socket.SOCK_DGRAM)
    with tempfile.TemporaryDirectory() as temp:
        work = pathlib.Path(temp)
        server_config = work / "frps.toml"
        server_config.write_text(
            f'bindAddr = "127.0.0.1"\nbindPort = {tcp_control}\n'
            f'quicBindPort = {quic_control}\nproxyBindAddr = "127.0.0.1"\n'
            'auth.method = "token"\nauth.token = "ci-only"\n'
            f'allowPorts = [{{ single = {remote_tcp} }}, {{ single = {remote_udp} }}]\n',
            encoding="utf-8",
        )
        server = socketserver.ThreadingTCPServer(("127.0.0.1", local_tcp), TcpEcho)
        server_thread = threading.Thread(target=server.serve_forever, daemon=True)
        server_thread.start()
        stop_udp = threading.Event()
        udp_thread = threading.Thread(target=udp_echo, args=(stop_udp, local_udp), daemon=True)
        udp_thread.start()
        frps = subprocess.Popen([args.frps, "-c", str(server_config)],
                                stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
        try:
            time.sleep(1)
            for protocol in ("tcp", "quic"):
                frpc_conf = work / f"frpc-{protocol}.toml"
                control = tcp_control if protocol == "tcp" else quic_control
                frpc_conf.write_text(
                    f'serverAddr = "127.0.0.1"\nserverPort = {control}\n'
                    'auth.method = "token"\nauth.token = "ci-only"\n'
                    f'transport.protocol = "{protocol}"\ntransport.tls.enable = true\n'
                    f'[[proxies]]\nname = "skyroof-control"\ntype = "tcp"\n'
                    f'localIP = "127.0.0.1"\nlocalPort = {local_tcp}\n'
                    f'remotePort = {remote_tcp}\n'
                    f'[[proxies]]\nname = "skyroof-rtc"\ntype = "udp"\n'
                    f'localIP = "127.0.0.1"\nlocalPort = {local_udp}\n'
                    f'remotePort = {remote_udp}\n',
                    encoding="utf-8",
                )
                subprocess.run([args.frpc, "verify", "-c", str(frpc_conf)],
                               check=True, timeout=10)
                frpc = subprocess.Popen([args.frpc, "-c", str(frpc_conf)],
                                        stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
                try:
                    wait(lambda: check_tcp(remote_tcp))
                    wait(lambda: check_udp(remote_udp))
                    print(f"PASS: frps/frpc {protocol.upper()} TCP + UDP real socket echo")
                finally:
                    stop_process(frpc)
                time.sleep(2)
        finally:
            stop_process(frps)
            stop_udp.set()
            server.shutdown()
            server.server_close()

if __name__ == "__main__":
    main()
