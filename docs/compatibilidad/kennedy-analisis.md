# Análisis de compatibilidad — apps de la Universidad Kennedy

Análisis estático de los `.xap` de la universidad, con su autorización. Los `.xap` **no** están en el
repositorio; este documento solo registra su estructura y dependencias.

## Apps

| App | EntryPoint | RuntimeVersion | Librerías |
|-----|-----------|----------------|-----------|
| GGI.Academico | `GGI.Academico.UI.Silverlight.App.App` | 3.0.40624.0 (Silverlight 3) | 25 |
| GGI.CtaCte | `GGI.CtaCte.UI.Silverlight.App.App` | 3.0.40624.0 (Silverlight 3) | 23 |

## Composición de las librerías

Tres grupos:

1. **Código propio de GGI** (lo que hay que correr): `GGI.*.UI.Silverlight.*`
   y el framework base `Bpf.Silverlight`.
2. **SDK / Toolkit de Silverlight, incluidos en el `.xap`**:
   `System.Windows.Controls`, `System.Windows.Controls.Data`,
   `System.Windows.Controls.Data.Input`, `System.Windows.Controls.Toolkit`,
   `System.Windows.Controls.Layout.Toolkit`, `System.Windows.Data`,
   `System.ComponentModel.DataAnnotations`, `System.Xml.Serialization`.
   OpenSilver provee equivalentes: estas no se cargan del `.xap`, se usan las de
   OpenSilver.
3. **Terceros**: `SilverlightContrib`, `SilverlightContrib.Controls`.

## Dependencias de núcleo (lo que el traductor debe mapear)

Todas las librerías dependen del núcleo de Silverlight, que el traductor
redirige a OpenSilver:

- `System.Windows`, `System.Windows.Browser` → OpenSilver
- `System.Windows.Controls` (y familia Toolkit/Data) → equivalentes de OpenSilver
- `System.ServiceModel`, `System.Net`, `System.Runtime.Serialization` →
  OpenSilver / .NET (los datos llegan por servicios WCF del mismo servidor)
- `System.Core`, `System.Xml`, `mscorlib` → .NET moderno

## Veredicto de viabilidad

**Favorable.** Son aplicaciones Silverlight de formularios, de la época de
Silverlight 3, sin dependencias exóticas en la lista: no hay video protegido,
DRM ni Smooth Streaming. Toda la superficie (XAML, controles del Toolkit,
DataAnnotations, servicios WCF) es justo lo que OpenSilver apunta a cubrir.

El único componente de terceros fuera del SDK es **SilverlightContrib**, que es
open source; si usa algo que OpenSilver no tiene, se cubre en `Plata.Compat`.

## Próximo paso (go/no-go real)

Integrar en el motor WASM la carga dinámica: `XapReader` → descartar las
librerías del SDK incluidas → `ReferenceRetargeter` sobre las de GGI y terceros →
`Assembly.Load` → instanciar el EntryPoint → `RootVisual` → render. Falta
precisar, en OpenSilver, a qué ensamblado concreto apunta cada tipo del SDK
(OpenSilver.Controls, OpenSilver.Controls.Data, etc.) para que el traductor no
redirija a un ensamblado único equivocado.

## Resultado del go/no-go (2026-10-14)

Prueba real con `GGI.Academico.UI.Silverlight.App.xap` (local, no versionado),
usando `XapReader` + `PlanDeCarga` + `ReferenceRetargeter` de este repo:

- **Las 13 librerías de GGI y terceros cargan** tras reescribir sus referencias
  de Silverlight a OpenSilver (`Assembly.Load` OK en todas).
- Con un resolvedor de ensamblados (`AppDomain.AssemblyResolve` que devuelve las
  cargadas por bytes), **el tipo de entrada `GGI.Academico.UI.Silverlight.App.App`
  resuelve correctamente** contra OpenSilver.

**Veredicto: GO.** La resolución de tipos —el riesgo principal del proyecto— se
cumple con las apps reales. Falta instanciar y renderizar dentro del runtime WASM
de OpenSilver (el contexto de ejecución real); la parte de enlace binario, que
era la incógnita, funciona.

Aprendizaje a incorporar al motor: hace falta un resolvedor de ensamblados que
sirva las librerías cargadas por bytes a las referencias por nombre.

