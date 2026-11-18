@echo off
title PK Trainer - House of Legacy - Uninstall
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -Uninstall
echo.
pause
