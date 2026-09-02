@echo off
title Otimizador de PC
color 0A

echo ==========================================
echo        OTIMIZADOR DE PC
echo ==========================================
echo.
echo Executando PowerShell...
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -NoExit -File "%~dp0Otimizador_de_PC.ps1"

echo.
echo ==========================================
echo PowerShell foi encerrado.
echo ==========================================
echo.
pause
