@echo off
rem Compila SlideSnap.exe con el compilador de C# incluido en Windows (.NET Framework 4).
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /out:SlideSnap.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll SlideSnap.cs
if %errorlevel%==0 echo Listo: SlideSnap.exe
