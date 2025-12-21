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

REM Знайти NetworkCore.exe в різних можливих місцях
set SOURCE_EXE=
if exist ".\DocControlNetworkCore\bin\Debug\net8.0-windows\DocControlNetworkCore.exe" (
    set SOURCE_EXE=.\DocControlNetworkCore\bin\Debug\net8.0-windows\DocControlNetworkCore.exe
)
if exist ".\DocControlNetworkCore\bin\Release\net8.0-windows\DocControlNetworkCore.exe" (
    set SOURCE_EXE=.\DocControlNetworkCore\bin\Release\net8.0-windows\DocControlNetworkCore.exe
)

if "%SOURCE_EXE%"=="" (
    echo ПОМИЛКА: Не знайдено DocControlNetworkCore.exe
    echo.
    echo Збери проект DocControlNetworkCore в Visual Studio:
    echo 1. Відкрий DocControlSolution.sln
    echo 2. Правий клік на DocControlNetworkCore -^> Build
    echo 3. Або Build -^> Build Solution
    echo.
    pause
    exit /b 1
)

echo Знайдено: %SOURCE_EXE%

REM Копіюємо всі файли з папки bin рекурсивно
for %%F in ("%SOURCE_EXE%") do set SOURCE_DIR=%%~dpF
echo Копіювання файлів з %SOURCE_DIR%...
xcopy /E /I /Y /Q "%SOURCE_DIR%*" "%TEST_DIR%\" > nul
echo Скопійовано NetworkCore.exe та всі залежності (включаючи DLL)

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
echo ВАЖЛИВО: NetworkCore потребує прав адміністратора
echo Якщо з'явиться запит UAC - дозволь запуск
echo.

cd /d "%TEST_DIR%"
start "TestNode NetworkCore" DocControlNetworkCore.exe --debug

echo.
echo Тестовий вузол запущено в окремому вікні
echo Перевір вікно "TestNode NetworkCore"
echo.
pause
