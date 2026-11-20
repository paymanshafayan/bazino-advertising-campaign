"""
Safe deployer for the FreeVideo RTX 5090 HTTP API.
Uses the proven 3KB-chunk Base64 SSH uploader (stdin=DEVNULL) to avoid
Windows OpenSSH scp EOF hangs, MTU packet drops, and MaxStartups limits.
"""

import base64
import io
import json
import secrets
import subprocess
import tarfile
import time
import urllib.request
from pathlib import Path

KEY_PATH = r"C:\Users\payman\.ssh\id_ed25519"
HOST = "root@194.93.48.125"
BASE_DIR = Path(__file__).resolve().parent
SERVER_DIR = BASE_DIR / "server"
CONFIG_FILE = BASE_DIR / ".api_config.json"


def ssh_run(cmd: str, timeout: int = 30) -> str:
    last_err = ""
    for attempt in range(4):
        try:
            r = subprocess.run(
                [
                    "ssh",
                    "-i",
                    KEY_PATH,
                    "-o",
                    "StrictHostKeyChecking=no",
                    "-o",
                    "ConnectTimeout=10",
                    HOST,
                    cmd,
                ],
                stdin=subprocess.DEVNULL,
                capture_output=True,
                text=True,
                timeout=timeout,
            )
            if r.returncode == 0:
                return r.stdout
            last_err = r.stderr.strip() or f"exit={r.returncode}"
        except subprocess.TimeoutExpired:
            last_err = "timeout"
        time.sleep(1.5)
    raise RuntimeError(f"SSH failed ({last_err}): {cmd[:120]}")


def upload_bytes(raw_bytes: bytes, remote_path: str) -> None:
    b64 = base64.b64encode(raw_bytes).decode("ascii")
    tmp_b64 = f"/tmp/upload_{int(time.time())}.b64"
    ssh_run(f"rm -f {tmp_b64}")
    chunk_size = 3000
    total_chunks = (len(b64) + chunk_size - 1) // chunk_size
    for idx, i in enumerate(range(0, len(b64), chunk_size), start=1):
        chunk = b64[i : i + chunk_size]
        ssh_run(f"printf '%s' '{chunk}' >> {tmp_b64}")
        print(f"  uploaded chunk {idx}/{total_chunks}")
        time.sleep(0.25)
    ssh_run(f"base64 -d {tmp_b64} > {remote_path} && rm -f {tmp_b64}")


def load_or_create_api_key() -> str:
    if CONFIG_FILE.is_file():
        data = json.loads(CONFIG_FILE.read_text(encoding="utf-8"))
        if data.get("api_key"):
            if data.get("base_url") != "https://194.93.48.125":
                data["base_url"] = "https://194.93.48.125"
                CONFIG_FILE.write_text(json.dumps(data, indent=2), encoding="utf-8")
            return data["api_key"]
    api_key = "fv_" + secrets.token_urlsafe(32)
    CONFIG_FILE.write_text(
        json.dumps(
            {
                "base_url": "https://194.93.48.125",
                "api_key": api_key,
            },
            indent=2,
        ),
        encoding="utf-8",
    )
    return api_key


def build_tar_gz(api_key: str) -> bytes:
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w:gz") as tar:
        for fname in ("api_server.py", "freevideo-api.service"):
            fpath = SERVER_DIR / fname
            tar.add(fpath, arcname=fname)
        env_bytes = f"FREEVIDEO_API_KEY={api_key}\n".encode("utf-8")
        info = tarfile.TarInfo(name=".api_env")
        info.size = len(env_bytes)
        info.mode = 0o600
        tar.addfile(info, io.BytesIO(env_bytes))
    return buf.getvalue()


def main() -> None:
    print("1. Preparing API key and deployment bundle...")
    api_key = load_or_create_api_key()
    bundle = build_tar_gz(api_key)
    print(f"   Bundle size: {len(bundle)} bytes")

    print("2. Checking SSH connectivity to RTX 5090 server...")
    out = ssh_run("hostname && nvidia-smi --query-gpu=name --format=csv,noheader")
    print(f"   Connected: {out.strip().replace(chr(10), ' | ')}")

    print("3. Uploading bundle in safe 3KB chunks...")
    remote_tar = "/tmp/freevideo_api_bundle.tar.gz"
    upload_bytes(bundle, remote_tar)

    print("4. Extracting files, creating SSL cert & installing dependencies if needed...")
    install_cmd = (
        "mkdir -p /root/FreeVideo /root/FreeVideo/ssl /root/freevideo-api-jobs && "
        f"tar -xzf {remote_tar} -C /tmp && "
        "mv /tmp/api_server.py /root/FreeVideo/api_server.py && "
        "mv /tmp/.api_env /root/FreeVideo/.api_env && "
        "mv /tmp/freevideo-api.service /etc/systemd/system/freevideo-api.service && "
        f"rm -f {remote_tar} && "
        "[ -f /root/FreeVideo/ssl/freevideo.crt ] || openssl req -x509 -nodes -days 3650 -newkey rsa:2048 "
        "-keyout /root/FreeVideo/ssl/freevideo.key -out /root/FreeVideo/ssl/freevideo.crt "
        "-subj '/CN=194.93.48.125' -addext 'subjectAltName=IP:194.93.48.125' 2>/dev/null && "
        "/root/FreeVideo/envs/unified/bin/python -c 'import fastapi, uvicorn, multipart' 2>/dev/null || "
        "/root/FreeVideo/envs/unified/bin/pip install -q fastapi 'uvicorn[standard]' python-multipart"
    )
    ssh_run(install_cmd, timeout=180)

    print("5. Opening port 443 (if ufw active) and starting systemd service...")
    service_cmd = (
        "(ufw status 2>/dev/null | grep -q 'Status: active' && ufw allow 443/tcp || true) && "
        "systemctl daemon-reload && "
        "systemctl enable freevideo-api.service && "
        "systemctl restart freevideo-api.service && "
        "sleep 2 && "
        "systemctl is-active freevideo-api.service"
    )
    status = ssh_run(service_cmd, timeout=30).strip()
    print(f"   Service status: {status}")

    print("6. Verifying HTTPS /health endpoint from Windows...")
    import ssl
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    opener = urllib.request.build_opener(
        urllib.request.ProxyHandler({}),
        urllib.request.HTTPSHandler(context=ctx),
    )
    req = urllib.request.Request(
        "https://194.93.48.125/health",
        headers={"X-API-Key": api_key},
    )
    with opener.open(req, timeout=15) as resp:
        health_json = resp.read().decode("utf-8")
    print(f"   Health OK: {health_json}")
    print(f"\nDeployment complete! Config saved in: {CONFIG_FILE}")


if __name__ == "__main__":
    main()
