@echo off
rem Builds IdleSlideSnap.exe using the C# compiler that ships with Windows (.NET Framework 4).
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /out:IdleSlideSnap.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll IdleSlideSnap.cs
if errorlevel 1 exit /b 1
echo Done: IdleSlideSnap.exe
