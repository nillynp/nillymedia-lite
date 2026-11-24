@echo off
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall_NillyMediaLite.ps1" %*
