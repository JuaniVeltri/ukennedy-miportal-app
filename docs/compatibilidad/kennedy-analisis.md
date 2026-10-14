# Análisis de compatibilidad — apps de la Universidad Kennedy

Análisis estático de los `.xap` de la universidad, bajo la autorización en
[`../autorizacion.md`](../autorizacion.md). Los `.xap` **no** están en el
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
