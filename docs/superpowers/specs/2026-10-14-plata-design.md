# Plata: reproductor libre de Silverlight. Diseño

- **Fecha:** 2026-10-14
- **Estado:** borrador para revisión
- **Licencia del proyecto:** MIT

## 1. Objetivo

### Problema

Muchas instituciones siguen usando aplicaciones hechas en Microsoft Silverlight. Microsoft dejó de dar soporte a Silverlight en 2021 y los navegadores ya no lo aceptan.

La Universidad Kennedy, por ejemplo, ofrece Académico, Cuentas Corrientes e Ingreso a través del "Visor de Aplicaciones UK". Ese visor solo funciona en Windows: es Internet Explorer más el plugin de Silverlight. Los alumnos que usan Mac o Linux no tienen forma de acceder a esas aplicaciones.

### Solución

Plata es un reproductor de código abierto para Linux, macOS y Windows. Ejecuta aplicaciones Silverlight **ya compiladas** (archivos `.xap`) **sin recompilarlas**. Para eso usa OpenSilver, que es una reimplementación de Silverlight con licencia MIT.

La pieza que se agrega es un **puente**: hace que el código compilado para Silverlight corra sobre OpenSilver.

### Criterios de éxito

1. Un alumno de la Kennedy, con Linux o macOS, puede:
   - entrar a MiPortal desde Plata;
   - tocar "Académico", "UK Cuentas Corrientes" o "Ingreso";
   - usar todas las pantallas de esas tres aplicaciones con el mismo resultado que en Windows.
2. Las mismas tres aplicaciones también funcionan en Windows con Plata.
3. Plata se puede instalar en Linux (Flatpak y AppImage), macOS (`.dmg`) y Windows (instalador), con documentación en español e inglés.
4. Plata publica una tabla de compatibilidad, generada automáticamente a partir de una colección de aplicaciones Silverlight de prueba.
5. Todos los cambios pasan pruebas automáticas en los tres sistemas operativos antes de publicarse.

### Fuera de alcance (v1)

- Reimplementar Silverlight desde cero. Usamos OpenSilver y, cuando algo falta, lo agregamos ahí.
- Redistribuir aplicaciones de terceros. Plata solo descarga cada aplicación desde su servidor de origen cuando el usuario la abre.
- Video protegido (PlayReady/DRM) y Smooth Streaming.
- Las funciones "fuera del navegador" con confianza elevada de Silverlight, como el acceso libre a archivos o COM.
- Ejecutar el código de las aplicaciones fuera del encierro (modo "suelto"). Se descartó por seguridad.
- Ser un navegador de uso general. El modo portal es solo para sitios de confianza.

## 2. Experiencia de uso

### Instalación

El usuario instala Plata una sola vez. Durante la instalación, Plata se registra como el programa que abre los links `uksv:` y `plata:`. En Windows, si el visor oficial ya está instalado, el instalador pregunta cuál de los dos debe abrir `uksv:`, y por defecto conserva el oficial.

### Flujo desde MiPortal (modo portal)

1. El usuario abre Plata y toca **"Entrar a MiPortal"**.
2. Plata abre `miportal.kennedy.edu.ar` en su propia ventana y el usuario inicia sesión en la página de la universidad. Plata nunca lee ni guarda la contraseña.
3. El usuario toca "Académico". MiPortal ofrece el visor porque, para los sitios del modo portal, Plata se identifica como Windows (ver 4.2).
4. MiPortal genera el link `uksv:https://…/UKSV/Launcher.aspx?…`. Plata lo atrapa dentro de su propia ventana y abre la aplicación.

### Flujo desde el navegador del usuario

Si el navegador entrega un link `uksv:` (porque el sitio lo ofrece o por la extensión futura), el sistema operativo abre Plata con ese link. A partir de ahí el flujo es el mismo.

### Otros modos de apertura

El usuario también puede:
- abrir un archivo `.xap` local;
- abrir una dirección `https://…/app.xap`;
- abrir una página que contenga un objeto Silverlight.

Esto sirve para cualquier aplicación Silverlight y para el desarrollo del proyecto.

### Ventanas

