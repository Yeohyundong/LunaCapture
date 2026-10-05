@echo off
rem LunaCapture release build. The real work is in publish.ps1 (PowerShell handles Korean text better).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1"
