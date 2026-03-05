@echo off
setlocal

set GRADLE_VERSION=8.13
set DIST_NAME=gradle-%GRADLE_VERSION%
set DIST_URL=https://services.gradle.org/distributions/%DIST_NAME%-bin.zip
set ROOT_DIR=%~dp0
set CACHE_DIR=%ROOT_DIR%.gradle-dist
set ZIP_PATH=%CACHE_DIR%\%DIST_NAME%-bin.zip
set DIST_DIR=%CACHE_DIR%\%DIST_NAME%

if not exist "%CACHE_DIR%" mkdir "%CACHE_DIR%"

if not exist "%DIST_DIR%\bin\gradle.bat" (
  echo Bootstrapping Gradle %GRADLE_VERSION%...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri '%DIST_URL%' -OutFile '%ZIP_PATH%'"
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -Path '%ZIP_PATH%' -DestinationPath '%CACHE_DIR%' -Force"
)

call "%DIST_DIR%\bin\gradle.bat" %*
endlocal
