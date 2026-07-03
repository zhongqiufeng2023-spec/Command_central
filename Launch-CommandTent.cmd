@echo off
REM 中军帐 启动器:用便携 .NET 8 SDK 启动 Godot 并打开本项目。
REM 原因:系统 C:\Program Files\dotnet 只有运行时无 SDK,必须让便携 ~/.dotnet 在 PATH 最前。
set "DOTNET_ROOT=C:\Users\zhong\.dotnet"
set "PATH=C:\Users\zhong\.dotnet;%PATH%"
start "" "C:\Users\zhong\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe" --path "D:\git\Central_Command_Post\godot" --editor
