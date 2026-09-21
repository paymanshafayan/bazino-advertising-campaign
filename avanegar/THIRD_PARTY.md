# Third-party software

Avanegar uses the following independent projects. Their names and copyrights remain with their owners. This file is an inventory, not a replacement for the full licenses. Preserve dependency license files in the distributed bundle and verify obligations before public distribution.

| Component | Upstream | License information |
| --- | --- | --- |
| PySide6 / Qt for Python | https://doc.qt.io/qtforpython-6/ | LGPLv3 / GPLv3 / commercial; verify included Qt modules |
| faster-whisper | https://github.com/SYSTRAN/faster-whisper | MIT |
| CTranslate2 | https://github.com/OpenNMT/CTranslate2 | MIT |
| Whisper models | https://github.com/openai/whisper | MIT; also review downloaded model cards |
| sounddevice | https://github.com/spatialaudio/python-sounddevice | MIT |
| PortAudio | https://www.portaudio.com/ | MIT-style |
| NumPy | https://numpy.org/ | BSD-3-Clause |
| PyAV | https://github.com/PyAV-Org/PyAV | BSD-3-Clause; FFmpeg libraries carry their own licenses |
| ONNX Runtime | https://github.com/microsoft/onnxruntime | MIT |
| Silero VAD model (faster-whisper data) | https://github.com/snakers4/silero-vad | MIT |
| Hugging Face Hub / Tokenizers | https://github.com/huggingface | Apache-2.0 |
| Requests | https://github.com/psf/requests | Apache-2.0 |
| PyInstaller (build tool) | https://pyinstaller.org/ | GPL with bootloader distribution exception |

The build uses a directory bundle with dynamically linked Qt libraries, rather than embedding everything in a single executable. Consult the Qt licensing documentation and the licenses shipped by the wheels, including FFmpeg's build-specific licensing. Further transitive dependencies are listed by `python -m pip freeze`; this is not an exhaustive legal audit.
