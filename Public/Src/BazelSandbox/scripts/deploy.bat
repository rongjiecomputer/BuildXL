@echo off
rmdir /S /Q %TEMP%\BazelSandboxDeploy
mkdir %TEMP%\BazelSandboxDeploy
xcopy /y /s %~dp0..\..\..\..\Out\Bin\BazelSandbox\release\win-x64 %TEMP%\BazelSandboxDeploy

@REM Remove debug symbols
del %TEMP%\BazelSandboxDeploy\*.pdb
del %TEMP%\BazelSandboxDeploy\x86\*.pdb
del %TEMP%\BazelSandboxDeploy\x64\*.pdb

@REM Remove UCRT DLLs (up-to-date Win7 and onwards should have it already)
del %TEMP%\BazelSandboxDeploy\api-*.dll
del %TEMP%\BazelSandboxDeploy\ucrtbase.dll
%~dp0deploy.py

"C:\Program Files\7-Zip\7z.exe" a -tzip %~dp0..\..\..\..\Out\Bin\BazelSandbox\BazelSandbox.zip %TEMP%\BazelSandboxDeploy
