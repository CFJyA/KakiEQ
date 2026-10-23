@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo ==========================================================
echo        Compilador del Instalador Oficial de Kaki EQ
echo ==========================================================
echo.

echo [1/3] Publicando aplicacion independiente (self-contained)...
dotnet publish "KakiEQ_WinUI\KakiEQ_WinUI.csproj" /p:PublishProfile=win-x64
if %ERRORLEVEL% NEQ 0 (
    echo Error al compilar y publicar la aplicacion.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [2/3] Generando instalador ejecutable con Inno Setup...
set "ISCC=C:\Users\W11\AppData\Local\Programs\Inno Setup 6\iscc.exe"
if not exist "%ISCC%" (
    set "ISCC=C:\Program Files (x86)\Inno Setup 6\iscc.exe"
)
"%ISCC%" "installer\KakiEQ_Installer.iss"
if %ERRORLEVEL% NEQ 0 (
    echo Error al compilar el instalador con Inno Setup.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [3/3] Firmando instalador Setup con certificado digital...
set "SIGNTOOL=C:\Users\W11\.nuget\packages\microsoft.windows.sdk.buildtools\10.0.28000.2705\bin\10.0.28000.0\x64\signtool.exe"
if exist "%SIGNTOOL%" (
    "%SIGNTOOL%" sign /fd SHA256 /f "KakiEQ_WinUI\KakiEQ_TemporaryKey.pfx" /p "KakiEQ2026!" "installer\Output\KakiEQ_Setup_v1.0.exe"
)

echo.
echo ==========================================================
echo  ¡INSTALADOR LISTO!
echo  Ubicacion: installer\Output\KakiEQ_Setup_v1.0.exe
echo ==========================================================
echo.
pause
