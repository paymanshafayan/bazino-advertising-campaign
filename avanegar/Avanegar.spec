# Build on Windows x64 with Python 3.11: python -m PyInstaller --noconfirm Avanegar.spec
# Standard PyInstaller hooks include Qt plugins, PortAudio, NumPy, SciPy and
# Requests' CA certificates. No Whisper/ONNX/CTranslate2/model assets are used.
a = Analysis(
    ["launcher.py"], pathex=[SPECPATH],
    binaries=[], datas=[], hiddenimports=[],
    hookspath=[], runtime_hooks=[],
    excludes=["torch", "tensorflow", "matplotlib", "IPython", "pytest", "faster_whisper", "ctranslate2", "onnxruntime", "tokenizers", "huggingface_hub", "av"],
    noarchive=False,
)
pyz = PYZ(a.pure)
exe = EXE(
    pyz, a.scripts, [], exclude_binaries=True,
    name="Avanegar", debug=False, bootloader_ignore_signals=False,
    strip=False, upx=False, console=False,
)
coll = COLLECT(exe, a.binaries, a.datas, strip=False, upx=False, name="Avanegar")
