@echo off
setlocal
REM 一键：生成站点 → git 提交 → 推送到 GitHub
REM 可从 Docear、任务计划程序或其它工具直接调用本文件。

set "SCRIPT_DIR=%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%publish-blog.ps1" %*
set "EXITCODE=%ERRORLEVEL%"
exit /b %EXITCODE%
