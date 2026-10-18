@echo off
chcp 65001 >nul
title SCADA 一键启动
setlocal EnableDelayedExpansion

REM ---- 子窗口分支（勿删）----
if "%~1"=="--backend" goto :run_backend
if "%~1"=="--frontend" goto :run_frontend

set "HEALTH_URL=http://localhost:5555/swagger/v1/swagger.json"

echo.
echo ============================================
echo           SCADA 一键启动
echo ============================================
echo.

REM ---- 依赖检查 ----
where dotnet >nul 2>nul
if errorlevel 1 (
    echo [错误] 未检测到 dotnet，请先安装 .NET 8 SDK
    echo         下载地址: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)
where node >nul 2>nul
if errorlevel 1 (
    echo [错误] 未检测到 node，请先安装 Node.js
    echo         下载地址: https://nodejs.org
    pause
    exit /b 1
)
where npm >nul 2>nul
if errorlevel 1 (
    echo [错误] 未检测到 npm
    pause
    exit /b 1
)

REM ---- 敏感配置检查：后端启动强依赖以下环境变量（appsettings 内为占位/空值）----
if not defined SystemDbConfig__Password (
    echo [错误] 未检测到环境变量 SystemDbConfig__Password（MySQL 数据库密码）
    echo        请先设置后再启动，例如: set "SystemDbConfig__Password=你的数据库密码"
    pause
    exit /b 1
)
if not defined Jwt__Key (
    echo [错误] 未检测到环境变量 Jwt__Key（JWT 签名密钥，长度不少于 32 字符）
    echo        请先设置后再启动，例如: set "Jwt__Key=至少32字符的随机密钥"
    pause
    exit /b 1
)

REM ---- 0. 端口检测（占用则提示确认后杀掉）----
echo [0/3] 检测端口占用 ...
call :check_port 5555 dotnet.exe
if errorlevel 1 exit /b 1
call :check_port 3333 node.exe
if errorlevel 1 exit /b 1
echo [0/3] 端口检测完成，均可用。
echo.

REM ---- 1. 启动后端（独立窗口，先 dotnet clean 清理再运行）----
echo [1/3] 启动后端：先清理项目，再 dotnet run ...
start "SCADA-Backend" cmd /k ""%~f0" --backend"

REM ---- 2. 等待后端就绪（最多 120 秒）----
echo [2/3] 等待后端就绪: %HEALTH_URL%
set /a tries=0
:wait_loop
set /a tries+=1
if %tries% gtr 60 goto :wait_timeout
curl -s -f -o nul --connect-timeout 2 "%HEALTH_URL%"
if not errorlevel 1 goto :backend_ready
timeout /t 2 /nobreak >nul
goto :wait_loop

:wait_timeout
echo [警告] 后端 120 秒内未就绪，仍继续启动前端（请留意后端窗口报错）。
goto :after_wait

:backend_ready
echo [OK] 后端已就绪（第 %tries% 次检查通过）。

:after_wait
REM ---- 3. 启动前端（独立窗口）----
echo [3/3] 启动前端 Vite dev server ...
start "SCADA-Frontend" cmd /k ""%~f0" --frontend"

REM ---- 打开浏览器 ----
start "" http://localhost:3333

echo.
echo ============================================
echo   全部启动完成！
echo   前端: http://localhost:3333
echo   后端: http://localhost:5555 （Swagger 为首页）
echo   依赖: MySQL(3306) / InfluxDB(8086) 需自行保持运行
echo   停止: 直接关闭对应的两个黑色窗口即可
echo ============================================
pause
endlocal
exit /b

REM ===================== 端口检测子程序 =====================
REM 用法: call :check_port 端口 进程名白名单
REM 返回: errorlevel 0 = 端口可用 / 已清理完成；errorlevel 1 = 用户取消或异常
:check_port
set "CHK_PORT=%~1"
set "CHK_PROC=%~2"
:check_again
REM 查找该端口的 LISTENING 进程 PID
set "FOUND_PID="
for /f "tokens=5" %%p in ('netstat -ano ^| findstr /R /C:":%CHK_PORT% " ^| findstr "LISTENING"') do (
    set "FOUND_PID=%%p"
)

if not defined FOUND_PID (
    echo [OK] 端口 %CHK_PORT% 空闲。
    exit /b 0
)

REM 有占用，反查进程名
set "PROC_NAME="
for /f "delims=," %%a in ('tasklist /FI "PID eq %FOUND_PID%" /FO CSV /NH') do (
    set "PROC_NAME=%%~a"
)

echo [占用] 端口 %CHK_PORT% 被进程占用: %PROC_NAME% (PID %FOUND_PID%)

REM 进程名是否命中白名单
if /i "%PROC_NAME%"=="%CHK_PROC%" (
    echo        检测到残留的 %CHK_PROC% 进程。
    echo.
    echo        按任意键杀掉该进程并释放端口 ...
    echo        （按 Ctrl+C 可取消，脚本将退出）
    pause >nul
    taskkill /F /PID %FOUND_PID% >nul 2>nul
    REM 等待端口释放，最多轮询 10 次
    set /a rel_tries=0
    :wait_release
    set /a rel_tries+=1
    netstat -ano | findstr /R /C:":%CHK_PORT% " | findstr "LISTENING" >nul
    if errorlevel 1 (
        echo [OK] 端口 %CHK_PORT% 已释放。
        exit /b 0
    )
    if !rel_tries! gtr 10 (
        echo [错误] 端口 %CHK_PORT% 杀掉进程后仍未释放，请手动处理。
        exit /b 1
    )
    timeout /t 1 /nobreak >nul
    goto :wait_release
) else (
    echo [错误] 端口 %CHK_PORT% 被非预期程序 "%PROC_NAME%" 占用，已中止启动。
    echo        请手动关闭该程序后重试。
    exit /b 1
)

REM ===================== 后端 =====================
:run_backend
title SCADA-Backend
cd /d "%~dp0Server\ScadaServer.WebApi"
echo [后端] 清理项目 dotnet clean ...
dotnet clean --nologo
echo [后端] 清理完成，开始启动 dotnet run ...
dotnet run --launch-profile http
exit /b

REM ===================== 前端 =====================
:run_frontend
title SCADA-Frontend
cd /d "%~dp0Client"
if not exist node_modules (
    echo [前端] 首次运行，正在安装依赖 npm install ...
    call npm install
    if errorlevel 1 (
        echo [前端] npm install 失败，请检查网络后重试。
        pause
        exit /b 1
    )
)
echo [前端] 启动 Vite dev server npm run dev ...
call npm run dev
exit /b
