# 从项目根启动独立客服服务，数据库和密钥配置参见 README。
param([int]$Port = 5180)
$ErrorActionPreference = 'Stop'
if ($Port -lt 1024 -or $Port -gt 65535) {
    throw '监听端口必须为 1024 至 65535。'
}
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    dotnet run --project src/AI.Customer.Service.Web --no-launch-profile -- --urls "http://localhost:$Port"
    if ($LASTEXITCODE -ne 0) {
        throw '客服服务启动失败，请检查启动日志。'
    }
}
finally {
    Pop-Location
}
