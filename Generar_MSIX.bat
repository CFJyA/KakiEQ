@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo ========================================================
echo       Generador de Paquete MSIX / MSIXBundle
echo       Proyecto: Kaki EQ (WinUI 3 - Release x64)
echo ========================================================
echo.

set "MSBUILD_EXE=C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
if not exist "%MSBUILD_EXE%" (
    for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
        set "MSBUILD_EXE=%%i"
    )
)

if not exist "%MSBUILD_EXE%" (
    echo Error: No se encontró MSBuild. Verifica que Visual Studio esté instalado.
    pause
    exit /b 1
)

echo Usando MSBuild: "%MSBUILD_EXE%"
echo Compilando y empaquetando en Release (x64)...
echo.

"%MSBUILD_EXE%" "KakiEQ_WinUI\KakiEQ_WinUI.csproj" /target:Build /p:Configuration=Release /p:Platform=x64 /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=true /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxBundle=Always

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] La compilación o empaquetado falló con código %ERRORLEVEL%.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ========================================================
echo  ¡Paquete generado con éxito!
echo  Ubicación: KakiEQ_WinUI\AppPackages\
echo ========================================================
echo.
pause
