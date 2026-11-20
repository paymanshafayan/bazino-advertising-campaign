"""Bazino karaoke subtitles: word-by-word animated captions + BAZINO badge.

Usage:
  python scripts/karaoke_burn.py --src clean.mp4 --srt subs.srt --out out.mp4 \
      [--badge Assets/bazino-badge.png] [--badge-pos 1090:16] [--margin-v 152] [--margin-l 420]

Notes:
- --src must be the clean video (no burned-in subtitles).
- Word timing = SRT cue time split by word length (approximate). Replace with
  faster-whisper word timestamps if exact sync is needed.
- Font: Poppins Bold from Assets/fonts (approved family; Arial is banned by the guide).
"""
import argparse, re, shutil, subprocess, tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FONT_FILE = ROOT / "Assets" / "fonts" / "Poppins-Bold.ttf"
HILITE = "&HF0D322&"  # BGR of #22D3EE
WHITE = "&HFFFFFF&"


def ts(t):
    return f"{int(t // 3600)}:{int(t % 3600 // 60):02d}:{t % 60:05.2f}"


def parse_srt(p):
    txt = Path(p).read_text(encoding="utf-8-sig").replace("\r", "")
    cues = []
    for blk in re.split(r"\n\s*\n", txt.strip()):
        lines = blk.split("\n")
        a = [int(x) for x in re.match(r"(\d+):(\d+):(\d+),(\d+) --> (\d+):(\d+):(\d+),(\d+)", lines[1]).groups()]
        cues.append((a[0]*3600 + a[1]*60 + a[2] + a[3]/1000,
                     a[4]*3600 + a[5]*60 + a[6] + a[7]/1000,
                     " ".join(lines[2:]).strip()))
    return cues


def pages(words, n):
    out, cur = [], []
    for w in words:
        cur.append(w)
        if len(cur) >= n or re.search(r"[.,;:!?]$", w):
            out.append(cur); cur = []
    if cur:
        if out and len(cur) == 1 and len(out[-1]) < n + 1:
            out[-1] += cur
        else:
            out.append(cur)
    return out


def build_events(cues, per_page):
    events = []
    for st, en, text in cues:
        words = text.split()
        if not words:
            continue
        pgs = pages(words, per_page)
        weights = [sum(len(w) + 1 for w in pg) for pg in pgs]
        total, t = sum(weights), st
        for pg, wt in zip(pgs, weights):
            pdur = (en - st) * wt / total
            lw = [len(w) + 1 for w in pg]
            t0 = t
            for i, _ in enumerate(pg):
                wdur = pdur * lw[i] / sum(lw)
                parts = [(r"{\c%s\fscx112\fscy112}%s{\c%s\fscx100\fscy100}" % (HILITE, x, WHITE)) if j == i else x
                         for j, x in enumerate(pg)]
                # the word event must end with its own slot, otherwise pages overlap
                events.append((t0, t0 + wdur, " ".join(parts)))
                t0 += wdur
            t += pdur
    return events


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True)
    ap.add_argument("--srt", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--badge", default=str(ROOT / "Assets" / "bazino-badge.png"))
    ap.add_argument("--badge-pos", default="1090:16")
    ap.add_argument("--width", type=int, default=1276)
    ap.add_argument("--height", type=int, default=718)
    ap.add_argument("--font-size", type=int, default=56)
    ap.add_argument("--words-per-page", type=int, default=4)
    ap.add_argument("--margin-v", type=int, default=152)
    ap.add_argument("--margin-l", type=int, default=420)
    a = ap.parse_args()

    events = build_events(parse_srt(a.srt), a.words_per_page)
    ass = f"""[Script Info]
ScriptType: v4.00+
PlayResX: {a.width}
PlayResY: {a.height}
WrapStyle: 2

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: K,Poppins,{a.font_size},{WHITE},{WHITE},&H000000&,&H80000000,1,0,0,0,100,100,0,0,1,5,2,2,{a.margin_l},60,{a.margin_v},1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
"""
    for s, e, t in events:
        ass += f"Dialogue: 0,{ts(s)},{ts(e)},K,,0,0,0,,{t}\n"

    with tempfile.TemporaryDirectory() as tmp:
        tmp = Path(tmp)
        (tmp / "karaoke.ass").write_text(ass, encoding="utf-8")
        shutil.copy(FONT_FILE, tmp / FONT_FILE.name)
        fc = f"[0:v][1:v]overlay={a.badge_pos}[v];[v]ass=karaoke.ass:fontsdir=.[vo]"
        subprocess.run(["ffmpeg", "-y", "-v", "error", "-i", str(Path(a.src).resolve()), "-i", str(Path(a.badge).resolve()),
                        "-filter_complex", fc, "-map", "[vo]", "-map", "0:a?", "-c:v", "libx264", "-crf", "18",
                        "-preset", "medium", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k",
                        "-movflags", "+faststart", str(Path(a.out).resolve())], cwd=str(tmp), check=True)
    print("done", a.out, "events", len(events))


if __name__ == "__main__":
    main()