## Render en WASM — hasta dónde llega (2026-10-14)

Ejecutando el `.xap` real dentro del motor WASM (OpenSilver), con mensajes de
diagnóstico, la secuencia llega hasta:

1. `init` ✓
2. baja el `.xap` (1.333.240 B) ✓
3. carga las 13 librerías y **resuelve el tipo de entrada** `...App.App` ✓
4. entra al arranque de OpenSilver e **invoca el constructor de la App** ✓
5. el **constructor de la App no retorna** (se cuelga, sin excepción); la zona de
   render (`#opensilver-root`) queda vacía (kids=0) tras +120 s.

**Interpretación:** el enlace binario y la resolución de tipos funcionan con la
app real. El bloqueo está en el **arranque propio de la app GGI/Bpf**: lo más
probable es que espere sus `InitParams` (incluye `InitialConfiguration`) y/o una
llamada de configuración a sus servicios, que aquí no existen.

**Próximos pasos (iterativo, varias sesiones):**
- Pasar `InitParams` representativos al arranque (`StartupEventArgs`).
- Stub local de los servicios de configuración de arranque para que no bloquee.
- Si bloquea por una llamada síncrona, identificarla y cubrir ese hueco en
  OpenSilver / `Plata.Compat`.

## Blocker final localizado: unificación de BCL (2026-10-14, cont.)

Tras blindar el resolvedor contra recursión (el cuelgue era un bucle infinito
resolviendo `System.Core`) y publicar sin recorte (`PublishTrimmed=false`, para
que las fachadas de la BCL estén presentes), el motor avanza y falla con un error
claro y repetible:

- Los ensamblados de GGI referencian tipos de la **BCL de Silverlight**
  (`System`, `System.Core`, `System.Xml`, … versión 2.0.5.0), por ejemplo
  `System.ComponentModel.INotifyPropertyChanged` en `System, 2.0.5.0`.
- En .NET moderno esos tipos viven en **otros ensamblados** (p. ej.
  `INotifyPropertyChanged` está en `System.ObjectModel`). Resolver el ensamblado
  por nombre no alcanza porque el **tipo no está donde la referencia lo busca**.

Resolver tipo por tipo no escala. Se necesita una de dos soluciones sistemáticas:

1. **Ensamblados-fachada** con las identidades de Silverlight (`System 2.0.5.0`,
   `System.Core 2.0.5.0`, …) llenos de `[TypeForwardedTo]` a las ubicaciones
   modernas. Se generan una vez y se cargan antes que las apps.
2. **Reescritura de referencias de tipo** (no solo de ensamblado) con Mono.Cecil:
   redirigir cada `TypeReference` de la BCL de Silverlight al ensamblado moderno
   correcto.

La opción 1 es la más limpia y la recomendada. Es el núcleo de trabajo que queda.

## Resumen del estado

- Enlace binario y resolución de **tipos de UI (OpenSilver)**: funciona.
- Carga + resolución + instanciación arranca con la app real.
- Falta: **unificación de la BCL** (fachadas), que es la próxima pieza grande.

## Unificación BCL resuelta; ahora: desviaciones de API de OpenSilver (2026-10-14)

Implementada la unificación de BCL en `ReferenceRetargeter` (reescribe cada
referencia de tipo de la BCL de Silverlight —System, System.Core, System.Xml,
System.Net, System.Runtime.Serialization, System.ComponentModel.DataAnnotations—
al ensamblado moderno que exporta el tipo, resuelto en runtime). El error de
`INotifyPropertyChanged` desapareció.

Ahora el motor entra al **constructor base de la app** (`BaseApplication`) y falla
en una **desviación de API de OpenSilver**:

- `MissingMethodException: System.Windows.Application.add_Exit(System.EventHandler)`.
- En Silverlight real, `Application.Exit` es `event EventHandler`. OpenSilver lo
  declara como `event ExitEventHandler`. GGI, compilado contra Silverlight, llama
  `add_Exit(EventHandler)`, que OpenSilver no tiene.

Este es el tipo de hueco esperado de aquí en más: desviaciones puntuales de API
entre OpenSilver y Silverlight real. Se resuelven de a una, por:
- corrección en OpenSilver (upstream), o
- reescritura dirigida del sitio de llamada en el traductor.

