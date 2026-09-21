@echo off
setlocal
cd /d "%~dp0"
if not exist ".venv\Scripts\python.exe" (
    py -3.11 -m venv .venv
    if errorlevel 1 goto :no_python
)
.venv\Scripts\python.exe -m pip install -r requirements-build.txt
if errorlevel 1 goto :failed
.venv\Scripts\python.exe -m unittest discover -s tests -v
if errorlevel 1 goto :failed
.venv\Scripts\python.exe -m PyInstaller --clean --noconfirm Avanegar.spec
if errorlevel 1 goto :failed
copy /y README.fa.md dist\Avanegar\README.fa.md >nul
copy /y THIRD_PARTY.md dist\Avanegar\THIRD_PARTY.md >nul
echo.
echo Build complete: dist\Avanegar\Avanegar.exe
echo Keep the entire Avanegar folder together, including _internal.
echo The destination computer does NOT need Python.
pause
exit /b 0
:no_python
echo Install Python 3.11 x64 and its launcher from python.org first.
pause
exit /b 1
:failed
echo Build failed. See the error above.
pause
exit /b 1
