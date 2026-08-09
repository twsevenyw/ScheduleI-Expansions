@echo off
setlocal

rem ---------------------------------------------------------------------------
rem  Ship a Schedule I Expansions release.
rem
rem  Double-click this. It bumps the patch version, builds, runs the updater
rem  tests, packages, commits, pushes, tags and creates the GitHub Release.
rem  The only thing it may ask for is one line of changelog, and pressing Enter
rem  accepts a line generated from your commit messages.
rem
rem  From a terminal you can pass anything publish.ps1 takes, for example:
rem      Push-Update.cmd -Bump minor
rem      Push-Update.cmd -DryRun
rem      Push-Update.cmd -Version 2.0.0 -Changelog "Rebuilt for game 0.5.0."
rem
rem  Close the game first. Mod DLLs are locked while it is running and the
rem  build deploys into Mods\ as a side effect, so a locked file means the
rem  release could be packaged from a stale DLL.
rem ---------------------------------------------------------------------------

title Schedule I Expansions - publish

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1" %*
set "PUBLISH_EXIT=%ERRORLEVEL%"

echo.
if "%PUBLISH_EXIT%"=="0" (
    echo Done.
) else (
    echo FAILED - exit code %PUBLISH_EXIT%. Nothing further was published.
    echo The error above says exactly which step stopped and why.
)

echo.
pause
exit /b %PUBLISH_EXIT%