Progreso del arranque: carga ✓ · BCL ✓ · enlace de tipos UI ✓ · entra al ctor
base de la app ✓ · falla en `Application.Exit` (firma).

## Puente completo; frontera = arranque de la app (2026-10-14)

Parche acotado (SaltearApiAusente) que anula la suscripcion a Application.Exit
(firma distinta en OpenSilver, prescindible en un visor). Con eso el constructor
de la App completa sin error y OpenSilver engancha su raiz en el DOM
(#opensilver-root con 2 hijos). El puente (carga -> BCL -> enlace de tipos UI ->
construccion -> runtime OpenSilver vivo) funciona end-to-end con la app real.

Falta para ver la interfaz: el arranque propio de la app (Application_Startup ->
Shell.Instance.Start, ApplicationSettings, roles, servicios) necesita sus
InitParams (InitialConfiguration), configuracion y servicios, ausentes en prueba.
Proximos pasos: (1) InitParams representativos; (2) stub de servicios de arranque;
(3) mas parches de API puntuales de OpenSilver segun aparezcan.

## Arranque llega a la configuración de la app (2026-10-14, cont. 3)

- Los `MissingMethod` de la BCL (`Convert.ToDecimal`, `XmlReader.ReadToFollowing`)
  **no eran un problema de enlace**: eran un `obj/` viejo publicado con recorte
  (el runtime tenía la BCL recortada). Publicando limpio y sin recorte desaparecen;
  hay que borrar `obj/` al cambiar `PublishTrimmed`.
- Se quitan los atributos de ensamblado exclusivos de Silverlight
  (`AssemblyTrademarkAttribute`), que el runtime moderno no puede leer
  (`QuitarAtributosInexistentes`).
- `Application_Startup` → `Shell.Start` llega a
  `ConfigurationManagerFactory.CreateConfigurationManagerFactory(InitialConfiguration)`
  y falla con `NullReferenceException`: la app necesita el parámetro de inicio
  `InitialConfiguration` (JSON `{"Data":"<xml con ConfigurationManagerFactory,
  servicios WCF, ...>"}`) que genera el servidor para cada sesión en
  `Launcher.aspx`.

**Frontera actual:** sin el `initParams` real de la sesión del alumno no se puede
seguir. El portal no lo entrega a clientes Linux/Mac (bloqueo por sistema operativo
en el servidor), por lo que el siguiente paso real requiere que el portal lo
permita (pedido a Sistemas) o un `initParams` de prueba provisto por la universidad.

## Arranque completo con configuración sintética (2026-10-14, cont. 4)

Con un `InitialConfiguration` sintético (armado a partir del código de `Bpf.Silverlight`
y `GGI.Core`, solo local) el arranque de Académico pasa entero: `Shell.Start` termina,
`RootVisual` es `Bpf.Silverlight.UI.RootContainer` y en la página queda el árbol real
de GGI (`RootContainer` → `View` → `SingleShellLayout` → `Grid shellLayout` →
`Canvas ctrlLayout`).

Qué espera la app al arrancar:

- `InitParams` con dos claves: `InitialConfiguration` (JSON `{"Data":"<xml>"}`) y
  `SessionData` (JSON con `UserName`, `Roles`, `Privileges`, `Token`).
- `App.Application_Startup` agrega `Encrypted=True`: todos los valores llegan
  cifrados (AES) y la app los descifra con una clave embebida en `GGI.Core`. Plata no
  necesita la clave: pasa los valores tal como los entrega `Launcher.aspx`. El fixture
  local los cifra igual; la clave no se copia al repositorio.
- El XML de `Data` nombra por reflexión los tipos a crear: `ConfigurationManagerFactory`,
  `ServiceFactory` (`WcfServiceProxyFactory`), los `Service` (nombre de la interfaz y
  dirección), `Shell` (`errorHandlerType`, `securityType`, `ShellLayout`,
  `ShellApplicationLayout`) y `ApplicationSettings`.

Huecos cubiertos en el camino, todos generales (sirven a cualquier app Silverlight):

1. **InitParams:** el host los recibe con `--init-params <archivo>` y los sirve en
   `plata://app/_plata/init-params`; el motor los inyecta como
   `<param name="InitParams">` en `#opensilver-root`, que es de donde los lee
   OpenSilver. No van por la URL: Photino aborta con URLs largas
   (`*** buffer overflow detected ***`).