Cuando la aplicación pide abrir reportes, documentos o PDF, se abren en ventanas nuevas de Plata con la misma sesión. Esto replica el comportamiento del visor oficial: ventanas nuevas para `kennedy.edu.ar` y el navegador del sistema para cualquier otro dominio.

### Errores

Plata nunca se cierra sin explicar qué pasó. Siempre muestra un mensaje en español y en lenguaje simple, con un botón "Ver detalles" y otro "Compartir informe" (ver 4.10).

## 3. Arquitectura

```
┌──────────────────────────── Plata (proceso nativo, .NET 10 + Photino.NET) ───────────────────────────┐
│  Lanzador · Modo portal · Portero (red) · Sitios de confianza · Gestor de ventanas · Registro uksv:  │
│                                   ▲                 ▲                                                │
│                    mensajes (JSON)│                 │ esquema propio plata-net:// (HTTP)             │
│  ┌────────────────────────────────┴─────────────────┴─────────────────────────────────────────────┐  │
│  │ WebView del sistema (WebKitGTK / WKWebView / WebView2)                                        │  │
│  │  ┌────────────────────── Motor (WebAssembly, encerrado) ────────────────────────────────────┐ │  │
│  │  │ OpenSilver (runtime) · Cargador XAP · Traductor de referencias (Mono.Cecil)              │ │  │
│  │  │ Cargador de pantallas (XAML) · Arranque · Emulación HtmlPage · Desvío de red · Informe   │ │  │
│  │  └──────────────────────────────────────────────────────────────────────────────────────────┘ │  │
│  └────────────────────────────────────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

El sistema tiene dos partes.

**El host nativo** maneja todo lo que necesita al sistema operativo:
- las ventanas;
- el registro del link `uksv:`;
- la red real;
- la lista de sitios de confianza.

El host **no ejecuta** código de ninguna aplicación Silverlight.

**El motor** corre dentro de un WebView, compilado a WebAssembly. Ahí se carga y se ejecuta el código de la aplicación Silverlight, con el mismo encierro que da un navegador: no tiene acceso a archivos, procesos ni red directa.

El host y el motor se comunican por dos canales:
- **Mensajes JSON**, por el canal de mensajes de Photino, para órdenes como abrir ventanas, pedir confirmaciones o enviar informes.
- **Un esquema propio, `plata-net://`**, para todo el tráfico HTTP de la aplicación. El host lo atiende como un proxy.

El host sirve los archivos del motor mediante el esquema `plata://`, así que no depende de ningún servidor local.

## 4. Componentes

Para cada componente se indica qué hace, su interfaz y sus dependencias.

### 4.1 Lanzador (host)

**Qué hace.** Recibe la orden de apertura: un link `uksv:`, un archivo `.xap`, una dirección o una página.

Un link `plata:<dirección absoluta>` abre esa dirección (una página o un `.xap`) sin transformarla. Es la forma genérica para cualquier institución.

En el caso de `uksv:` reproduce exactamente lo que hace el visor oficial:
1. le quita el esquema;
2. valida que el resultado sea una dirección absoluta;
3. le agrega `&v=3`.

Después pide la página de inicio a través del Portero. En el HTML busca el objeto Silverlight: `<object type="application/x-silverlight-2">` con sus parámetros `source` e `initParams`, o la llamada a `Silverlight.createObject(...)`. Con eso arma un **pedido de ejecución**.

**Interfaz.** `Task<PedidoDeEjecucion> ResolverAsync(OrdenDeApertura)`. El pedido de ejecución contiene:
- la dirección del `.xap`;
- los `InitParams`;
- la dirección de la página (que la aplicación ve como `HtmlPage.Document.DocumentUri`);
- el origen.

**Depende de:** Portero y Sitios de confianza.

### 4.2 Modo portal (host)

**Qué hace.** Abre una ventana de navegación limitada a los sitios de confianza que tienen "modo portal" activado; para la Kennedy, `*.kennedy.edu.ar`. En esa ventana:
- se presenta con un identificador de navegador de Windows, tanto en la cabecera `User-Agent` como en `navigator.userAgent` y `navigator.platform`;
- intercepta la navegación a `uksv:` y la pasa al Lanzador;
- abre en el navegador del sistema cualquier link fuera de los sitios del portal.

