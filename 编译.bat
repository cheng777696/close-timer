@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [错误] 找不到 C# 编译器 csc.exe，请确认系统已启用 .NET Framework 4.x。
    pause
    exit /b 1
)

echo 正在编译……
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /utf8output ^
    /out:"定时关闭程序.exe" ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    "src\App.cs" "src\ConsoleRunner.cs" "src\MainForm.cs" "src\Program.cs" "src\SelfTest.cs"

if errorlevel 1 (
    echo.
    echo [失败] 编译出错，请把上面的信息发给开发者。
    pause
    exit /b 1
)

echo.
echo [成功] 已生成：%~dp0定时关闭程序.exe
pause