2. **AES:** .NET en WebAssembly no trae AES (`PlatformNotSupportedException`). El
   traductor reemplaza `AesManaged` por `Plata.Compat.AesManaged` (CBC + PKCS7,
   administrado, verificado contra FIPS-197 y contra el AES del sistema).
3. **`DataContractJsonSerializer`:** vive en la BCL moderna, no en OpenSilver.
   `System.ServiceModel*` pasa a la reubicación tipo por tipo, y la búsqueda carga el
   ensamblado moderno por su espacio de nombres si todavía no estaba cargado.
4. **`ReadObject(Stream)`:** GGI escribe un `MemoryStream` y lo lee sin volver a 0
   (en Silverlight funcionaba). Se desvía a `Plata.Compat.Desvios`, que rebobina solo
   un stream que quedó justo al final.
5. **XAML:** el `Application.LoadComponent` de OpenSilver busca una clase que genera su
   compilador; con apps de Silverlight no carga nada y no avisa. Se desvía a
   `Plata.Compat.CargadorDeXaml`, que lee el XAML de `<Ensamblado>.g.resources` y lo
   aplica sobre la instancia con el lector de XAML de OpenSilver (`RootObjectInstance`,
   ámbito de nombres para `FindName`, eventos al objeto raíz). Los
   `clr-namespace:…;assembly=System.Windows*` se mapean al espacio de presentación.

**Frontera actual:** después del login, el shell pide la lista de aplicaciones a
`IApplicationService` (WCF, `basicHttpBinding`). Sin servidor no llega respuesta y no
se abre ninguna aplicación. Siguen: imágenes y recursos `/Ensamblado;component/…`,
`ResourceDictionary Source` y `themes/generic.xaml`, y la red (Portero) con datos
reales, con autorización de la universidad.

## Académico funcionando con la sesión real (2026-10-15)

Con autorización de la universidad: MiPortal abierto como Windows, el
`initParams` real de `Gestion.aspx` y la red por el host. **Resultado:** el shell de GGI, el
menú de sistemas, el menú lateral de Inscripciones y la pantalla **Inscripción a Materias**
se dibujan con los datos reales del alumno (materias, inscripción vigente, comisiones), igual
que en la guía de la universidad. Navegar entre materias funciona.

### Red: el Portero (diseño §4.8), variante por mensajes

- El motor no puede usar el `HttpClientHandler` que arma WCF (el navegador no admite esas
  propiedades) ni llamar a los servidores de la institución (CORS). `Plata.Compat.TransportePlata`
  reemplaza el transporte HTTP de la binding: arma el sobre SOAP con el encoder y lo manda al
  host como mensaje `plata-net:{json}`; el host (`Plata.Host.Portero`) hace el pedido real con
  cookies solo en memoria y **solo a dominios permitidos** (`--permitir kennedy.edu.ar`).
- Sin WS-Addressing la acción viaja solo en `SOAPAction`: se quita el encabezado SOAP `Action`
  que agrega el encoder (con él, los servicios respondían HTTP 400).
- Las respuestas se entregan **en el orden de los pedidos**: GGI muestra una pantalla solo si
  sus traducciones llegan antes que sus datos (el navegador de la época lo garantizaba en la
  práctica). Sin esto, a veces la pantalla quedaba en "cargando" para siempre.
- `WebClient` de Silverlight → `Plata.Compat.WebClient` (por el Portero, relativo al .xap).
  Los recursos relativos al .xap (imágenes) los sirve el host desde `--origen`.

### WCF en un solo hilo

`ChannelFactory<T>.CreateChannel()`, `Open()` y `Close()` sincrónicos esperan bloqueando
(`Task.Yield` + `WaitForCompletion`) y el navegador no lo permite; el encoder de texto
sincrónico hace `WaitOne` y gira para siempre. Se reemplazan por `CanalDiferido` (abre todo
en forma asincrónica en el primer `BeginX`), `Abort` en vez de `Close`, y las versiones
asincrónicas del encoder.

### Diferencias de API (Silverlight → OpenSilver / .NET moderno)