**Interfaz.** `AbrirPortal(SitioDeConfianza)`.

**Depende de:** Sitios de confianza y Lanzador.

**Por qué existe.** MiPortal responde al botón "Académico" (un `__doPostBack('ctl00$main$ukAcademicoUKSV$sauc','')`) con el cartel "La aplicación no está disponible en este equipo" cuando el navegador no es Windows. La causa supuesta es la detección por identificador de navegador, y se verifica en el hito 3.

### 4.3 Cargador XAP (motor)

**Qué hace.** Descarga el `.xap` a través del Desvío de red. Lo abre como un ZIP en memoria y lee `AppManifest.xaml`:
- `EntryPointAssembly`;
- `EntryPointType`;
- `RuntimeVersion`;
- `Deployment.Parts` / `AssemblyPart`;
- `Deployment.ExternalParts` / `ExtensionPart`.

Las `ExtensionPart` se descargan desde la misma base que el `.xap`. Nada se escribe en disco.

**Interfaz.** `Task<PaqueteXap> CargarAsync(Uri)`. `PaqueteXap` expone el manifiesto, las librerías (en bytes) y los recursos (imágenes, XAML sueltos y fuentes).

**Depende de:** Desvío de red.

### 4.4 Traductor de referencias (motor)

**Qué hace.** Antes de cargar cada librería de la aplicación, la reescribe **en memoria** con Mono.Cecil. Para cada referencia de ensamblado aplica la siguiente tabla:

| Referencia original (Silverlight) | Destino |
|---|---|
| `mscorlib`, `System`, `System.Core`, `System.Xml`, `System.Net`, `System.Runtime.Serialization` | Librerías equivalentes de .NET 10 |
| `System.Windows`, `System.Windows.Browser`, `System.ServiceModel` y similares | OpenSilver |
| Librerías del SDK de Silverlight incluidas en el `.xap` (por ejemplo `System.Windows.Controls`, `System.Windows.Controls.Data`, Toolkit) | No se cargan. Se usan sus equivalentes de OpenSilver |
| Cualquier otra librería (la aplicación y sus terceros) | Se carga traducida de la misma forma |

Las librerías del SDK se reconocen por su nombre y por la clave pública de Microsoft.

**Referencias sin resolver.** Para cada tipo o miembro que no existe en el destino, se aplica este orden:
1. si existe un **reemplazo** en `Plata.Compat`, se usa;
2. si no, la referencia se registra en el Informe de compatibilidad y se reemplaza por un lanzamiento de `PlataNoSoportadoException`, que trae el nombre exacto de lo que falta.

Así, la aplicación funciona hasta el punto exacto en que necesita lo que falta, en vez de fallar al cargar.

**Interfaz.** `Assembly Traducir(byte[] original, ContextoDeTraduccion)`.

**Depende de:** Mono.Cecil, el catálogo de tipos de OpenSilver (generado al compilar Plata) y `Plata.Compat`.

### 4.5 Cargador de pantallas (motor)

**Qué hace.** Silverlight guarda el XAML de cada pantalla dentro de la librería, en `<Ensamblado>.g.resources`. La aplicación lo pide con `Application.LoadComponent(this, new Uri("/Ensamblado;component/Pantalla.xaml", UriKind.Relative))` y después busca sus controles con `FindName`.

Plata registra un cargador para esas direcciones que:
1. lee el XAML del recurso;
2. lo procesa con el lector de XAML en tiempo de ejecución de OpenSilver, usando `XamlObjectWriterSettings.RootObjectInstance` para aplicarlo sobre el objeto que ya existe;
3. registra los nombres en el ámbito de nombres de la pantalla, para que funcione `FindName`;
4. conecta los eventos declarados en el XAML (por ejemplo `Click="Guardar_Click"`) con los métodos de la pantalla, mediante reflexión;
5. resuelve las direcciones de recursos `/Ensamblado;component/...` y las rutas relativas al `.xap`.

**Interfaz.** Es una implementación de `IXamlComponentLoader` de OpenSilver, más un resolvedor de recursos.

**Depende de:** OpenSilver (`XamlReader` y `System.Xaml`) y el Cargador XAP.

### 4.6 Arranque (motor)

