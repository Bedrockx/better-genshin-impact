@echo off
chcp 65001 >nul
cd /d %~dp0

if exist dist rd /s /q dist
mkdir dist\BetterGI

@echo [prepare compiler]
for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath`) do set "path=%path%;"%%i\MSBuild\Current\Bin";"%%i\Common7\IDE""

@echo [prepare version]
set "script=Select-String -Path '%~dp0..\BetterGenshinImpact\BetterGenshinImpact.csproj' -Pattern '<Version>(.*)<\/Version>' | ForEach-Object { $_.Matches.Groups[1].Value }"
for /f "usebackq delims=" %%i in (`powershell -NoLogo -NoProfile -Command "%script%"`) do set "pkgVer=%%i"
if "%pkgVer%"=="" set "pkgVer=0.78.91+mno"
echo extracted version: %pkgVer%

@echo [input version - press any key within 5s to customize]
for /f "usebackq delims=" %%v in (`powershell -NoLogo -NoProfile -Command "$d='%pkgVer%';[System.Console]::Error.Write('Enter version (default: '+$d+'): ');$s=[DateTime]::Now;$r=$null;while(($r -eq $null)-and(([DateTime]::Now-$s).TotalSeconds-lt 5)){if([System.Console]::KeyAvailable){$r=[System.Console]::ReadLine();break}Start-Sleep -Milliseconds 100}if([string]::IsNullOrWhiteSpace($r)){$d}else{$r}"`) do set "pkgVer=%%v"
set "b=%pkgVer%"
echo using version: %b%

set "tmpfolder=%~dp0dist\BetterGI"
set "archiveFile=BetterGI_v%b%.7z"

echo [build app using vs2022]
cd /d %~dp0
set "PUBLISH_DIR=..\BetterGenshinImpact\bin\x64\Release\net8.0-windows10.0.22621.0\publish\win-x64"
rd /s /q "%PUBLISH_DIR%" 2>nul
dotnet publish "%~dp0..\BetterGenshinImpact\BetterGenshinImpact.csproj" -c Release -p:PublishProfile=FolderProfile
if %ERRORLEVEL% neq 0 (
    echo [ERROR] dotnet publish failed with exit code %ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)

echo [copy publish output]
cd /d %~dp0
if not exist "%PUBLISH_DIR%" (
    echo [ERROR] Publish directory not found: %PUBLISH_DIR%
    echo The dotnet publish may have output to a different location.
    pause
    exit /b 1
)
cd /d "%PUBLISH_DIR%"
xcopy * "%tmpfolder%" /E /C /I /Y

echo [clean build-only files]
cd /d %~dp0
del /f /q %tmpfolder%\*.lib 2>nul
del /f /q %tmpfolder%\*ffmpeg*.dll 2>nul
del /f /q %tmpfolder%\*.pdb 2>nul

echo [build web projects]
set "WEB_WORK_DIR=%~dp0..\Tmp\web-build"
call :BuildWebProject "bettergi-map" "https://github.com/huiyadanli/bettergi-map" "main" "%tmpfolder%\Assets\Map\Editor"
call :BuildWebProject "bettergi-script-web" "https://github.com/zaodonganqi/bettergi-script-web" "" "%tmpfolder%\Assets\Web\ScriptRepo"

echo [verify build output]
dir /b "%tmpfolder%" >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] No files in %tmpfolder% - nothing to pack!
    pause
    exit /b 1
)

echo [pack app using 7z]
cd /d %~dp0
if exist "%archiveFile%" del /f /q "%archiveFile%"
MicaSetup.Tools\7-Zip\7z a "%archiveFile%" %tmpfolder%\* -t7z -mx=5 -mf=BCJ2 -r -y

rd /s /q dist\BetterGI

echo.
echo Done: %~dp0%archiveFile%
pause
goto :EOF

:: ============================================================
:: BuildWebProject Name RepoUrl Branch DestinationDir
:: ============================================================
:BuildWebProject
setlocal
set "NAME=%~1"
set "REPO=%~2"
set "BRANCH=%~3"
set "OUT=%~4"
set "REPO_DIR=%WEB_WORK_DIR%\%NAME%"

echo --- Building %NAME% ---

REM 1. Ensure git repo
call :EnsureGitRepo "%REPO_DIR%" "%REPO%" "%BRANCH%"
if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%

REM 2. npm install
pushd "%REPO_DIR%"
call npm install
if %ERRORLEVEL% neq 0 (
    popd
    exit /b %ERRORLEVEL%
)

REM 3. npm run build:single
call npm run build:single
if %ERRORLEVEL% neq 0 (
    popd
    exit /b %ERRORLEVEL%
)
popd

REM 4. Copy dist to output
if not exist "%REPO_DIR%\dist" (
    echo Error: dist directory not found in %REPO_DIR%
    exit /b 1
)
if not exist "%OUT%" mkdir "%OUT%"
xcopy "%REPO_DIR%\dist\*" "%OUT%" /E /C /I /Y
if %ERRORLEVEL% geq 4 exit /b %ERRORLEVEL%

echo --- %NAME% done ---
echo.
endlocal
goto :EOF

:: ============================================================
:: EnsureGitRepo Dir Url Branch
:: ============================================================
:EnsureGitRepo
setlocal
set "DIR=%~1"
set "REPO=%~2"
set "BRANCH=%~3"

if exist "%DIR%\.git" (
    echo Updating existing repo %DIR%...
    git -C "%DIR%" fetch --prune origin
    if not "%BRANCH%"=="" (
        git -C "%DIR%" checkout %BRANCH%
        git -C "%DIR%" pull --ff-only origin %BRANCH%
    ) else (
        git -C "%DIR%" pull --ff-only
    )
) else (
    if exist "%DIR%" (
        echo Error: %DIR% exists but is not a git repository.
        exit /b 1
    )
    echo Cloning %REPO%...
    if not "%BRANCH%"=="" (
        git clone --depth 1 -b %BRANCH% %REPO% "%DIR%"
    ) else (
        git clone --depth 1 %REPO% "%DIR%"
    )
)
endlocal
goto :EOF
