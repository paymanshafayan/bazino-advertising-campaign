"""
Windows client for the FreeVideo RTX 5090 HTTP API.
Uses MTU-safe 16KB chunked uploads + status polling + direct MP4 streaming download
with SHA-256 verification so large uploads or 4-minute renders never hit WinError 10054.
"""

import argparse
import hashlib
import io
import json
import sys
import time
from pathlib import Path
from typing import List, Optional

import requests
import urllib3
from PIL import Image

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

BASE_DIR = Path(__file__).resolve().parent
DEFAULT_CONFIG = BASE_DIR / ".api_config.json"
CHUNK_BYTES = 32768  # 32 KB over encrypted HTTPS


def optimize_image_bytes(img_path: Path, max_w: int = 512, max_h: int = 896, quality: int = 78) -> bytes:
    with Image.open(img_path) as img:
        img = img.convert("RGB")
        img.thumbnail((max_w, max_h), Image.Resampling.LANCZOS)
        buf = io.BytesIO()
        img.save(buf, format="JPEG", quality=quality, optimize=True)
        return buf.getvalue()


def load_config(base_url: Optional[str], api_key: Optional[str]) -> tuple[str, str]:
    cfg = {}
    if DEFAULT_CONFIG.is_file():
        cfg = json.loads(DEFAULT_CONFIG.read_text(encoding="utf-8"))
    url = (base_url or cfg.get("base_url") or "https://194.93.48.125").rstrip("/")
    key = api_key or cfg.get("api_key") or ""
    return url, key


def make_session() -> requests.Session:
    s = requests.Session()
    s.trust_env = False
    s.verify = False
    return s


def stream_and_verify(resp: requests.Response, out_path: Path) -> Path:
    out_path.parent.mkdir(parents=True, exist_ok=True)
    expected_sha = resp.headers.get("X-File-SHA256", "").strip().lower()
    job_id = resp.headers.get("X-Job-Id", "")

    h = hashlib.sha256()
    total_bytes = 0
    with out_path.open("wb") as f:
        for chunk in resp.iter_content(chunk_size=524288):
            if not chunk:
                continue
            f.write(chunk)
            h.update(chunk)
            total_bytes += len(chunk)

    actual_sha = h.hexdigest().lower()
    if expected_sha and actual_sha != expected_sha:
        raise RuntimeError(
            f"SHA-256 mismatch! expected={expected_sha}, actual={actual_sha}, job_id={job_id}"
        )

    print(f"Saved {out_path} ({total_bytes:,} bytes | job={job_id} | sha256=OK)")
    return out_path


def upload_chunked(
    session: requests.Session,
    url: str,
    headers: dict,
    job_id: str,
    remote_name: str,
    raw_bytes: bytes,
) -> None:
    for i in range(0, len(raw_bytes), CHUNK_BYTES):
        part = raw_bytes[i : i + CHUNK_BYTES]
        for attempt in range(6):
            try:
                r = session.post(
                    f"{url}/jobs/{job_id}/chunk",
                    params={"name": remote_name, "offset": i},
                    headers={**headers, "Content-Type": "application/octet-stream"},
                    data=part,
                    timeout=(15, 45),
                )
                if r.status_code == 200:
                    break
                if attempt == 5:
                    raise RuntimeError(f"Chunk upload failed: {r.status_code} {r.text}")
            except requests.RequestException as exc:
                if attempt == 5:
                    raise RuntimeError(f"Chunk upload network error at offset {i}: {exc}") from exc
            time.sleep(1.0)


def notify_webhook(
    webhook_url: Optional[str],
    payload: dict,
    api_key: Optional[str] = None,
) -> None:
    if not webhook_url or not webhook_url.strip():
        return
    try:
        s = make_session()
        hdrs = {"Content-Type": "application/json"}
        if api_key:
            hdrs["X-API-Key"] = api_key
        s.post(webhook_url.strip(), json=payload, headers=hdrs, timeout=(5, 10))
    except Exception as exc:
        print(f"Warning: webhook notification failed: {exc}", file=sys.stderr)


