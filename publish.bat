@echo off
REM ============================================
REM  绘梨衣桌宠 — 打包发布脚本
REM  输出: ..\Sakura-v4.0\（与源码同级）
REM ============================================

echo.
echo  🌸 绘梨衣桌宠 — 开始打包...
echo.

echo  [1/2] 发布 Release（自包含 .NET Runtime）...
dotnet publish src/EriiPet.App -c Release -r win-x64 ^
    -p:PublishSingleFile=true ^
    -p:SelfContained=true ^
    -p:PublishReadyToRun=true ^
    -p:PublishTrimmed=false ^
    -o "..\Sakura-v4.0"

if %errorlevel% neq 0 (
    echo  ❌ 打包失败！
    pause
    exit /b 1
)

echo.
echo  [2/2] 资源文件已由工程自动拷入发布目录（assets\）。
echo.
echo  ✅ 打包完成！输出: ..\Sakura-v4.0\
echo.
pause
