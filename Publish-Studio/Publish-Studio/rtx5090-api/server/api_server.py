import hashlib
import json
import os
import random
import shutil
import subprocess
import threading
import time
import urllib.error
import urllib.request
import uuid
from pathlib import Path
from typing import List, Optional

from fastapi import Depends, FastAPI, File, Form, Header, HTTPException, Request, UploadFile
from fastapi.responses import FileResponse, JSONResponse
from PIL import Image

FREEVIDEO_ROOT = Path(os.environ.get("FREEVIDEO_ROOT", "/root/FreeVideo"))
FREEVIDEO_PYTHON = FREEVIDEO_ROOT / "envs" / "unified" / "bin" / "python"
JOBS_ROOT = Path(os.environ.get("FREEVIDEO_JOBS_ROOT", "/root/freevideo-api-jobs"))
API_KEY = os.environ.get("FREEVIDEO_API_KEY", "").strip()
MAX_IMAGES = 6
JOB_RETENTION_SECONDS = int(os.environ.get("FREEVIDEO_JOB_RETENTION", "36000"))

JOBS_ROOT.mkdir(parents=True, exist_ok=True)

app = FastAPI(title="FreeVideo RTX 5090 API", version="1.1.0")

gpu_lock = threading.Lock()
current_job_id: Optional[str] = None


def verify_api_key(
    x_api_key: Optional[str] = Header(default=None),
    authorization: Optional[str] = Header(default=None),
):
    if not API_KEY:
        return
    token = x_api_key
    if not token and authorization and authorization.lower().startswith("bearer "):
        token = authorization[7:].strip()
    if token != API_KEY:
        raise HTTPException(status_code=401, detail="Invalid or missing API key")


def prune_old_jobs() -> None:
    now = time.time()
    for entry in JOBS_ROOT.iterdir():
        if not entry.is_dir():
            continue
        try:
            if now - entry.stat().st_mtime > JOB_RETENTION_SECONDS:
                shutil.rmtree(entry, ignore_errors=True)
        except Exception:
            pass


def build_ref2va_prompt(raw_prompt: str, num_images: int) -> str:
    text = raw_prompt.strip()
    if "subject_definitions:" in text and "detailed_description:" in text:
        return text

    subject_lines = []
    retention_lines = []
    tags = []
    for i in range(1, num_images + 1):
        tag = f"<Subject {i}>"
        pic = f"<Picture {i}>"
        tags.append(tag)
        subject_lines.append(
            f"- {tag} (S{i}): Visual reference from {pic}. Maintain strict visual consistency with {pic}."
        )
        retention_lines.append(
            f"- {tag} (S{i}): fully_preserved across all shots."
        )

    subjects_str = ", ".join(tags)
    return (
        "subject_definitions:\n"
        + "\n".join(subject_lines)
        + "\n\n"
        + "summary:\n"
        + f"[reference generation] A cinematic 9:16 vertical video featuring {subjects_str}. {text}\n\n"
        + "retention_analysis:\n"
        + "\n".join(retention_lines)
        + "\n\n"
        + "detailed_description:\n"
        + f"[Shot 1] {text} No subtitles, no on-screen text, no watermarks.\n\n"
        + "overall_soundscape:\n"
        + "Natural, realistic ambient audio matching the scene environment and character actions.\n\n"
        + "non_diegetic_music:\n"
        + "N/A\n"
    )


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        while True:
            chunk = f.read(1024 * 1024)
            if not chunk:
                break
            h.update(chunk)
    return h.hexdigest()


def fire_webhook(webhook_url: Optional[str], payload: dict) -> None:
    if not webhook_url or not webhook_url.strip():
        return
    try:
        body = json.dumps(payload).encode("utf-8")
        headers = {"Content-Type": "application/json"}
        if API_KEY:
            headers["X-API-Key"] = API_KEY
        req = urllib.request.Request(webhook_url.strip(), data=body, headers=headers, method="POST")
        with urllib.request.urlopen(req, timeout=10) as resp:
            resp.read()
    except Exception:
        pass


