@echo off
cd /d "%~dp0\src"
dotnet build /p:Configuration=Debug /p:Platform="Any CPU"
cd ..\BizHawk\
EmuHawk.exe --open-ext-tool-dll=GameHook.Integrations.BizHawk
cd /d "%~dp0"