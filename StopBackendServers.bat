@echo off
setlocal

if "%OLLAMA_EXE%"=="" set "OLLAMA_EXE=ollama.exe"
if "%COEIROINK_EXE%"=="" set "COEIROINK_EXE="
if "%BACKEND_ENTRY%"=="" set "BACKEND_ENTRY=BackendServer\app\server.py"
if "%BACKEND_MODULE%"=="" set "BACKEND_MODULE=app.server"
if "%BACKEND_EXE%"=="" set "BACKEND_EXE="

rem Stop Ollama
taskkill /F /IM "ollama.exe" >nul 2>&1

rem Stop COEIROINK if provided
if not "%COEIROINK_EXE%"=="" (
    for %%f in ("%COEIROINK_EXE%") do set "COEIROINK_NAME=%%~nxf"
    if not "%COEIROINK_NAME%"=="" taskkill /F /IM "%COEIROINK_NAME%" >nul 2>&1
)

rem Stop backend python by command line match
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*app\\server.py*' -or $_.CommandLine -like '*-m app.server*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }" >nul 2>&1

rem Stop backend exe if provided
if not "%BACKEND_EXE%"=="" (
    for %%f in ("%BACKEND_EXE%") do set "BACKEND_EXE_NAME=%%~nxf"
    if not "%BACKEND_EXE_NAME%"=="" taskkill /F /IM "%BACKEND_EXE_NAME%" >nul 2>&1
)

endlocal
