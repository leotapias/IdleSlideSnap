# SlideSnap

A tiny Windows app (about 14 KB, no dependencies) that watches one of your monitors and quietly saves a screenshot whenever the screen changes in a meaningful way. It's made for grabbing every slide while you watch a presentation on Zoom, Teams and similar apps.

## Download

Grab `SlideSnap.exe` from the [latest release](../../releases/latest) and run it. There's nothing to install.

## How to use it

1. Open `SlideSnap.exe` and pick the folder where the screenshots should go.
2. Pick the screen to watch, if you have more than one monitor.
3. Hit **PLAY**. Hit **STOP** when you're done.

Global hotkeys work even while another app is in front:

- **Ctrl+Alt+S**: play / stop
- **Ctrl+Alt+A**: take a screenshot manually

## How it works

- It looks at the screen every 0.7 seconds and compares it to the last saved screenshot, using a small grayscale thumbnail.
- It only saves when the image has changed **and** has stopped moving. This keeps it from catching a slide mid-transition or saving a playing video over and over.
- The sensitivity slider sets what percentage of the screen has to change before it counts as a new slide.
- The SlideSnap window is hidden from its own screenshots.
- Screenshots are saved as PNG files named `slide_0001_YYYYMMDD_HHMMSS.png`.

## Building

Run `build.bat`. It uses the C# compiler that comes with Windows (.NET Framework 4), so there's nothing extra to install.

Note: the app's interface is in Spanish.
