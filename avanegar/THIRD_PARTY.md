# Third-party software — Avanegar 2.1

The bundle contains both the local inference runtime and the cloud API client. Model weights are NOT bundled; existing per-user model caches are reused. Missing models can be downloaded separately.

| Component | Upstream | License |
| --- | --- | --- |
| PySide6 / Qt for Python | https://doc.qt.io/qtforpython-6/ | LGPLv3 / GPLv3 / commercial; verify included Qt modules |
| sounddevice | https://github.com/spatialaudio/python-sounddevice | MIT |
| PortAudio | https://www.portaudio.com/ | MIT-style |
| NumPy | https://numpy.org/ | BSD-3-Clause |
| SciPy (audio sample-rate conversion only, not speech inference) | https://scipy.org/ | BSD-3-Clause and bundled numerical-library notices |
| faster-whisper | https://github.com/SYSTRAN/faster-whisper | MIT |
| CTranslate2 | https://github.com/OpenNMT/CTranslate2 | MIT |
| Whisper models (separate download) | https://github.com/openai/whisper | MIT; also review model cards |
| PyAV / FFmpeg | https://github.com/PyAV-Org/PyAV | BSD-3-Clause; FFmpeg build-specific licenses also apply |
| ONNX Runtime | https://github.com/microsoft/onnxruntime | MIT |
| Silero VAD | https://github.com/snakers4/silero-vad | MIT |
| Hugging Face Hub / Tokenizers | https://github.com/huggingface | Apache-2.0 |
| Requests | https://github.com/psf/requests | Apache-2.0 |
| PyInstaller (build tool) | https://pyinstaller.org/ | GPL with bootloader distribution exception |

Windows DPAPI is an operating-system service, not a bundled credential server.
OpenAI Transcription is an external paid API, governed by its service terms and data policies; it is not an included/free model license.

Preserve the actual licenses shipped by all wheels and transitive dependencies. This inventory is not an exhaustive legal audit. The directory bundle uses dynamically linked Qt libraries; consult the Qt distribution obligations before public distribution.
