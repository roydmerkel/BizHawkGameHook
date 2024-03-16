@echo off
cd /d "%~dp0\src"
dotnet build /p:Configuration=Debug /p:Platform="Any CPU"
cd ..\BizHawk\
EmuHawk.exe --open-ext-tool-dll=BizHawkGameHook
cd /d "%~dp0"