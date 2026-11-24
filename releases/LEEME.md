# Presenta: instrucciones rápidas

Presenta es un reloj Wear OS que controla una presentación de PowerPoint en el computador con Windows. Muestra el avance, avisa cuando se acaba el tiempo y, si se quiere, una IA redacta las recomendaciones. Todo se prueba con el emulador de reloj, sin un reloj físico.

## Qué hay en esta carpeta

| Archivo | Para qué sirve |
|---|---|
| `Presenta-Companion-win-x64.zip` | El programa de Windows (el `compañero`). No hace falta instalar .NET. |
| `Presenta-Wear.apk` | La aplicación del reloj, para instalar en el emulador. Es una versión de depuración (debug), suficiente para el emulador. |
| `SHA256SUMS.txt` | Sumas de verificación de los dos archivos. |

## Requisitos

- Windows 11 o Windows 10 (64 bits).
- Android Studio con el emulador `Wear_OS_Large_Round` ya creado.
- PowerPoint para abrir una presentación.

## 1. Iniciar el compañero en Windows

1. Descomprime `Presenta-Companion-win-x64.zip`. Queda la carpeta `Presenta-Companion`.
2. Entra a la carpeta, haz clic derecho en `Iniciar-Presenta.ps1` y elige Ejecutar con PowerShell. Otra forma, desde PowerShell dentro de la carpeta: `powershell -ExecutionPolicy Bypass -File .\Iniciar-Presenta.ps1`
3. Se abre la ventana de Presenta. Debe decir "Servidor activo".

La primera vez, Windows puede mostrar un aviso de SmartScreen (`Windows protegió su PC`) o del firewall. En SmartScreen elige Más información y luego Ejecutar de todos modos. En el firewall elige Permitir acceso (basta en redes privadas). Es normal: el programa no está firmado y abre un puerto local para hablar con el reloj.

## 2. Instalar la aplicación en el emulador

1. Abre el emulador `Wear_OS_Large_Round` desde Android Studio y espera a que arranque.
2. Instala con `adb`. Cambia `<RUTA-DEL-SDK>` por la carpeta del SDK de Android (en Android Studio: Settings, Android SDK):

   `& "<RUTA-DEL-SDK>\platform-tools\adb.exe" install -r Presenta-Wear.apk`

   Otra forma: arrastra `Presenta-Wear.apk` sobre la ventana del emulador.
3. Abre la aplicación Presenta en el reloj.

## 3. Vincular el reloj

1. En la ventana de Presenta, abre la página Vincular. Allí está el código de 6 dígitos. Si hace falta uno nuevo, pulsa "Generar código nuevo".
2. Escribe ese código en el reloj. La dirección del compañero para el emulador ya viene configurada: `10.0.2.2:8765`.
3. Si el reloj no se conecta, usa esta alternativa (con el emulador abierto): `& "<RUTA-DEL-SDK>\platform-tools\adb.exe" reverse tcp:8765 tcp:8765`, y después `& "<RUTA-DEL-SDK>\platform-tools\adb.exe" shell run-as com.presenta.wear sh -c "echo ws://localhost:8765 > files/server_address.txt"`. Reinicia la aplicación del reloj.

## 4. Elegir la ventana de PowerPoint

1. Abre tu presentación en PowerPoint.
2. En Presenta, ve a la página Ventana ("Ventana de la presentación"), elige la ventana de PowerPoint y pulsa "Verificar".
3. Revisa que "Enviar teclas a la presentación" esté encendido. Ya puedes iniciar la presentación desde el reloj.

## 5. Activar la IA (opcional)

Sin esto todo funciona igual; solo que las recomendaciones no las redacta una IA.

1. En la carpeta `Presenta-Companion`, copia `.env.example` con el nombre `.env`.
2. Escribe tu clave después de `PRESENTA_AI_API_KEY=`. No la compartas ni la subas a ningún sitio.
3. Inicia de nuevo con `Iniciar-Presenta.ps1`.
4. En la página IA y alertas, enciende "Usar IA para redactar las recomendaciones".

## Si algo falla

1. El reloj no conecta: confirma que Presenta diga "Servidor activo" y usa la alternativa de `adb reverse` del paso 3.
2. El puerto 8765 está ocupado: cierra otra copia de Presenta y vuelve a abrirla.
3. PowerShell no deja ejecutar el script: usa la línea con `-ExecutionPolicy Bypass` del paso 1.
4. Las teclas no llegan a PowerPoint: elige la ventana de nuevo y pulsa "Verificar".
5. La IA no responde: revisa que el `.env` esté en la misma carpeta que `Iniciar-Presenta.ps1` y que la clave esté completa.
