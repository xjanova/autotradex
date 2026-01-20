@echo off
REM AutoTrade-X Build Script
REM Workaround for CommunityToolkit.Mvvm + WPF source generator issue

echo Cleaning solution...
dotnet clean

echo Removing obj directories...
for /d /r . %%d in (obj) do @if exist "%%d" rd /s /q "%%d" 2>nul

echo Building solution (attempt 1)...
dotnet build -c Release

if %errorlevel% neq 0 (
    echo First build failed, trying with rebuild...
    dotnet build -c Release --no-incremental
)

echo.
echo Build complete. Check output above for errors.
echo.
echo NOTE: If you see source generator duplicate errors, you need to:
echo 1. Open the solution in Visual Studio
echo 2. Build from Visual Studio (it handles the temp project correctly)
echo 3. Or run: dotnet publish -c Release -o publish
pause
