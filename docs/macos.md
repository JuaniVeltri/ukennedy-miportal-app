# Plan para macOS

El motor (OpenSilver en WebAssembly) y el host (Photino) ya funcionan en macOS: Photino usa ahí
WKWebView, el navegador del sistema. Lo que falta es lo que hoy se resuelve llamando directo a
WebKitGTK, que solo existe en Linux (`src/Plata.Host/WebKitGtk.cs` y `CookiesDelNavegador.cs`).

| Qué hace hoy en Linux | Equivalente en macOS |
|---|---|
| Registrar el esquema `uksv` como seguro para que el portal (https) lo cargue en su iframe | Photino registra los esquemas con `WKURLSchemeHandler`; hay que probar si WKWebView lo bloquea como contenido mixto |
| Aceptar cookies de terceros (el portal corre en un marco de la ventana) | WKWebView las bloquea siempre (ITP). Alternativa: abrir el portal en su propia ventana y las apps en la ventana con pestañas |
| Leer las cookies del portal para el lanzador | `WKHTTPCookieStore.getAllCookies` |
| Agregar los scripts del portal | `WKUserContentController.addUserScript` |

Esas llamadas de Objective-C se pueden hacer desde .NET con el runtime de Objective-C
(`objc_msgSend`) o con un agregado chico a Photino.Native.

## Paquete

- `dotnet publish src/Plata.Host -r osx-arm64 --self-contained` (y `osx-x64` para Intel), con el
  motor en `motor/`, dentro de un `.app` con su `Info.plist` e ícono.
- Firmar y notarizar con una cuenta de Apple Developer (USD 99 por año). Sin eso, macOS pide
  "clic derecho → Abrir" la primera vez.
- GitHub Actions tiene máquinas macOS para armar el paquete en cada versión.

## Cómo ayudar

Hace falta alguien con una Mac para probar cada paso. Si podés, abrí un issue con la etiqueta
**macos**.