**Qué hace.** Crea la clase de inicio `EntryPointType`, que hereda de `System.Windows.Application`. Además:
- carga los `InitParams` en `StartupEventArgs`;
- dispara `Startup`;
- atiende `UnhandledException` como lo hacía Silverlight: si la aplicación no lo maneja, Plata muestra el error (ver sección 6);
- informa la página de origen en `Application.Current.Host.Source`.

**Interfaz.** `Task EjecutarAsync(PaqueteXap, PedidoDeEjecucion)`.

**Depende de:** el Traductor, el Cargador de pantallas y OpenSilver.

### 4.7 Emulación de HtmlPage (motor y host)

**Qué hace.** Atiende las llamadas de la aplicación a la página que la contenía:
- `HtmlPage.Document.DocumentUri` y `QueryString` devuelven los datos de la página de inicio.
- `HtmlPage.Window.Navigate` y `NavigateToNewWindow` se envían como mensaje al Gestor de ventanas del host, que aplica las reglas del visor oficial descritas en la sección 2.
- `HtmlPage.Window.Invoke` y `Eval` ejecutan un conjunto mínimo de funciones conocidas. Si se pide algo desconocido, se registra en el Informe de compatibilidad.

**Depende de:** OpenSilver (`System.Windows.Browser`) y el canal de mensajes.

### 4.8 Desvío de red (motor) y Portero (host)

**Desvío de red.** OpenSilver hace las conexiones con `XMLHttpRequest`. El motor instala un desvío que manda **todas** las conexiones de la aplicación a `plata-net://` con la dirección real. Esto incluye `WebClient`, `HttpWebRequest` y los clientes WCF y RIA.

**Portero.** Atiende `plata-net://` en el host y:
1. **aplica la política de Silverlight**:
   - permite el origen del `.xap`;
   - para cualquier otro dominio, descarga y respeta `clientaccesspolicy.xml` y, si falta, `crossdomain.xml`, con las mismas reglas que Silverlight;
2. hace la conexión real con `HttpClient`, usando la sesión de esa aplicación (cookies **solo en memoria**);
3. devuelve la respuesta al motor, con las cabeceras necesarias para que el WebView la acepte;
4. aplica límites: tiempo de espera, tamaño máximo de respuesta, y solo `https` (salvo que el origen de la aplicación sea `http`).

**Interfaz.** Del lado del motor es transparente. Del lado del host: `Task<RespuestaHttp> AtenderAsync(PedidoHttp, SesionDeAplicacion)`.

**Depende de:** Sitios de confianza.

### 4.9 Sitios de confianza (host)

**Qué hace.** Antes de ejecutar una aplicación o abrir un portal, verifica que el dominio de origen sea de confianza.

La primera vez pregunta: "¿Confiás en kennedy.edu.ar para abrir aplicaciones?", con las opciones Una vez / Siempre / No. La decisión se guarda en la configuración del usuario. Solo se guarda el dominio, nunca direcciones con tokens.

Plata trae una lista de portales conocidos, con su configuración de modo portal (por ejemplo `miportal.kennedy.edu.ar`), pero no aprueba nada sin consultar al usuario.

**Interfaz.** `Task<bool> AutorizarAsync(Origen, Motivo)`.

### 4.10 Informe de compatibilidad (motor y host)

**Qué hace.** Junta:
- referencias sin resolver;
- errores de XAML;
- llamadas a `HtmlPage` desconocidas;
- excepciones no manejadas.

Les quita los datos personales: no incluye direcciones con parámetros, cookies ni contenido de la aplicación. El resultado es un JSON con:
- el nombre y la versión de la aplicación (del manifiesto);
- los nombres de los tipos y miembros que faltan;
- la versión de Plata.

"Compartir informe" abre un *issue* de GitHub ya completado, y solo lo hace si el usuario lo pide.

## 5. Seguridad y privacidad

