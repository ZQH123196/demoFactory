@echo off
rem docbudget.cmd -- Windows Task Scheduler entry for docbudget.ps1
rem
rem Why a wrapper instead of pointing schtasks straight at the .ps1: schtasks has no
rem output redirection, and we want a history trail. Log lives OUTSIDE the repo
rem (%LOCALAPPDATA%\DemoFactory) so the working tree never gets polluted.
rem
rem The alert channel is NOT this log: when AGENTS.md goes over budget, the script
rem writes a warning line into AGENTS.md itself, so the agent sees it on next load.
rem No LLM is involved in this loop at all.
setlocal
set LOGDIR=%LOCALAPPDATA%\DemoFactory
if not exist "%LOGDIR%" mkdir "%LOGDIR%"
set LOG=%LOGDIR%\docbudget.log

echo [%DATE% %TIME%] start >>"%LOG%"
pwsh -NoProfile -File "%~dp0docbudget.ps1" >>"%LOG%" 2>&1
set RC=%ERRORLEVEL%
echo [%DATE% %TIME%] exit=%RC% >>"%LOG%"
exit /b %RC%
