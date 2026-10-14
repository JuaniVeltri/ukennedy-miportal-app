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
