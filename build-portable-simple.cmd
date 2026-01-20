@echo off
chcp 65001 > nul
echo.
echo กำลังสร้าง AutoTrade-X Portable...
echo.

set OUT=AutoTradeX-Portable

if exist "%OUT%" rd /s /q "%OUT%"
mkdir "%OUT%"
mkdir "%OUT%\logs"

dotnet publish src\AutoTradeX.UI\AutoTradeX.UI.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o "%OUT%" -v q

if %errorlevel% neq 0 (
    echo Build ล้มเหลว!
    pause
    exit /b 1
)

copy appsettings.json "%OUT%\" > nul

echo @echo off > "%OUT%\Run.cmd"
echo cd /d "%%~dp0" >> "%OUT%\Run.cmd"
echo start AutoTradeX.UI.exe >> "%OUT%\Run.cmd"

echo.
echo เสร็จสิ้น! โฟลเดอร์: %OUT%
echo ดับเบิลคลิก Run.cmd หรือ AutoTradeX.UI.exe เพื่อเริ่มโปรแกรม
echo.
pause
