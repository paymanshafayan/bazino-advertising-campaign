import os
import sys
import subprocess

CLIP1 = r"C:\Jinus-Media\viral01_paralysis_flow_raw.mp4"
CLIP2 = r"C:\Jinus-Media\viral01_paralysis_scene2.mp4"
AUDIO = r"C:\Jinus-Media\viral01_mona_voiceover_final.mp3"
OUT_PACK = r"C:\projects\Antigravity Bazino\Publish-Studio\ig-reel\viral-01-paralysis-pack\viral01_paralysis_reel.mp4"
OUT_MEDIA = r"C:\Jinus-Media\viral01_paralysis_reel_final.mp4"

# We will cut clip1 to 7s and clip2 to 10s (total 17s), concat them, add audio, and encode to 1080x1920 9:16
filter_complex = (
    "[0:v]trim=start=0:end=7,setpts=PTS-STARTPTS,scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920[v0];"
    "[1:v]trim=start=0:end=10,setpts=PTS-STARTPTS,scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920[v1];"
    "[v0][v1]concat=n=2:v=1:a=0[vconcat]"
)

cmd = [
    "ffmpeg", "-y",
    "-i", CLIP1,
    "-i", CLIP2,
    "-i", AUDIO,
    "-filter_complex", filter_complex,
    "-map", "[vconcat]",
    "-map", "2:a",
    "-c:v", "libx264",
    "-preset", "fast",
    "-crf", "19",
    "-c:a", "aac",
    "-b:a", "192k",
    "-t", "17",
    "-pix_fmt", "yuv420p",
    "-movflags", "+faststart",
    OUT_PACK
]

print("Running ffmpeg command...")
res = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
if res.returncode != 0:
    print("FFmpeg error:", res.stderr)
else:
    print(f"SUCCESS! Rendered: {OUT_PACK} ({os.path.getsize(OUT_PACK):,} bytes)")
    import shutil
    shutil.copy2(OUT_PACK, OUT_MEDIA)
    print(f"Copied to media: {OUT_MEDIA} ({os.path.getsize(OUT_MEDIA):,} bytes)")
