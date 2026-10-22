@echo off
setlocal

set GUID={8C2F1A70-4B4D-4E52-9C1E-3A6E1B2D4F01}
set DLL=%~dp0SwAiGuide.dll

net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo Uninstalling Model Inspector...
reg delete "HKLM\SOFTWARE\SolidWorks\AddIns\%GUID%" /f >nul 2>&1
if exist "%DLL%" (
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\regasm.exe" /unregister "%DLL%" >nul 2>&1
)
echo Done. Restart SolidWorks to take effect.
pause
