@echo off
rem Starts a sample host: run desktop10 | desktop48 | winforms48 [--scenario "name"] [--minimized]
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