| Amenaza | Mitigación |
|---|---|
| Una página web cualquiera dispara un link `uksv:` o `plata:` hacia una aplicación maliciosa | Se pide confirmación de sitio de confianza antes de ejecutar nada, y la aplicación corre encerrada en WebAssembly |
| Una aplicación (`.xap`) maliciosa | El encierro del WebView impide acceder a archivos, procesos o red directa. La red pasa por el Portero, con la política de Silverlight |
| Robo de la sesión | Las cookies y los tokens viven solo en memoria y se borran al cerrar. Los registros nunca muestran parámetros de las direcciones |
| Que Plata capture credenciales | En el modo portal el usuario escribe su contraseña en la página de la institución. Plata no lee los campos del formulario ni guarda contraseñas |
| Actualizaciones adulteradas | Las actualizaciones van firmadas y se verifica la firma antes de instalar |

Además:
- **Sin telemetría.** Plata no envía nada a ningún lado salvo que el usuario lo pida.
- **Política de seguridad pública** (`SECURITY.md`), con un canal privado para reportar fallas.
- **Revisión de seguridad dedicada** del Portero, el encierro y el modo portal antes de cada versión.

## 6. Manejo de errores

| Situación | Qué ve el usuario |
|---|---|
| No se pudo descargar la página o el `.xap` | "No pudimos conectar con kennedy.edu.ar. Revisá tu conexión." con el botón Reintentar |
| Sitio no confiable o rechazado | Se explica y no se ejecuta nada |
| Falta algo para esta aplicación | "Esta parte de la aplicación todavía no funciona en Plata." con los botones Detalles y Compartir informe. El resto de la aplicación sigue funcionando |
| Error de la aplicación (excepción no manejada) | Un cuadro equivalente al de Silverlight, con detalles técnicos que se pueden copiar |
| La sesión expiró | "Tu sesión terminó. Volvé a entrar desde MiPortal." con un botón para abrir el portal |

Cada error tiene un código estable (por ejemplo `PLATA-RED-001`) que aparece en la documentación.

## 7. Pruebas

- **Pruebas unitarias** con xUnit para cada componente: el lector del manifiesto, la tabla y las reglas del Traductor, el Cargador de pantallas (con XAML de ejemplo), el procesamiento de `clientaccesspolicy.xml` y `crossdomain.xml`, el Lanzador (con HTML de ejemplo, incluido el formato `uksv:` + `&v=3`) y los Sitios de confianza. Las pruebas se escriben antes que el código.
- **Pruebas del motor en un navegador sin pantalla.** Como el motor es web, se prueba con Playwright en Chromium y WebKit: carga de `.xap`, armado de pantallas, eventos y red mediante un Portero de prueba.
- **Servidor de prueba** que imita los servicios típicos de una institución: páginas de inicio con objeto Silverlight, servicios WCF (`basicHttpBinding` con codificación XML y binaria) y RIA. Así ninguna prueba usa servidores reales.
- **Colección de compatibilidad.** Aplicaciones Silverlight reales con licencia abierta y `.xap` disponible: ejemplos del SDK y del Toolkit, y aplicaciones de código abierto. Cada una tiene un guion que la abre, verifica que no haya errores y que estén los controles clave, y saca capturas para detectar cambios. El resultado genera la tabla de compatibilidad pública.
- **Pruebas en el host de cada sistema.** En Linux, macOS y Windows se verifica que se abra la ventana, que funcione el registro de `uksv:`, el esquema `plata-net://` y el canal de mensajes.
- **Integración continua** con GitHub Actions en Linux, macOS y Windows, en cada cambio. Solo se publica si todo pasa.
- **Aplicaciones de la Kennedy.** Se prueban solo en máquinas de colaboradores con cuenta propia, con una lista de chequeo de pantallas que vive en `docs/compatibilidad/kennedy.md`. Los `.xap` reales y las capturas con datos personales no se suben nunca al repositorio: `.gitignore` excluye `*.xap` fuera de `tests/corpus/`.

## 8. Proyecto de código abierto

**Estructura del repositorio:**

```
src/Plata.Host/        host nativo (Photino.NET)
src/Plata.Motor/       motor WebAssembly (OpenSilver + puente)
src/Plata.Compat/      reemplazos de API faltantes
tests/                 pruebas unitarias, del motor y del host
tests/corpus/          aplicaciones de la colección de compatibilidad (con licencia abierta)
tests/servidor/        servidor de prueba
docs/                  guía de usuario y de desarrollo (es/en), compatibilidad, diseño
packaging/             Flatpak, AppImage, dmg, instalador de Windows
```

