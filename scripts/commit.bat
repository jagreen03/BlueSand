@echo off
rem Usage: scripts\commit.bat "feat: message" -all -push -runscan
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0commit.ps1" -Message %*
