@echo off
chcp 65001 >nul

echo ========================================
echo  部署 StudentManagerCore
echo ========================================
echo.

:: 1. 停止应用池
echo [1/3] 停止应用池 0008 ...
appcmd stop apppool /apppool.name:0008
if %errorlevel% neq 0 (
    echo [!] 停止失败，可能已停止，继续执行...
)

:: 2. 发布
echo.
echo [2/3] 正在发布 ...
dotnet publish -o E:\wwwroot\0008_qu4cz8\web
if %errorlevel% neq 0 (
    echo [X] 发布失败，请检查错误信息
    pause
    exit /b 1
)
echo [OK] 发布成功

:: 3. 启动应用池
echo.
echo [3/3] 启动应用池 0008 ...
appcmd start apppool /apppool.name:0008
if %errorlevel% neq 0 (
    echo [!] 启动失败，请手动检查
    pause
    exit /b 1
)
echo [OK] 启动成功

echo.
echo ========================================
echo  部署完成！
echo ========================================
pause
