@echo off
setlocal

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set SWDIR=C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS

if not exist "%CSC%" (
    echo ERROR: csc.exe not found. .NET Framework 4.x is required.
    pause
    exit /b 1
)

if not exist "%SWDIR%\SolidWorks.Interop.sldworks.dll" (
    echo ERROR: SolidWorks 2025 not found at:
    echo   %SWDIR%
    pause
    exit /b 1
)

echo [1/2] Building SwAiGuide.dll ...
"%CSC%" /nologo /codepage:65001 /target:library /platform:x64 /out:SwAiGuide.dll ^
  /r:"%SWDIR%\SolidWorks.Interop.sldworks.dll" ^
  /r:"%SWDIR%\SolidWorks.Interop.swconst.dll" ^
  /r:"%SWDIR%\SolidWorks.Interop.swpublished.dll" ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  SwAiGuide.cs
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  pause
  exit /b 1
)

echo [2/2] Copying interop assemblies from SolidWorks installation ...
copy /y "%SWDIR%\SolidWorks.Interop.sldworks.dll" . >nul
copy /y "%SWDIR%\SolidWorks.Interop.swconst.dll" . >nul
copy /y "%SWDIR%\SolidWorks.Interop.swpublished.dll" . >nul

echo.
echo BUILD OK
dir /b SwAiGuide.dll
echo.
echo Run install.cmd as administrator to register the add-in.
pause