def run_generation_job(
    job_id: str,
    job_dir: Path,
    prompt_text: str,
    num_images: int,
    width: int,
    height: int,
    seconds: int,
    seed: int,
    two_pass: bool,
    upscale_factor: float,
    webhook_url: Optional[str] = None,
) -> dict:
    global current_job_id
    refs_dir = job_dir / "refs"
    out_dir = job_dir / "out"
    out_dir.mkdir(parents=True, exist_ok=True)
    status_file = job_dir / "status.json"
    out_mp4 = out_dir / "video.mp4"

    try:
        references = []
        for idx in range(1, num_images + 1):
            raw_path = refs_dir / f"raw_{idx}"
            ref_path = refs_dir / f"ref_{idx}.jpg"
            with Image.open(raw_path) as img:
                img.load()
                img.convert("RGB").save(ref_path, format="JPEG", quality=92)
            raw_path.unlink(missing_ok=True)
            references.append({"path": f"refs/ref_{idx}.jpg", "kind": "image"})

        media_json = {
            "version": 1,
            "first": None,
            "last": None,
            "references": references,
            "loras": [],
        }
        media_path = job_dir / "media.json"
        media_path.write_text(json.dumps(media_json, indent=2), encoding="utf-8")

        formatted_prompt = build_ref2va_prompt(prompt_text, len(references))
        prompt_path = job_dir / "prompt.txt"
        prompt_path.write_text(formatted_prompt, encoding="utf-8")

        actual_seed = seed if seed > 0 else random.randint(100000, 99999999)
        status_file.write_text(
            json.dumps({"job_id": job_id, "status": "running", "started_at": time.time()}),
            encoding="utf-8",
        )

        cmd = [
            str(FREEVIDEO_PYTHON),
            "-B",
            "-m",
            "freevideo_engine.managed",
            "--root",
            str(FREEVIDEO_ROOT),
            "generate",
            "--prompt-file",
            str(prompt_path),
            "--media",
            str(media_path),
            "--task",
            "ref2va",
            "--out",
            str(out_mp4),
            "--width",
            str(width),
            "--height",
            str(height),
            "--seconds",
            str(seconds),
            "--seed",
            str(actual_seed),
        ]
        if two_pass:
            cmd.extend(["--two-pass", "--upscale-factor", str(upscale_factor)])

        env = os.environ.copy()
        env["PYTHONPATH"] = str(FREEVIDEO_ROOT)

        t0 = time.time()
        log_path = job_dir / "generate.log"
        with log_path.open("w", encoding="utf-8") as log_f:
            proc = subprocess.run(
                cmd,
                cwd=str(job_dir),
                env=env,
                stdin=subprocess.DEVNULL,
                stdout=log_f,
                stderr=subprocess.STDOUT,
                timeout=1200,
            )
        elapsed = round(time.time() - t0, 2)

        if proc.returncode != 0 or not out_mp4.is_file() or out_mp4.stat().st_size == 0:
            tail = ""
            if log_path.is_file():
                tail = log_path.read_text(encoding="utf-8", errors="replace")[-2000:]
            res = {"job_id": job_id, "status": "failed", "returncode": proc.returncode, "tail": tail}
            status_file.write_text(json.dumps(res), encoding="utf-8")
            fire_webhook(webhook_url, {"event": "video.failed", **res})
            return res

        digest = sha256_file(out_mp4)
        shutil.rmtree(refs_dir, ignore_errors=True)
        for item in out_dir.iterdir():
            if item.name != "video.mp4":
                if item.is_dir():
                    shutil.rmtree(item, ignore_errors=True)
                else:
                    item.unlink(missing_ok=True)
        res = {
            "job_id": job_id,
            "status": "completed",
            "elapsed_seconds": elapsed,
            "size_bytes": out_mp4.stat().st_size,
            "sha256": digest,
            "seed": actual_seed,
        }
        status_file.write_text(json.dumps(res), encoding="utf-8")
        fire_webhook(
            webhook_url,
            {
                "event": "video.completed",
                "download_path": f"/jobs/{job_id}/video",
                **res,
            },
        )
        return res
    except subprocess.TimeoutExpired:
        res = {"job_id": job_id, "status": "timeout"}
        status_file.write_text(json.dumps(res), encoding="utf-8")
        fire_webhook(webhook_url, {"event": "video.timeout", **res})
        return res
    except Exception as exc:
        res = {"job_id": job_id, "status": "failed", "error": str(exc)}
        status_file.write_text(json.dumps(res), encoding="utf-8")
        fire_webhook(webhook_url, {"event": "video.failed", **res})
        return res
    finally:
        current_job_id = None
        gpu_lock.release()


