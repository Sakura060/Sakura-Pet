@echo off
REM ============================================
REM  绘梨衣桌宠 Pro — 一键构建脚本
REM  用法：双击 build.bat 或在终端运行
REM  前提：需要安装 .NET 9 SDK
REM       https://dotnet.microsoft.com/download
REM ============================================

echo.
echo  🌸 绘梨衣桌宠 Pro — 开始构建...
echo.

REM 恢复 NuGet 包
echo  [1/2] 恢复依赖包...
dotnet restore EriiPet.sln
if %errorlevel% neq 0 (
    echo  ❌ 依赖恢复失败！请检查网络连接。
    pause
    exit /b 1
)

REM 构建 Release 版本
echo  [2/2] 编译 Release 版本...
dotnet build EriiPet.sln -c Release
if %errorlevel% neq 0 (
    echo  ❌ 编译失败！请检查错误信息。
    pause
    exit /b 1
)

echo.
echo  ✅ 构建成功！
echo.
echo  运行方式: dotnet run --project src/EriiPet.App
echo  或双击:   src\EriiPet.App\bin\Release\net9.0-windows\EriiPet.exe
echo.
pause
