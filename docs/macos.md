# macOS

El motor (OpenSilver en WebAssembly) y el host (Photino) funcionan igual en macOS: Photino usa ahí
WKWebView, el navegador del sistema. Lo que en Linux se resuelve llamando directo a WebKitGTK
(`src/Plata.Host/WebKitGtk.cs` y `CookiesDelNavegador.cs`), en Mac se resuelve con WKWebView.

| En Linux (WebKitGTK) | En macOS (WKWebView) | Estado |
|---|---|---|
| El script del portal avisa el enlace `uksv:` a la ventana (y el marco además lo carga por el esquema) | Solo el script: WKWebView no carga un esquema propio dentro de una página https | Probado (15/10/2026) |
| Agregar los scripts del portal | `WKUserContentController.addUserScript:` (`WebKitMac.AgregarScript`) | Probado |
| Aceptar cookies de terceros (el portal corre en un marco): `ACCEPT_ALWAYS` | `WKHTTPCookieStore _setCookieAcceptPolicy:` con `NSHTTPCookieAcceptPolicyAlways` (`WebKitMac.AceptarCookiesDeTerceros`) y prevención de rastreo desactivada con `_setResourceLoadStatisticsEnabled:` (las dos son internas de WebKit) | Probado |
| Leer las cookies del portal para el lanzador | `WKHTTPCookieStore getAllCookies:` con un bloque de Objective-C | Probado |
| "Recordarme" en el servicio de secretos (libsecret) | No está: ver abajo | Decidido |

Todo eso está en `src/Plata.Host/WebKitMac.cs`, que habla con Objective-C desde .NET
(`objc_msgSend`). Cada paso se intenta por separado y deja su resultado en el registro
(`~/Library/Logs/UKennedyMiPortalApp/registro.txt`): "macOS: … listo" o "no aplicado".

### Por qué fallaba el inicio de sesión

La ventana es `plata://plata/` y el portal corre en un marco, así que para WebKit sus cookies son
de otro dominio. La política por defecto de WKWebView es `OnlyFromMainDocumentDomain`
(`WKCookiePolicyAllow` se traduce a eso en `WKHTTPCookieStore.mm`): descartaba
`ASP.NET_SessionId`, el inicio de sesión (un postback de ASP.NET) llegaba sin sesión y el portal
volvía a mostrar el formulario. Desactivar la prevención de rastreo no alcanza; hace falta aceptar
todas las cookies, como en Linux. `_setCookieAcceptPolicy:` existe desde macOS 12, el mínimo del
paquete. Se aplica cuando se crea la ventana, antes de que corra su bucle de eventos, así que
`ventana.html` y el marco del portal se piden después.

### "Recordarme" no existe en macOS

El Llavero pide permiso cada vez que una versión de la app con otra firma (cada paquete firmado ad
hoc, cada compilación local) lee la cuenta guardada. Guardarla en un archivo de la app la dejaría
legible para cualquier programa y en los backups. En macOS la casilla no aparece y la app no toca
el Llavero.

## Paquete

- `empaquetado/macos/empaquetar.sh` arma el `.app` (el host autocontenido para `osx-arm64`, el
  motor, `argumentos.txt`, `Info.plist` e ícono) y lo mete en un `.dmg`. El CI lo arma en cada
  cambio (sección *Artifacts*) y en cada versión.
- Con los secretos del repositorio (ver [CONTRIBUTING.md](../CONTRIBUTING.md)), el CI lo firma con
  Developer ID (hardened runtime) y lo notariza con Apple: macOS lo abre sin alertas. Sin ellos,
  por ejemplo en un fork, lo firma ad hoc y macOS pide confirmar la primera apertura.
- Solo Apple Silicon por ahora. El script acepta `x64`, pero el CI no arma ese paquete.

## Cómo ayudar

Si tenés una Mac, probá la app y contá cómo te fue en un issue con la etiqueta **macos**. Hace
falta sobre todo alguien con una Mac con Intel.
