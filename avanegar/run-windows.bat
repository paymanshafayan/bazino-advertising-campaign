@echo off
setlocal
cd /d "%~dp0"
if not exist ".venv\Scripts\python.exe" (
    py -3.11 -m venv .venv
    if errorlevel 1 goto :no_python
)
.venv\Scripts\python.exe -m pip install -r requirements.txt
if errorlevel 1 goto :failed
.venv\Scripts\python.exe -m avanegar
if errorlevel 1 goto :failed
exit /b 0
:no_python
echo Python 3.11 x64 is required. Install it from https://www.python.org/downloads/windows/
echo Include the Python launcher during installation.
pause
exit /b 1
:failed
echo Avanegar could not start. Check the error above and your internet connection.
pause
exit /b 1
