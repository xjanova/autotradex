@echo off
chcp 65001 > nul
REM ========================================
REM AutoTrade-X Portable Build Script
REM สร้างโปรแกรมแบบ Portable พร้อมใช้งาน
REM ========================================

echo.
echo  ╔════════════════════════════════════════════╗
echo  ║     AutoTrade-X Portable Builder           ║
echo  ║     สร้างโปรแกรมแบบ Portable               ║
echo  ╚════════════════════════════════════════════╝
echo.

set OUTPUT_DIR=AutoTradeX-Portable
set PUBLISH_DIR=publish-temp

REM ลบโฟลเดอร์เก่า
if exist "%OUTPUT_DIR%" (
    echo กำลังลบโฟลเดอร์เก่า...
    rd /s /q "%OUTPUT_DIR%"
)
if exist "%PUBLISH_DIR%" rd /s /q "%PUBLISH_DIR%"

echo.
echo [1/5] กำลังทำความสะอาด solution...
dotnet clean -v q > nul 2>&1

echo [2/5] กำลัง restore packages...
dotnet restore -v q > nul 2>&1

echo [3/5] กำลัง build โปรแกรมแบบ self-contained...
dotnet publish src\AutoTradeX.UI\AutoTradeX.UI.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o "%PUBLISH_DIR%" ^
    -v q

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Build ล้มเหลว! กรุณาตรวจสอบข้อผิดพลาด
    pause
    exit /b 1
)

echo [4/5] กำลังจัดเตรียมโฟลเดอร์ Portable...

REM สร้างโครงสร้างโฟลเดอร์
mkdir "%OUTPUT_DIR%"
mkdir "%OUTPUT_DIR%\logs"
mkdir "%OUTPUT_DIR%\data"

REM คัดลอกไฟล์หลัก
copy "%PUBLISH_DIR%\AutoTradeX.UI.exe" "%OUTPUT_DIR%\AutoTradeX.exe" > nul
copy "appsettings.json" "%OUTPUT_DIR%\" > nul

echo [5/5] กำลังสร้างไฟล์ช่วยเหลือ...

REM สร้าง Launcher
(
echo @echo off
echo chcp 65001 ^> nul
echo cd /d "%%~dp0"
echo start "" "AutoTradeX.exe"
) > "%OUTPUT_DIR%\เริ่มโปรแกรม.cmd"

REM สร้าง Setup API Keys Script
(
echo @echo off
echo chcp 65001 ^> nul
echo echo.
echo echo  ========================================
echo echo   ตั้งค่า API Keys สำหรับ AutoTrade-X
echo echo  ========================================
echo echo.
echo echo กรุณากรอก API Keys ของ Exchange A:
echo set /p "KEY_A=API Key: "
echo set /p "SECRET_A=API Secret: "
echo echo.
echo echo กรุณากรอก API Keys ของ Exchange B:
echo set /p "KEY_B=API Key: "
echo set /p "SECRET_B=API Secret: "
echo echo.
echo echo กำลังบันทึกการตั้งค่า...
echo ^(
echo echo set EXCHANGE_A_API_KEY=%%KEY_A%%
echo echo set EXCHANGE_A_API_SECRET=%%SECRET_A%%
echo echo set EXCHANGE_B_API_KEY=%%KEY_B%%
echo echo set EXCHANGE_B_API_SECRET=%%SECRET_B%%
echo ^) ^> "%%~dp0api-keys.cmd"
echo echo.
echo echo บันทึกเรียบร้อย! API Keys ถูกเก็บไว้ในไฟล์ api-keys.cmd
echo echo.
echo pause
) > "%OUTPUT_DIR%\ตั้งค่า-API-Keys.cmd"

REM สร้าง Launcher พร้อม API Keys
(
echo @echo off
echo chcp 65001 ^> nul
echo cd /d "%%~dp0"
echo if exist "api-keys.cmd" call "api-keys.cmd"
echo start "" "AutoTradeX.exe"
) > "%OUTPUT_DIR%\เริ่มโปรแกรม-พร้อม-API.cmd"

REM ลบโฟลเดอร์ชั่วคราว
rd /s /q "%PUBLISH_DIR%" > nul 2>&1

echo.
echo  ╔════════════════════════════════════════════╗
echo  ║           สร้างเสร็จสมบูรณ์!               ║
echo  ╚════════════════════════════════════════════╝
echo.
echo  โฟลเดอร์: %OUTPUT_DIR%
echo.
echo  โครงสร้างข้อมูล:
echo  - data\autotradex.db  : ฐานข้อมูล SQLite (สร้างอัตโนมัติ)
echo  - logs\               : ไฟล์ log ข้อความ
echo  - appsettings.json    : การตั้งค่าโปรแกรม
echo.
echo  วิธีใช้งาน:
echo  1. คัดลอกโฟลเดอร์ "%OUTPUT_DIR%" ไปที่ใดก็ได้
echo  2. ดับเบิลคลิก "เริ่มโปรแกรม.cmd" เพื่อเปิดโปรแกรม
echo  3. (ถ้าต้องการเทรดจริง) รัน "ตั้งค่า-API-Keys.cmd" ก่อน
echo     แล้วใช้ "เริ่มโปรแกรม-พร้อม-API.cmd"
echo.
echo  ข้อมูลที่เก็บใน SQLite:
echo  - ประวัติการเทรด (ไม่จำกัดจำนวน)
echo  - ยอดเงินและ P/L รายวัน
echo  - การตั้งค่าคู่เหรียญ
echo  - Demo Wallet สำหรับ Paper Trading
echo.
echo  ไฟล์ในโฟลเดอร์:
dir /b "%OUTPUT_DIR%"
echo.
pause