def generate_video(
    images: List[Path],
    prompt: str,
    out_path: Path,
    width: int = 768,
    height: int = 1344,
    seconds: int = 10,
    seed: int = 0,
    two_pass: bool = True,
    upscale_factor: float = 1.5,
    base_url: Optional[str] = None,
    api_key: Optional[str] = None,
    webhook_url: Optional[str] = None,
) -> Path:
    if not 1 <= len(images) <= 6:
        raise ValueError(f"Expected 1 to 6 images, got {len(images)}")

    cfg_webhook = None
    if DEFAULT_CONFIG.is_file():
        cfg_webhook = json.loads(DEFAULT_CONFIG.read_text(encoding="utf-8")).get("webhook_url")
    effective_webhook = webhook_url or cfg_webhook

    url, key = load_config(base_url, api_key)
    headers = {"X-API-Key": key} if key else {}
    session = make_session()

    r = session.post(f"{url}/jobs/create", headers=headers, timeout=(15, 30))
    if r.status_code != 200:
        raise RuntimeError(f"Create job failed: {r.status_code} {r.text}")
    job_id = r.json()["job_id"]
    print(f"Created job {job_id}")

    for idx, img_path in enumerate(images, start=1):
        p = Path(img_path)
        if not p.is_file():
            raise FileNotFoundError(f"Image not found: {p}")
        data = optimize_image_bytes(p)
        upload_chunked(session, url, headers, job_id, f"raw_{idx}", data)
        print(f"  Uploaded image {idx}/{len(images)} ({len(data):,} bytes)")

    upload_chunked(session, url, headers, job_id, "prompt_raw.txt", prompt.encode("utf-8"))

    start_data = {
        "num_images": str(len(images)),
        "width": str(width),
        "height": str(height),
        "seconds": str(seconds),
        "seed": str(seed),
        "two_pass": "true" if two_pass else "false",
        "upscale_factor": str(upscale_factor),
    }
    if effective_webhook and not ("127.0.0.1" in effective_webhook or "localhost" in effective_webhook):
        start_data["webhook_url"] = effective_webhook

    r = session.post(
        f"{url}/jobs/{job_id}/start",
        headers=headers,
        data=start_data,
        timeout=(15, 30),
    )
    if r.status_code != 200:
        raise RuntimeError(f"Start job failed: {r.status_code} {r.text}")
    print(f"Generation started on RTX 5090 (job={job_id})...")

    final_status = {}
    while True:
        time.sleep(15)
        try:
            sr = session.get(f"{url}/jobs/{job_id}/status", headers=headers, timeout=(15, 30))
        except requests.RequestException:
            continue
        if sr.status_code != 200:
            continue
        st = sr.json()
        status = st.get("status")
        if status == "completed":
            final_status = st
            print(f"Completed in {st.get('elapsed_seconds')}s! Downloading MP4...")
            break
        if status in ("failed", "timeout"):
            notify_webhook(
                effective_webhook,
                {"event": f"video.{status}", "job_id": job_id, **st},
                key,
            )
            raise RuntimeError(f"Job {job_id} {status}: {st}")

    saved = resume_job_video(job_id, Path(out_path), base_url, api_key)
    notify_webhook(
        effective_webhook,
        {
            "event": "video.downloaded",
            "job_id": job_id,
            "local_path": str(saved),
            "size_bytes": saved.stat().st_size,
            "sha256": final_status.get("sha256"),
            "elapsed_seconds": final_status.get("elapsed_seconds"),
        },
        key,
    )
    return saved


def resume_job_video(
    job_id: str,
    out_path: Path,
    base_url: Optional[str] = None,
    api_key: Optional[str] = None,
) -> Path:
    url, key = load_config(base_url, api_key)
    headers = {"X-API-Key": key} if key else {}
    session = make_session()
    with session.get(
        f"{url}/jobs/{job_id}/video",
        headers=headers,
        stream=True,
        timeout=(30, 300),
    ) as resp:
        if resp.status_code != 200:
            raise RuntimeError(f"API error {resp.status_code}: {resp.text[:800]}")
        return stream_and_verify(resp, Path(out_path))


def main() -> None:
    parser = argparse.ArgumentParser(description="FreeVideo RTX 5090 API Client")
    parser.add_argument("--images", nargs="+", help="1 to 6 reference image paths")
    parser.add_argument("--prompt", help="Prompt text (or use --prompt-file)")
    parser.add_argument("--prompt-file", help="Path to prompt.txt")
    parser.add_argument("--out", required=True, help="Output .mp4 path")
    parser.add_argument("--width", type=int, default=768)
    parser.add_argument("--height", type=int, default=1344)
    parser.add_argument("--seconds", type=int, default=10)
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--no-two-pass", action="store_true")
    parser.add_argument("--upscale-factor", type=float, default=1.5)
    parser.add_argument("--resume-job", help="Download video for an already-completed job ID")
    parser.add_argument("--base-url", help="Override API base URL")
    parser.add_argument("--api-key", help="Override API key")
    parser.add_argument("--webhook-url", help="Webhook URL to notify on completion/download")
    args = parser.parse_args()

    if args.resume_job:
        resume_job_video(args.resume_job, Path(args.out), args.base_url, args.api_key)
        return

    if not args.images:
        print("Error: --images is required unless --resume-job is set.", file=sys.stderr)
        sys.exit(2)

    prompt_text = args.prompt
    if args.prompt_file:
        prompt_text = Path(args.prompt_file).read_text(encoding="utf-8")
    if not prompt_text:
        print("Error: --prompt or --prompt-file is required.", file=sys.stderr)
        sys.exit(2)

    generate_video(
        images=[Path(p) for p in args.images],
        prompt=prompt_text,
        out_path=Path(args.out),
        width=args.width,
        height=args.height,
        seconds=args.seconds,
        seed=args.seed,
        two_pass=not args.no_two_pass,
        upscale_factor=args.upscale_factor,
        base_url=args.base_url,
        api_key=args.api_key,
        webhook_url=args.webhook_url,
    )


if __name__ == "__main__":
    main()
