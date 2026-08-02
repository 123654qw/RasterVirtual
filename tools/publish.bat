@echo off
REM Raster Virtual 发布脚本
REM 1) 单文件自包含发布到 publish 目录
REM 2) 把内置 QEMU 运行时 runtime\qemu 复制到 exe 同目录
setlocal
set ROOT=E:\PC\Raster Virtual
set SRC=%ROOT%\src\RasterVirtual
set OUT=%ROOT%\publish
set RUNTIME=%SRC%\runtime\qemu

if not exist "%RUNTIME%\qemu-system-x86_64.exe" (
  echo [ERROR] 未找到内置 QEMU 运行时：%RUNTIME%
  echo         请先运行 tools\prepare_qemu.py 生成 runtime\qemu
  exit /b 1
)

echo [1/2] dotnet publish ...
dotnet publish "%SRC%\RasterVirtual.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableWindowsTargeting=true -o "%OUT%"
if errorlevel 1 (
  echo [ERROR] publish 失败
  exit /b 1
)

echo [2/2] 复制内置 QEMU 运行时 -> %OUT%\runtime\qemu
if exist "%OUT%\runtime\qemu" rmdir /s /q "%OUT%\runtime\qemu"
xcopy /E /I /Y "%RUNTIME%" "%OUT%\runtime\qemu" >nul

echo.
echo 完成。发布产物：
echo   %OUT%\RasterVirtual.exe
echo   %OUT%\runtime\qemu\
endlocal
