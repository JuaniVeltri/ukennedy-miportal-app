# Plan para macOS

El motor (OpenSilver en WebAssembly) y el host (Photino) ya funcionan en macOS: Photino usa ahí
WKWebView, el navegador del sistema. Lo que falta es lo que hoy se resuelve llamando directo a
WebKitGTK, que solo existe en Linux (`src/Plata.Host/WebKitGtk.cs` y `CookiesDelNavegador.cs`).

| En Linux (WebKitGTK) | En macOS (WKWebView) | Estado |
|---|---|---|
| El script del portal avisa el enlace `uksv:` a la ventana | Igual: el mismo script | Hecho, a probar |
| Agregar los scripts del portal | `WKUserContentController.addUserScript:` (`WebKitMac.AgregarScript`) | Hecho, a probar |
| Aceptar cookies de terceros (el portal corre en un marco) | Desactivar la prevención de rastreo con `_setResourceLoadStatisticsEnabled:` (interna de WebKit) | Hecho, a probar |
| Leer las cookies del portal para el lanzador | `WKHTTPCookieStore getAllCookies:` con un bloque de Objective-C | Hecho, a probar |

Todo eso está en `src/Plata.Host/WebKitMac.cs`, que habla con Objective-C desde .NET
(`objc_msgSend`). Cada paso se intenta por separado y deja su resultado en el registro
(`~/Library/Logs/UKennedyMiPortalApp/registro.txt`): "macOS: … listo" o "no aplicado".

Si la prevención de rastreo no se puede desactivar y el portal no mantiene la sesión dentro del
marco, la alternativa es abrir el portal en su propia ventana y las apps en la ventana con
pestañas.

## Paquete

- `empaquetado/macos/empaquetar.sh` arma el `.app` (el host autocontenido para `osx-arm64`, el
  motor, `argumentos.txt`, `Info.plist` e ícono), lo firma ad hoc y lo mete en un `.dmg`. El CI
  lo arma en cada cambio (sección *Artifacts*) y en cada versión.
- Firmar y notarizar con una cuenta de Apple Developer (USD 99 por año). Sin eso, macOS pide
  "clic derecho → Abrir" la primera vez.

## Cómo ayudar

Hace falta alguien con una Mac para probar cada paso. Si podés, abrí un issue con la etiqueta
**macos**.
