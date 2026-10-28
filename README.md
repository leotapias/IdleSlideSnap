# SlideSnap

Pequeña app para Windows (14 KB, sin dependencias) que vigila un monitor y guarda silenciosamente una captura cada vez que la pantalla cambia de forma importante. Pensada para sacar una imagen de cada slide al ver presentaciones en Zoom, Teams, etc.

## Uso

1. Abre `SlideSnap.exe` y elige la carpeta donde guardar las capturas.
2. Elige la pantalla a vigilar (si tienes más de un monitor).
3. Aprieta **PLAY**. Para detener, aprieta **STOP**.

Atajos globales (funcionan aunque otra app esté en primer plano):

- **Ctrl+Alt+S**: play / stop
- **Ctrl+Alt+A**: captura manual

## Cómo funciona

- Revisa la pantalla cada 0.7 s y la compara con la última captura guardada usando una miniatura en escala de grises.
- Solo guarda cuando la imagen cambió **y** ya está quieta, para no capturar a mitad de una transición ni guardar videos sin parar.
- La barra de sensibilidad define qué porcentaje de la pantalla debe cambiar para considerarlo un slide nuevo.
- La ventana de SlideSnap se excluye de sus propias capturas.
- Las capturas se guardan en PNG como `slide_0001_AAAAMMDD_HHMMSS.png`.

## Compilar

Ejecuta `build.bat`. Usa el compilador de C# que viene incluido en Windows (.NET Framework 4), no hace falta instalar nada.