@app.get("/health", dependencies=[Depends(verify_api_key)])
def health():
    gpu_info = "unknown"
    try:
        r = subprocess.run(
            [
                "nvidia-smi",
                "--query-gpu=name,memory.used,memory.total,utilization.gpu",
                "--format=csv,noheader",
            ],
            stdin=subprocess.DEVNULL,
            capture_output=True,
            text=True,
            timeout=5,
        )
        if r.returncode == 0:
            gpu_info = r.stdout.strip()
    except Exception as exc:
        gpu_info = f"error: {exc}"

    return {
        "status": "ok",
        "busy": gpu_lock.locked(),
        "current_job": current_job_id,
        "gpu": gpu_info,
    }


@app.post("/generate", dependencies=[Depends(verify_api_key)])
def generate_video(
    images: List[UploadFile] = File(..., description="1 to 6 reference images"),
    prompt: str = Form(..., description="Ref2VA 6-section prompt or plain text prompt"),
    width: int = Form(768),
    height: int = Form(1344),
    seconds: int = Form(10),
    seed: int = Form(0),
    two_pass: bool = Form(True),
    upscale_factor: float = Form(1.5),
    webhook_url: Optional[str] = Form(default=None),
):
    global current_job_id

    if not images or len(images) > MAX_IMAGES:
        raise HTTPException(status_code=400, detail=f"Must provide 1..{MAX_IMAGES} images.")
    if not prompt or not prompt.strip():
        raise HTTPException(status_code=400, detail="Prompt must not be empty.")
    if seconds < 1 or seconds > 20:
        raise HTTPException(status_code=400, detail="seconds must be between 1 and 20.")

    if not gpu_lock.acquire(blocking=False):
        raise HTTPException(
            status_code=409,
            detail=f"GPU is currently busy with job {current_job_id}. Try again shortly.",
        )

    job_id = time.strftime("%Y%m%d_%H%M%S_") + uuid.uuid4().hex[:8]
    current_job_id = job_id
    job_dir = JOBS_ROOT / job_id
    refs_dir = job_dir / "refs"
    refs_dir.mkdir(parents=True, exist_ok=True)
    prune_old_jobs()

    for idx, upload in enumerate(images, start=1):
        raw_path = refs_dir / f"raw_{idx}"
        with raw_path.open("wb") as f:
            shutil.copyfileobj(upload.file, f)

    res = run_generation_job(
        job_id, job_dir, prompt, len(images), width, height, seconds, seed, two_pass, upscale_factor, webhook_url
    )
    if res.get("status") != "completed":
        raise HTTPException(status_code=500, detail=res)

    out_mp4 = job_dir / "out" / "video.mp4"
    return FileResponse(
        path=str(out_mp4),
        media_type="video/mp4",
        filename=f"freevideo_{job_id}.mp4",
        headers={
            "X-Job-Id": job_id,
            "X-File-SHA256": res["sha256"],
            "X-Generation-Seconds": str(res["elapsed_seconds"]),
            "X-Seed": str(res["seed"]),
        },
    )


