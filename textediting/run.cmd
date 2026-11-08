@echo off
rem Starts a sample host: run desktop10 -InteractiveDesktop [--scenario "name"] [--minimized]
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
