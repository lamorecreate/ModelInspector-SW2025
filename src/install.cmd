@echo off
setlocal

set GUID={8C2F1A70-4B4D-4E52-9C1E-3A6E1B2D4F01}
set DLL=%~dp0SwAiGuide.dll
set SWDIR=C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator rights...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo ============================================
echo  SolidWorks Model Inspector - Install
echo ============================================
echo.

if not exist "%DLL%" (
    echo ERROR: SwAiGuide.dll not found next to this script.
    pause
    exit /b 1
)

if not exist "%SWDIR%\SLDWORKS.exe" (
    echo ERROR: SolidWorks 2025 not found at:
    echo   %SWDIR%
    echo This add-in requires SolidWorks 2025.
    pause
    exit /b 1
)

tasklist /fi "imagename eq SLDWORKS.exe" 2>nul | find /i "SLDWORKS.exe" >nul
if %errorlevel% equ 0 (
    echo WARNING: SolidWorks is running. Close it completely, then run this again.
    echo.
    pause
    exit /b 1
)

echo [1/4] Copying interop assemblies from your SolidWorks installation...
for %%F in (SolidWorks.Interop.sldworks.dll SolidWorks.Interop.swconst.dll SolidWorks.Interop.swpublished.dll) do (
    if not exist "%~dp0%%F" (
        copy /y "%SWDIR%\%%F" "%~dp0" >nul
        if errorlevel 1 (
            echo ERROR: failed to copy %%F
            pause
            exit /b 1
        )
        echo    copied %%F
    ) else (
        echo    %%F already present
    )
)

echo.
echo [2/4] Registering COM component...
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\regasm.exe" /codebase "%DLL%"
if errorlevel 1 (
    echo ERROR: regasm failed.
    pause
    exit /b 1
)

echo.
echo [3/4] Writing SolidWorks add-in registry keys...
reg add "HKLM\SOFTWARE\SolidWorks\AddIns\%GUID%" /ve /t REG_DWORD /d 1 /f >nul
reg add "HKLM\SOFTWARE\SolidWorks\AddIns\%GUID%" /v Title /t REG_SZ /d "Model Inspector" /f >nul
reg add "HKLM\SOFTWARE\SolidWorks\AddIns\%GUID%" /v Description /t REG_SZ /d "Batch model health check" /f >nul
if errorlevel 1 (
    echo ERROR: registry write failed.
    pause
    exit /b 1
)

echo [4/4] Verifying...
reg query "HKLM\SOFTWARE\SolidWorks\AddIns\%GUID%" >nul 2>&1
if errorlevel 1 (
    echo ERROR: verification failed.
    pause
    exit /b 1
)

echo.
echo ============================================
echo  INSTALL OK
echo ============================================
echo.
echo  Next:
echo   1. Start SolidWorks 2025
echo   2. Tools - Add-Ins - check "Model Inspector"
echo   3. A window appears; scan the current document
echo      or a whole folder.
echo.
echo  Log: %%LOCALAPPDATA%%\SwAiGuide\addin.log
echo.
pause