| Silverlight | Plata |
|---|---|
| `AppDomain.DefineDynamicAssembly`, `DefineDynamicModule(string,bool)` | equivalentes de `AssemblyBuilder` |
| `ScriptObject.Invoke` de una función que la página no tiene | aviso y `null` |
| `SilverlightHost` | `System.Windows.Host` de OpenSilver |
| `Host.Source` (= dirección del .xap) | `Origen.Xap` |
| `Application.GetResourceStream(StreamResourceInfo, Uri)`, `AssemblyPart.Load(Stream)` (en OpenSilver devuelven null) | lectura del ZIP y carga con el mismo traductor |
| `Loaded` (llega con el layout hecho) | `Loaded` diferido al Dispatcher + `UpdateLayout` |
| tipos en argumentos de atributos (`[TemplatePart(Type=…)]`, guardados como texto) | se reubican como las referencias |

### XAML de Silverlight en el lector de OpenSilver

El cargador (`Plata.Compat.CargadorDeXaml`) lee el XAML de `<Ensamblado>.g.resources` y lo
adapta antes de procesarlo:

- `themes/generic.xaml` de cada ensamblado como tema (`TemaPlata`); `ResourceDictionary Source`
  (relativos o `/Ensamblado;component/…`) cargados y fusionados.
- `VisualStateManager.VisualStateGroups` → `plata:Estados.Grupos` (el lector de OpenSilver no
  crea la colección).
- Templates: las declaraciones `xmlns` de la raíz viajan al contenido (se procesa al aplicarse,
  sin ellas); sus `StaticResource` encuentran los recursos de la pantalla por el tema; los
  eventos (`LoadingRow="…"`) se conectan al code-behind de la pantalla.
- `x:Name` dentro de recursos también es clave; bindings `OneTime` se reevalúan al cargarse
  el elemento; `ContentPresenter` sin `Content` en templates de `ContentControl` toma el del
  control (como en Silverlight).

### Herramientas de desarrollo

- **Arnés con Playwright** (fuera del repo por ahora): corre el motor en Chromium y hace de
  host; capturas de la página, árbol visual (`PlataArbol`), excepciones con stack (modo
  `--diagnostico`) y **rastreo de métodos** (el traductor inserta una traza al entrar a los
  métodos pedidos).
- Con la sesión real del alumno no se interactúa con la app desde la automatización (la
  pantalla de inscripción opera sobre datos reales).

### Pendiente (al 2026-10-15, antes del portal integrado)

- ~~Cuentas Corrientes (segundo `.xap`).~~ Abre (ver abajo).
- ~~Integrar el flujo completo en el host.~~ Hecho (ver abajo).
- `KeepAliveWebSession`: mantener viva la sesión ASP.NET de la página.
- Política de dominios: hoy explícita por argumento; falta `clientaccesspolicy.xml`.

## Portal integrado y ventana con pestañas (2026-10-15, cont.)

`Plata.Host --portal https://miportal.kennedy.edu.ar/ --portal-nombre MiPortal --permitir kennedy.edu.ar`
abre una sola ventana con una barra de pestañas (`src/Plata.Host/Ventana/ventana.html`). La primera
pestaña es el portal: el alumno inicia sesión ahí como siempre. Al hacer clic en "UK Académico" o
"UK Cuentas Corrientes", Plata hace lo que hacía el visor de Windows y abre la app en una pestaña
nueva. Las dos apps abren y cargan datos con la sesión real.

Cómo funciona:

- **User-Agent de Windows** solo en esta ventana (autorizado por la universidad): el portal
  solo ofrece las apps a Windows.
- **El enlace `uksv:`.** El portal lo pone en un iframe oculto. Plata registra el esquema `uksv` en
  el navegador de la ventana. En WebKitGTK un esquema propio cuenta como inseguro y una página
  https no lo carga en un iframe (contenido mixto): se lo registra como seguro
  (`webkit_security_manager_register_uri_scheme_as_secure`, `WebKitGtk.cs`).
- **"¿Tenés el visor instalado?"** El portal lo pregunta con `checkUKSVCookie()`, que busca la
  cookie `uksvCookie` (la pone su botón "Si ya instalaste el visor hacé clic aquí"). Dentro de la
  ventana el portal corre en un marco y WebKit no le deja leer ni escribir cookies desde JavaScript
  (probado: `document.cookie` vacío aun aceptando cookies de terceros y sin ITP). Un script de
  Plata en las páginas del portal reemplaza `checkUKSVCookie()` por una que responde que sí
  (`Lanzador.ScriptDelPortal`). Las cookies que pone el servidor sí se guardan (con la política
  "aceptar siempre"): el inicio de sesión funciona.
