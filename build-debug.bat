@echo off
setlocal
dotnet build "%~dp0winforms\MCPHub.slnx" --configuration Debug
exit /b %ERRORLEVEL%
