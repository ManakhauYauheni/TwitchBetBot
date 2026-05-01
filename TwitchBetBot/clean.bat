:: TwitchBetBot - Clean WebView2 Cache
:: This script removes temporary files while keeping user data (cookies, sessions)
:: Скрипт удаляет временные файлы, сохраняя пользовательские данные (куки, сессии)
:: При закрытии программы запускается автоматически
@echo off
timeout /t 2 /nobreak > nul

set "path=%~dp0TwitchBetBot.exe.WebView2\EBWebView"

if exist "%path%" (
    for /d %%i in ("%path%\*") do (
        if /i not "%%~nxi"=="Default" (
            rmdir /s /q "%%i" 2>nul
        )
    )
    for %%i in ("%path%\*") do (
        if /i not "%%~nxi"=="Local State" (
            del /f /q "%%i" 2>nul
        )
    )
    
    if exist "%path%\Default" (
        for /d %%i in ("%path%\Default\*") do (
            if /i not "%%~nxi"=="Network" (
                rmdir /s /q "%%i" 2>nul
            )
        )
        for %%i in ("%path%\Default\*") do (
            del /f /q "%%i" 2>nul
        )
    )
)

2>nul