@echo off
REM Скрипт для запуску тестового вузла NetworkCore на тому ж комп'ютері

echo ========================================
echo  Запуск тестового вузла NetworkCore
echo ========================================
echo.

REM Створити папку для тестового вузла
set TEST_DIR=C:\TestNode
if not exist "%TEST_DIR%" mkdir "%TEST_DIR%"
echo Папка: %TEST_DIR%

REM Створити SharedFiles для тестового вузла
if not exist "%TEST_DIR%\SharedFiles" mkdir "%TEST_DIR%\SharedFiles"
echo Створено: %TEST_DIR%\SharedFiles

REM Створити тестові файли
echo Тестовий файл 1 > "%TEST_DIR%\SharedFiles\test1.txt"
echo Тестовий файл 2 > "%TEST_DIR%\SharedFiles\test2.txt"
echo Створено тестові файли

REM Скопіювати NetworkCore.exe
set SOURCE_EXE=.\DocControlNetworkCore\bin\Debug\net8.0-windows\DocControlNetworkCore.exe
if exist "%SOURCE_EXE%" (
    copy /Y "%SOURCE_EXE%" "%TEST_DIR%\" > nul
    echo Скопійовано NetworkCore.exe
) else (
    echo ПОМИЛКА: Не знайдено %SOURCE_EXE%
    echo Спочатку збери проект!
    pause
    exit /b 1
)

REM Створити network_identity.json з іншими портами
echo { > "%TEST_DIR%\network_identity.json"
echo   "InstanceId": "22222222-2222-2222-2222-222222222222", >> "%TEST_DIR%\network_identity.json"
echo   "UserName": "TestNode", >> "%TEST_DIR%\network_identity.json"
echo   "MachineName": "TEST-PC", >> "%TEST_DIR%\network_identity.json"
echo   "IpAddress": "127.0.0.1", >> "%TEST_DIR%\network_identity.json"
echo   "TcpPort": 8001, >> "%TEST_DIR%\network_identity.json"
echo   "UdpPort": 9001 >> "%TEST_DIR%\network_identity.json"
echo } >> "%TEST_DIR%\network_identity.json"
echo Створено network_identity.json

echo.
echo ========================================
echo  Запуск тестового вузла...
echo  TCP: 8001, UDP: 9001
echo ========================================
echo.

cd /d "%TEST_DIR%"
DocControlNetworkCore.exe

pause
