@echo off
setlocal
set "AKKI_VCVARS="
set "AKKI_VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%AKKI_VSWHERE%" for /f "usebackq delims=" %%I in (`"%AKKI_VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find VC\Auxiliary\Build\vcvarsall.bat`) do set "AKKI_VCVARS=%%I"
if not defined AKKI_VCVARS (
  echo Visual Studio C++ tools were not found. Install the Desktop development with C++ workload.
  exit /b 1
)
call "%AKKI_VCVARS%" x64
if errorlevel 1 exit /b 1
if not exist "%~dp0..\artifacts\explorer-native" mkdir "%~dp0..\artifacts\explorer-native"
pushd "%~dp0..\artifacts\explorer-native"
cl /nologo /std:c++17 /EHsc /W4 /LD "%~dp0..\src\Akki.ExplorerCommand\ExplorerCommand.cpp" /link /OUT:Akki.ExplorerCommand.dll /EXPORT:DllCanUnloadNow,PRIVATE /EXPORT:DllGetClassObject,PRIVATE shlwapi.lib ole32.lib uuid.lib
set "AKKI_RESULT=%ERRORLEVEL%"
if not "%AKKI_RESULT%"=="0" goto done
cl /nologo /std:c++17 /EHsc /W4 "%~dp0..\tests\Akki.ExplorerCommand.Tests\ExplorerCommand.Tests.cpp" /Fe:ExplorerCommand.Tests.exe /link ole32.lib uuid.lib
set "AKKI_RESULT=%ERRORLEVEL%"
if not "%AKKI_RESULT%"=="0" goto done
ExplorerCommand.Tests.exe "%CD%\Akki.ExplorerCommand.dll"
set "AKKI_RESULT=%ERRORLEVEL%"
:done
popd
exit /b %AKKI_RESULT%
