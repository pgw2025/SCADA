# ============================================================
# SCADA 一键开发启动脚本
#   后端 : ScadaServer.WebApi  (dotnet clean -> dotnet run, 端口 5555)
#   前端 : Client Vite dev server (npm run dev, 端口 3333)
# 两个组件分别在独立的新窗口运行，日志互不干扰。
# ============================================================

$root        = $PSScriptRoot
$serverDir   = Join-Path $root 'Server'
$clientDir   = Join-Path $root 'Client'

# ---------- 前端：确保依赖已安装 ----------
if (-not (Test-Path (Join-Path $clientDir 'node_modules'))) {
    Write-Host '[Client] 未检测到 node_modules，先执行 npm install ...' -ForegroundColor Yellow
    Push-Location $clientDir
    npm install
    Pop-Location
    if ($LASTEXITCODE -ne 0) {
        Write-Host '[Client] npm install 安装依赖失败，请检查网络后重试' -ForegroundColor Red
        return
    }
}

# ---------- 敏感配置：数据库密码 / JWT 密钥必须由环境变量注入 ----------
# 禁止在本脚本中明文写死密钥，也不得提交到 git。
# 脚本从当前环境继承这两个变量；缺失时给出提示并中止，避免后端带空/弱配置启动。
$needDbPwd  = [string]::IsNullOrWhiteSpace($env:SystemDbConfig__Password)
$needJwtKey = [string]::IsNullOrWhiteSpace($env:Jwt__Key)
if ($needDbPwd -or $needJwtKey) {
    Write-Host '' -ForegroundColor Red
    if ($needDbPwd)  { Write-Host '[配置缺失] 未检测到环境变量 SystemDbConfig__Password（数据库密码）' -ForegroundColor Red }
    if ($needJwtKey) { Write-Host '[配置缺失] 未检测到环境变量 Jwt__Key（JWT 签名密钥，必须 >=32 字符）' -ForegroundColor Red }
    Write-Host '请先设置后再启动，例如：' -ForegroundColor Yellow
    Write-Host "  `$env:SystemDbConfig__Password = '<你的数据库密码>'" -ForegroundColor Yellow
    Write-Host "  `$env:Jwt__Key              = '<至少32字符的随机密钥>'" -ForegroundColor Yellow
    Write-Host '可通过 [Environment]::SetEnvironmentVariable(...) 永久写入当前用户，提示后重启终端注入。' -ForegroundColor Yellow
    Write-Host '' -ForegroundColor Yellow
    return
}

# ---------- 后端窗口：先 dotnet clean 清理所有后端项目，再 dotnet run ----------
# 注：Start-Process 的子窗口会继承父进程环境变量中的 SystemDbConfig__Password / Jwt__Key，
#     无需、也不应把它们拼进命令行参数（避免密钥暴露在进程参数中）。
$backendCmd = "Set-Location '$serverDir'; " +
    "dotnet clean --nologo | Out-Host; " +
    "Write-Host '' -ForegroundColor Cyan; " +
    "Write-Host '===== 后端清理完成，开始运行 ScadaServer.WebApi (端口 5555) =====' -ForegroundColor Cyan; " +
    "dotnet run --project ScadaServer.WebApi"
Start-Process powershell -ArgumentList '-NoExit', '-ExecutionPolicy', 'Bypass', '-Command', "`"$backendCmd`"" -WindowStyle Normal

# ---------- 前端窗口：npm run dev ----------
# 清除 DISABLE_HMR，确保开发环境开启热更新
$frontendCmd = "Set-Location '$clientDir'; " +
    "Remove-Item Env:DISABLE_HMR -ErrorAction SilentlyContinue; " +
    "Write-Host '===== 前端 Vite dev server (端口 3333) 开始启动 =====' -ForegroundColor Magenta; " +
    "npm run dev"
Start-Process powershell -ArgumentList '-NoExit', '-ExecutionPolicy', 'Bypass', '-Command', "`"$frontendCmd`"" -WindowStyle Normal

# ---------- 汇总提示 ----------
Start-Sleep -Milliseconds 500
Clear-Host
Write-Host '==================== SCADA 开发环境已启动 ====================' -ForegroundColor Green
Write-Host '  后端 WebApi : http://localhost:5555/swagger' -ForegroundColor Green
Write-Host '  前端 Client  : http://localhost:3333' -ForegroundColor Green
Write-Host '  后端依赖    : MySQL(3306) / InfluxDB(8086) 需自行保持运行' -ForegroundColor Yellow
Write-Host '  关闭方式    : 分别关闭对应的后端/前端窗口即可' -ForegroundColor Yellow
Write-Host '==============================================================' -ForegroundColor Green