@app.post("/jobs/create", dependencies=[Depends(verify_api_key)])
def create_job():
    prune_old_jobs()
    job_id = time.strftime("%Y%m%d_%H%M%S_") + uuid.uuid4().hex[:8]
    job_dir = JOBS_ROOT / job_id
    (job_dir / "refs").mkdir(parents=True, exist_ok=True)
    (job_dir / "out").mkdir(parents=True, exist_ok=True)
    (job_dir / "status.json").write_text(
        json.dumps({"job_id": job_id, "status": "created"}), encoding="utf-8"
    )
    return {"job_id": job_id}


@app.post("/jobs/{job_id}/chunk", dependencies=[Depends(verify_api_key)])
async def upload_chunk(job_id: str, name: str, request: Request, offset: Optional[int] = None):
    job_dir = JOBS_ROOT / job_id
    if not job_dir.is_dir():
        raise HTTPException(status_code=404, detail="Job not found")
    safe_name = os.path.basename(name)
    target = (job_dir / "refs" / safe_name) if safe_name.startswith("raw_") else (job_dir / safe_name)
    body = await request.body()
    mode = "r+b" if (offset is not None and target.exists()) else ("wb" if offset == 0 else "ab")
    with target.open(mode) as f:
        if offset is not None:
            f.seek(offset)
        f.write(body)
    return {"ok": True, "bytes_written": len(body), "offset": offset}


@app.post("/jobs/{job_id}/start", dependencies=[Depends(verify_api_key)])
def start_job(
    job_id: str,
    num_images: int = Form(...),
    width: int = Form(768),
    height: int = Form(1344),
    seconds: int = Form(10),
    seed: int = Form(0),
    two_pass: bool = Form(True),
    upscale_factor: float = Form(1.5),
    webhook_url: Optional[str] = Form(default=None),
):
    global current_job_id
    job_dir = JOBS_ROOT / job_id
    if not job_dir.is_dir():
        raise HTTPException(status_code=404, detail="Job not found")
    if not 1 <= num_images <= MAX_IMAGES:
        raise HTTPException(status_code=400, detail=f"num_images must be 1..{MAX_IMAGES}")
    prompt_upload = job_dir / "prompt_raw.txt"
    if not prompt_upload.is_file():
        raise HTTPException(status_code=400, detail="Missing prompt_raw.txt")
    prompt_text = prompt_upload.read_text(encoding="utf-8")

    if not gpu_lock.acquire(blocking=False):
        raise HTTPException(
            status_code=409,
            detail=f"GPU is currently busy with job {current_job_id}. Try again shortly.",
        )
    current_job_id = job_id
    t = threading.Thread(
        target=run_generation_job,
        args=(job_id, job_dir, prompt_text, num_images, width, height, seconds, seed, two_pass, upscale_factor, webhook_url),
        daemon=True,
    )
    t.start()
    return {"job_id": job_id, "status": "running"}


@app.get("/jobs/{job_id}/status", dependencies=[Depends(verify_api_key)])
def job_status(job_id: str):
    job_dir = JOBS_ROOT / job_id
    if not job_dir.is_dir():
        raise HTTPException(status_code=404, detail="Job not found")
    status_file = job_dir / "status.json"
    data = {"job_id": job_id, "status": "unknown"}
    if status_file.is_file():
        data = json.loads(status_file.read_text(encoding="utf-8"))
    engine_logs = list((job_dir / "out").glob("*.engine.log"))
    if engine_logs and data.get("status") == "running":
        try:
            lines = engine_logs[0].read_text(encoding="utf-8", errors="replace").splitlines()
            data["engine_tail"] = "\n".join(lines[-5:])
        except Exception:
            pass
    return JSONResponse(data)


@app.get("/jobs/{job_id}/video", dependencies=[Depends(verify_api_key)])
def download_job_video(job_id: str):
    out_mp4 = JOBS_ROOT / job_id / "out" / "video.mp4"
    if not out_mp4.is_file():
        raise HTTPException(status_code=404, detail="Video not found for this job")
    digest = sha256_file(out_mp4)
    return FileResponse(
        path=str(out_mp4),
        media_type="video/mp4",
        filename=f"freevideo_{job_id}.mp4",
        headers={
            "X-Job-Id": job_id,
            "X-File-SHA256": digest,
        },
    )
