"""PyInstaller entry point (absolute imports also work in the frozen bundle)."""
from avanegar.__main__ import main

if __name__ == "__main__":
    raise SystemExit(main())
