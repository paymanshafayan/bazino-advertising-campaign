# Build on Windows x64 with Python 3.11: python -m PyInstaller --noconfirm Avanegar.spec
from PyInstaller.utils.hooks import collect_all, copy_metadata

bundle_data = []
bundle_binaries = []
bundle_imports = []
# Include native inference DLLs, the bundled Silero VAD model, Qt plugins,
# tokenizer files and PortAudio. No speech model is bundled in the executable.
for package in ("faster_whisper", "ctranslate2", "onnxruntime", "tokenizers", "av", "sounddevice", "huggingface_hub"):
    data, binaries, hidden = collect_all(package)
    bundle_data += data
    bundle_binaries += binaries
    bundle_imports += hidden
for distribution in ("faster-whisper", "huggingface-hub", "ctranslate2", "tokenizers"):
    bundle_data += copy_metadata(distribution)

a = Analysis(
    ["launcher.py"], pathex=[SPECPATH],
    binaries=bundle_binaries, datas=bundle_data, hiddenimports=bundle_imports,
    hookspath=[], runtime_hooks=[],
    excludes=["torch", "tensorflow", "matplotlib", "IPython", "pytest"],
    noarchive=False,
)
pyz = PYZ(a.pure)
exe = EXE(
    pyz, a.scripts, [], exclude_binaries=True,
    name="Avanegar", debug=False, bootloader_ignore_signals=False,
    strip=False, upx=False, console=False,
)
coll = COLLECT(exe, a.binaries, a.datas, strip=False, upx=False, name="Avanegar")