**Documentación.** `README` en español e inglés, guía de usuario con capturas, guía de desarrollo, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md` y `CHANGELOG.md`.

**Calidad.**
- Todo cambio entra por *pull request*, con revisión de código y pruebas en verde.
- Formato y análisis estático automáticos.
- Lo que falte en OpenSilver se propone allí (*upstream*) cuando sea general.

**Nombre e imagen.** Plata se presenta como "compatible con aplicaciones Silverlight". No usa el logo ni la marca de Microsoft como propia.

**Publicación.**
- **Linux:** Flathub y AppImage.
- **Windows:** instalador firmado, con un servicio de firma gratuito para proyectos de código abierto.
- **macOS:** `.dmg`. La firma y notarización requieren una cuenta de Apple Developer (99 USD por año); sin ella se publica sin firmar, con instrucciones para abrirlo.
- Actualizaciones automáticas firmadas.

## 9. Hitos

Cada hito termina con su criterio de salida cumplido y las pruebas en verde.

1. **Esqueleto.** El repositorio con integración continua en los 3 sistemas. El host Photino abre el motor OpenSilver en WebAssembly y muestra una pantalla de prueba en Linux, macOS y Windows.
2. **Prueba de fuego del puente.** Una aplicación Silverlight de ejemplo, compilada con el SDK original (un formulario con botones, `DataGrid` y una llamada de red al servidor de prueba), corre en Plata **sin recompilar**.
   - Este hito decide si el enfoque es viable. Si falla, se revisa el diseño antes de seguir.
3. **Lanzador, Portero y modo portal.**
   - Se confirma la causa del cartel de MiPortal.
   - Funcionan los links `uksv:` registrados en los 3 sistemas.
   - Una aplicación del servidor de prueba se abre desde un portal de prueba.
4. **Colección de compatibilidad.** Al menos 10 aplicaciones en la colección, con la tabla de compatibilidad generada automáticamente.
5. **Kennedy al 100 %.** Académico, UK Cuentas Corrientes e Ingreso pasan su lista de chequeo en Linux y macOS.
6. **Versión 1.0.** Instaladores firmados, documentación completa, publicación en Flathub y anuncio a la comunidad.

## 10. Riesgos

| Riesgo | Mitigación |
|---|---|
| El lector de XAML en tiempo de ejecución de OpenSilver no cubre todo lo que usan las aplicaciones compiladas | Medirlo en el hito 2. Completar lo que falte en OpenSilver o en `Plata.Compat` |
| La carga dinámica de librerías en WebAssembly (intérprete) resulta lenta | Medir en el hito 2. Las aplicaciones de formularios suelen ser livianas. Si no alcanza, se cachean en memoria las librerías ya traducidas durante la sesión |
| WebKitGTK, WKWebView o WebView2 tratan distinto el esquema propio `plata-net://` | Pruebas del host en los 3 sistemas desde el hito 1. Como alternativa, usar el canal de mensajes para la red |
| Photino no permite cambiar el identificador de navegador por sitio en algún sistema | Como alternativa, el modo portal navega a través del Portero, que controla las cabeceras, y se inyecta un script para `navigator` |
| Las aplicaciones de la Kennedy usan controles comerciales (por ejemplo Telerik) | Se pasan por el mismo puente. Lo que falte se cubre en `Plata.Compat` y se prioriza por uso real |
| Servicios WCF con codificación binaria (`msbin1`) o RIA sin soporte en OpenSilver | El servidor de prueba los cubre desde el hito 2. Si falta soporte, se implementa en el motor |
| MiPortal cambia su detección o su flujo | El modo portal se configura por sitio en un archivo, no en código |

## 11. Supuestos que se verifican

- El cartel de MiPortal se debe a la detección del sistema por identificador de navegador (hito 3).
- El formato del link es `uksv:<dirección absoluta de Launcher.aspx>`, y el visor oficial le agrega `&v=3` antes de navegar. Esto se verificó descompilando `VisorAppsUK.exe` versión 3.0.0.0.
- OpenSilver en WebAssembly permite cargar librerías en tiempo de ejecución (hito 1).
