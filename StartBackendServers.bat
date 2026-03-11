@echo off
setlocal

set "ROOT=%~dp0"

if "%OLLAMA_EXE%"=="" set "OLLAMA_EXE=ollama"
if "%OLLAMA_ARGS%"=="" set "OLLAMA_ARGS=serve"

if "%COEIROINK_EXE%"=="" set "COEIROINK_EXE="
if "%COEIROINK_ARGS%"=="" set "COEIROINK_ARGS="

if "%BACKEND_WORKDIR%"=="" set "BACKEND_WORKDIR=BackendServer"
if "%BACKEND_MODULE%"=="" set "BACKEND_MODULE=app.server"
if "%BACKEND_ENTRY%"=="" set "BACKEND_ENTRY=BackendServer\app\server.py"
if "%BACKEND_EXE%"=="" set "BACKEND_EXE="
if "%BACKEND_ARGS%"=="" set "BACKEND_ARGS="
if "%PYTHON_EXE%"=="" set "PYTHON_EXE=python"

rem Start Ollama
where /Q "%OLLAMA_EXE%"
if %ERRORLEVEL%==0 (
    start "" /B "%OLLAMA_EXE%" %OLLAMA_ARGS%
) else (
    if exist "%OLLAMA_EXE%" (
        start "" /B "%OLLAMA_EXE%" %OLLAMA_ARGS%
    ) else (
        echo [StartBackendServers] Ollama not found: %OLLAMA_EXE%
    )
)

rem Start COEIROINK if provided
if not "%COEIROINK_EXE%"=="" (
    if exist "%COEIROINK_EXE%" (
        start "" /B "%COEIROINK_EXE%" %COEIROINK_ARGS%
    ) else (
        where /Q "%COEIROINK_EXE%"
        if %ERRORLEVEL%==0 (
            start "" /B "%COEIROINK_EXE%" %COEIROINK_ARGS%
        ) else (
            echo [StartBackendServers] COEIROINK not found: %COEIROINK_EXE%
        )
    )
)

rem Start backend server
pushd "%BACKEND_WORKDIR%"
if not "%BACKEND_EXE%"=="" (
    start "" /B "%BACKEND_EXE%" %BACKEND_ARGS%
) else (
    if not "%BACKEND_MODULE%"=="" (
        start "" /B "%PYTHON_EXE%" -m %BACKEND_MODULE% %BACKEND_ARGS%
    ) else (
        start "" /B "%PYTHON_EXE%" "%BACKEND_ENTRY%" %BACKEND_ARGS%
    )
)
popd

endlocal
