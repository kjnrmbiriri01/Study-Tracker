@echo off
rem Builds StudyTracker.exe using the C# compiler that ships with Windows (no install needed).
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /out:StudyTracker.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll StudyTracker.cs
if exist StudyTracker.exe (
  echo.
  echo Success! Double-click StudyTracker.exe to run it.
) else (
  echo.
  echo Build failed - see errors above.
)
pause
