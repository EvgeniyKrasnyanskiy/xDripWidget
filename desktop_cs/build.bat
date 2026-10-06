@echo off
setlocal
echo ========================================================
echo Compiling xDripWidget (C# .NET Edition) via MSBuild...
echo ========================================================

set MSBUILD="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"

if not exist %MSBUILD% (
    echo Error: MSBuild.exe not found at %MSBUILD%
    exit /b 1
)

cd /d "%~dp0"

%MSBUILD% xDripWidget.csproj /p:Configuration=Release /t:Rebuild /v:m /nologo
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Build FAILED!
    exit /b %ERRORLEVEL%
)

if not exist "..\dist" (
    mkdir "..\dist"
)

taskkill /F /IM xDripWidget-CS.exe 2>nul
timeout /t 1 /nobreak >nul
copy /Y "bin\Release\xDripWidget-CS.exe" "..\dist\xDripWidget-CS.exe" >nul
echo.
echo ========================================================
echo SUCCESS! Executable built:
echo ..\dist\xDripWidget-CS.exe
for %%I in ("..\dist\xDripWidget-CS.exe") do echo Size: %%~zI bytes (~%%~zI / 1024 KB)
echo ========================================================
