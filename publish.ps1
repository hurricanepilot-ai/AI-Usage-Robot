param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$publishedExeName = "AIUsageRobot-DSH.exe"
$publishRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "publish"))
$publishOutput = [System.IO.Path]::GetFullPath((Join-Path $publishRoot $Runtime))
if (!$publishOutput.StartsWith($publishRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "发布目录超出项目 publish 目录。"
}
if (Test-Path -LiteralPath $publishOutput) {
    Remove-Item -LiteralPath $publishOutput -Recurse -Force
}

dotnet publish "$PSScriptRoot\src\AIUsageRobot.Widget\AIUsageRobot.Widget.csproj" `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $publishOutput
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出代码 $LASTEXITCODE"
}

$defaultExe = Join-Path $publishOutput "AIUsageRobot.exe"
$editionExe = Join-Path $publishOutput $publishedExeName
if (!(Test-Path -LiteralPath $defaultExe -PathType Leaf)) {
    throw "未找到默认发布文件 $defaultExe"
}
Move-Item -LiteralPath $defaultExe -Destination $editionExe

$publishedFiles = @(Get-ChildItem -LiteralPath $publishOutput -File)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne $publishedExeName) {
    throw "发布结果不是单一 $publishedExeName，请检查 $publishOutput"
}

Write-Host "已生成: $($publishedFiles[0].FullName)"
