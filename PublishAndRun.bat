@echo off
setlocal
chcp 65001 >nul

rem Get the directory where this .BAT resides (strip trailing backslash)
set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

rem The build output lives under the Forms project, NOT the repo root
set "RELEASE_DIR=%ROOT%\ModularAudience.Forms\bin\Release"
set "PUBLISH_DIR=%RELEASE_DIR%\net10.0-windows\win-x64"
set "EXE_PATH=%PUBLISH_DIR%\ModularAudience.Forms.exe"
set "LNK_PATH=%ROOT%\ModularAudience.lnk"

rem === Step 1: Wipe the entire bin\Release directory recursively ===
echo.
echo ==========================================
echo Wiping %RELEASE_DIR% (recursive)...
echo ==========================================

if exist "%RELEASE_DIR%" (
    rmdir /s /q "%RELEASE_DIR%" 2>nul
)

rem Verify everything is gone - abort if not (e.g. locked by a running process)
if exist "%RELEASE_DIR%" (
    echo.
    echo ERROR: %RELEASE_DIR% still exists after the delete attempt!
    echo Files may be locked by a running process.
    echo Check the Task Manager and try again.
    pause
    exit /b 1
)

echo.
echo OK: bin\Release directory fully wiped.
echo ==========================================

rem === Step 2: Publish (Release, self-contained, ready-to-run, NOT single-file) ===
echo.
echo ==========================================
echo Publishing ModularAudience.Forms (Release, self-contained, ready-to-run)...
echo ==========================================

rem -o points the output directly at the win-x64 folder, so NO "publish"
rem    subfolder is created and there are no duplicate binaries.
rem -p:PublishSingleFile=false  -> not single-file
rem -p:PublishReadyToRun=true  -> ready-to-run
dotnet publish "%ROOT%\ModularAudience.Forms\ModularAudience.Forms.csproj" ^
    -c Release ^
    --self-contained true ^
    -r win-x64 ^
    -o "%PUBLISH_DIR%" ^
    -p:PublishSingleFile=false ^
    -p:PublishReadyToRun=true

if errorlevel 1 (
    echo.
    echo ERROR: Publish failed!
    pause
    exit /b 1
)

echo.
echo ==========================================
echo Publish completed successfully!
echo ==========================================

rem === Step 3: Create a shortcut to the EXE in the repo root ===
echo.
echo ==========================================
echo Creating shortcut in repo root...
echo ==========================================

if not exist "%EXE_PATH%" (
    echo.
    echo WARNING: Could not find %EXE_PATH%
    echo Please check the output directory manually.
    pause
    exit /b 1
)

rem Remove any stale shortcut first
if exist "%LNK_PATH%" del /f /q "%LNK_PATH%" 2>nul

powershell -NoProfile -Command ^
    "$ws = New-Object -ComObject WScript.Shell; " ^
    "$s = $ws.CreateShortcut('%LNK_PATH%'); " ^
    "$s.TargetPath = '%EXE_PATH%'; " ^
    "$s.WorkingDirectory = '%PUBLISH_DIR%'; " ^
    "$s.Description = 'ModularAudience.Forms (Release, self-contained, ready-to-run)'; " ^
    "$s.Save()"

if errorlevel 1 (
    echo.
    echo WARNING: Could not create the shortcut.
    pause
    exit /b 1
)

echo.
echo OK: Shortcut created at %LNK_PATH%
echo ==========================================

rem === Step 4: Run the app ===
echo.
echo ==========================================
echo Starting ModularAudience.Forms.exe...
echo ==========================================
start "" "%EXE_PATH%"

echo.
echo Done.
endlocal