- **El lanzador** (`Lanzador.cs`): pide `Launcher.aspx` con `&v=3` por el Portero, sigue las
  redirecciones por JavaScript hasta la página de la app (`Gestion.aspx`, `Administracion.aspx`) y
  saca el `.xap` y los InitParams (también del HTML escapado dentro de `Sys.UI.Silverlight.Control`).
  El enlace vence al primer uso ("El enlace ha expirado").
- **Cookies del portal.** La sesión del alumno viaja en cookies del dominio `.kennedy.edu.ar`
  que pone el portal (`.GGISECURECOOK`, `GGIReg`…); sin ellas Cuentas Corrientes redirige a
  `Error/AccesoNoAutorizado.aspx`. Antes de pedir el lanzador, Plata copia al Portero las cookies
  que el navegador mandaría (`CookiesDelNavegador.cs`, API de cookies de WebKitGTK). Las páginas
  de las apps agregan otras por `Set-Cookie` (`.EXTRAGGISECURECOOK`, `EXTRATOKENVALUE`, `__RouteID`),
  que quedan en el Portero. Un segundo inicio de sesión del mismo alumno en paralelo (otro
  navegador) invalida la sesión para Cuentas Corrientes: `AUTHENTICATION_INVALID_PASSWORD_BAD_USER`.
- **Una app por pestaña, cada una en su origen** (`plata://app1/`, `plata://app2/`): el host atiende
  cada origen con su `SesionDeApp` (InitParams, origen del `.xap`, página). El motor corre en un
  marco y sus mensajes al host pasan por la ventana (`postMessage`), que sabe de qué pestaña
  vienen; llevan una marca que solo conoce la página de la ventana.
- **Carga.** Al hacer clic en una tarjeta, un modal muestra "Abriendo aplicación…" (aviso del
  script del portal) hasta que aparece la pestaña; si falla, el mismo modal da el motivo. Cada pestaña nueva muestra "Abriendo …" hasta que el motor avisa
  `plata:visible` (la app ya tiene `RootVisual`).
- **Documentos** (`Documentos.cs`): lo que la app abre con `HtmlPage.Window.Navigate` (reportes,
  comprobantes, el formulario de pago) se resuelve contra la página de la app. Si el dominio está
  permitido, se baja por el Portero (con la sesión) y se abre con el programa del sistema; si no,
  en el navegador del sistema. Solo http/https. Probado: "Pagar" abrió la pasarela de pago.

Limitaciones conocidas:

- Las APIs de WebKit que Photino no expone (esquema seguro, política de cookies, cookies, scripts)
  se llaman directo a `libwebkit2gtk-4.1`: solo Linux. En macOS hay que resolverlo con WKWebView.
- `document.cookie` no funciona en el portal enmarcado: si otra función del portal depende de
  cookies escritas por JavaScript, va a fallar igual que el cartel del visor.
- Rótulo vertical del menú lateral de GGI ("MENU - …"): con el `RenderTransformOrigin` 0.5,0.5
  del XAML queda a 80 px y cortado arriba; en Silverlight sale sobre la franja gris, que es lo que
  dan las cuentas con origen 0,0 (y lo que supone la traslación 18,5 del diseñador). Es el único
  elemento de GGI con `Effect` + `RenderTransform` + origen no nulo; `CargadorDeXaml.OrigenConEfecto`
  aplica esa regla solo a esa combinación. Falta confirmarlo con otras apps.

### Red de las apps: reglas de Silverlight

Lo que pide una app en ejecución sigue las reglas de Silverlight (`PoliticaDeAcceso.cs`): su sitio
de origen siempre; otro sitio, solo si su `/clientaccesspolicy.xml` lo permite (o un
`/crossdomain.xml` abierto a todos). Los servidores de servicios de la universidad publican
políticas abiertas a `http://*` (y `servicios1`, también a `https://*`). `--permitir` queda para
lo que pide el host: portal, lanzador, `.xap` y documentos.

### Pendiente

- `KeepAliveWebSession`: mantener viva la sesión ASP.NET de la página.
- macOS ([plan](../macos.md)).
