@echo off
rem Builds the Shared Sweeps mod and installs it into BepInEx\plugins.
rem Usage: build.bat ["C:\path\to\Moon Sweepers"]
rem Uses the .NET SDK if installed, otherwise the C# compiler that ships with Windows.
setlocal EnableExtensions
set "HERE=%~dp0"
set "NAME=MoonSweepersSharedProgression"

set "GAME=%~1"
if not "%GAME%"=="" goto :havegame
if exist "%HERE%..\MoonSweepers.exe" for %%I in ("%HERE%..") do set "GAME=%%~fI"
if "%GAME%"=="" set "GAME=C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers"
:havegame

echo Game folder: %GAME%
if not exist "%GAME%\MoonSweepers.exe" goto :nogame
if not exist "%GAME%\BepInEx\core\BepInEx.dll" goto :nobepinex

set "HAVESDK="
for /f "delims=" %%S in ('dotnet --list-sdks 2^>nul') do set "HAVESDK=1"
if not defined HAVESDK goto :csc

echo Building with the .NET SDK...
dotnet build "%HERE%%NAME%.csproj" -c Release -nologo -v:minimal "-p:GameDir=%GAME%"
if errorlevel 1 goto :failed
set "OUT=%HERE%bin\Release\netstandard2.1\%NAME%.dll"
goto :install

:csc
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto :nocompiler
echo .NET SDK not found - building with the compiler built into Windows...
set "M=%GAME%\MoonSweepers_Data\Managed"
set "B=%GAME%\BepInEx\core"
if not exist "%HERE%bin" mkdir "%HERE%bin"
set "OUT=%HERE%bin\%NAME%.dll"
"%CSC%" /nologo /target:library /optimize+ /nostdlib+ /noconfig /nowarn:1701,1702 /out:"%OUT%" /r:"%M%\mscorlib.dll" /r:"%M%\System.dll" /r:"%M%\System.Core.dll" /r:"%M%\netstandard.dll" /r:"%M%\UnityEngine.dll" /r:"%M%\UnityEngine.CoreModule.dll" /r:"%B%\BepInEx.dll" /r:"%B%\0Harmony.dll" "%HERE%src\*.cs"
if errorlevel 1 goto :failed

:install
set "DEST=%GAME%\BepInEx\plugins\%NAME%"
if not exist "%DEST%" mkdir "%DEST%"
copy /y "%OUT%" "%DEST%\%NAME%.dll" >nul
if errorlevel 1 goto :copyfailed
echo.
echo Installed: %DEST%\%NAME%.dll
echo Start the game, host a crew, and check BepInEx\LogOutput.log for "Shared Sweeps".
goto :done

:nogame
echo Could not find MoonSweepers.exe in "%GAME%".
echo Run: build.bat "C:\path\to\Moon Sweepers"
goto :fail
:nobepinex
echo BepInEx is not installed yet. Install BepInEx 5 (x64) into the game folder, run the game once,
echo close it, then run build.bat again. See README.md.
goto :fail
:nocompiler
echo No C# compiler found. Install the .NET SDK (winget install Microsoft.DotNet.SDK.8) and try again.
goto :fail
:copyfailed
echo Built "%OUT%" but could not copy it into BepInEx\plugins. Copy it there by hand.
goto :fail
:failed
echo.
echo Build failed - see the errors above.
:fail
endlocal
pause
exit /b 1
:done
endlocal
pause
exit /b 0
