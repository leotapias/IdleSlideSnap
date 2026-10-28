@echo off
rem Builds SlideSnap.exe using the C# compiler that ships with Windows (.NET Framework 4).
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /out:SlideSnap.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll SlideSnap.cs
if %errorlevel%==0 echo Done: SlideSnap.exe
