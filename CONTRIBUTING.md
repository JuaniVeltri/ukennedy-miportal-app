# Cómo colaborar

¡Gracias por sumarte! Esta guía explica cómo proponer cambios para que sea fácil revisarlos y
sumarlos. Si algo no queda claro, abrí un issue y preguntá.

## Antes de empezar

- Buscá si ya hay un [issue](https://github.com/JuaniVeltri/ukennedy-miportal-app/issues) sobre lo
  que querés hacer. Si no, abrí uno y contá la idea antes de escribir mucho código: así acordamos
  el enfoque.
- Los issues con la etiqueta **buena primera tarea** son un buen punto de partida.
- Al participar aceptás el [código de conducta](CODE_OF_CONDUCT.md).

## Datos personales y archivos de la universidad: nunca al repo

Este proyecto trabaja con sistemas reales de alumnos. Por eso:

- **Nunca** subas archivos `.xap`, InitParams, cookies, enlaces `uksv:`, usuarios ni contraseñas.
  Para pruebas locales con apps reales usá la carpeta `local-corpus/`, que git ignora.
- En issues y PR, **tapá** nombre, legajo, materias, montos y cualquier dato personal de las capturas.
- Las pruebas automáticas usan **datos inventados**: un `.xap` real no puede ser parte de una prueba.
- Con una sesión real, probá solo navegando y consultando. Nada de inscribirse, eliminar ni pagar
  "para probar".

## Preparar el entorno

Requisitos: [.NET 10 SDK](https://dotnet.microsoft.com/download), la carga de trabajo de
WebAssembly (`dotnet workload install wasm-tools`) y WebKitGTK 4.1 (para correr la app en Linux).

```sh
git clone https://github.com/<tu-usuario>/ukennedy-miportal-app
cd ukennedy-miportal-app
dotnet build Plata.slnx
dotnet test Plata.slnx
```

Para correr la app desde el código, mirá la sección "Desarrollo" del [README](README.md).

## El flujo

1. **Fork** del repositorio y una **rama** por cambio, con un nombre que lo describa:
   `fix/rotulo-menu`, `feat/macos-cookies`, `docs/instalar-arch`.
2. **Commits** chicos y descriptivos, en castellano, con el formato `tipo(área): qué cambia`:
   - tipos: `feat` (algo nuevo), `fix` (un error), `docs`, `test`, `refactor`, `chore`;
   - áreas: `motor`, `compat`, `host`, `empaquetado`, `docs`.

   Ejemplo: `fix(compat): los ContentPresenter sin Content toman el del control`.
3. **Pruebas.** Todo cambio de comportamiento lleva su prueba en `tests/`. Corré
   `dotnet test Plata.slnx` antes de abrir el PR.
4. **Pull request** contra `main`, completando la plantilla: qué cambia, por qué y cómo lo
   probaste. Si cambia algo visible, sumá una captura (con los datos tapados).
5. **CI.** Cada PR compila y corre las pruebas en GitHub Actions; tiene que quedar en verde.
6. **Revisión.** Un mantenedor revisa, comenta y, cuando está listo, lo integra (squash merge).

## Estilo del código

- Nombres y comentarios **en castellano**, como el resto del código.
- Los comentarios explican **por qué** (qué hacía Silverlight, qué difiere en OpenSilver), no qué
  hace cada línea.
- Pruebas con xUnit, con nombres del estilo `Metodo_situacion_resultadoEsperado`.
- Cada archivo nuevo empieza con `// SPDX-License-Identifier: MIT`.

## Agregar compatibilidad con algo de Silverlight

La mayoría de los arreglos para que una app de Silverlight funcione caen en uno de estos lugares:

| Si la app… | Dónde |
|---|---|
| usa un tipo que no existe en .NET moderno u OpenSilver | `Reemplazos` en `src/Plata.Motor/Puente/ReferenceRetargeter.cs`, con la implementación en `src/Plata.Compat` |
| llama a un método que falta o se comporta distinto | `LlamadasADesviar` (mismo archivo) y el desvío en `src/Plata.Compat/Desvios.cs` |
| trae XAML que OpenSilver lee distinto | `CargadorDeXaml.Adaptar` en `src/Plata.Compat/CargadorDeXaml.cs` |
| necesita algo del sistema (red, archivos, ventanas) | `src/Plata.Host` |

Documentá en `docs/compatibilidad/` lo que encontraste: qué hacía Silverlight, qué hace
OpenSilver y cómo se resolvió. Es la memoria del proyecto.

## Versiones (mantenedores)

Una etiqueta `vX.Y.Z` en `main` corre las pruebas, arma el paquete de Linux y el `.dmg` de macOS
(firmado y notarizado si están los secretos de abajo) y publica la versión en GitHub:

```sh
git tag v0.2.0 && git push origin v0.2.0
```

### Firma y notarización para macOS (mantenedores)

Con una cuenta de Apple Developer, el CI firma el `.dmg` con un certificado **Developer ID
Application** y lo notariza con Apple: así macOS lo abre sin alertas. Sin estos secretos, el
paquete se firma ad hoc. Se cargan en **Settings → Secrets and variables → Actions** (nunca en
el código, issues ni chats):

| Secreto | Qué es |
|---|---|
| `MACOS_CERTIFICADO_P12` | El certificado Developer ID Application con su clave privada, exportado como `.p12` y pasado a base64 |
| `MACOS_CERTIFICADO_CLAVE` | La contraseña con la que se exportó el `.p12` |
| `MACOS_NOTARIZACION_P8` | El contenido de la clave de la API de App Store Connect (`AuthKey_XXXXXXXXXX.p8`) |
| `MACOS_NOTARIZACION_KEY_ID` | El Key ID de esa clave |
| `MACOS_NOTARIZACION_ISSUER_ID` | El Issuer ID de App Store Connect |

Desde una Mac con [GitHub CLI](https://cli.github.com):

```sh
base64 -i certificado.p12 | gh secret set MACOS_CERTIFICADO_P12
gh secret set MACOS_CERTIFICADO_CLAVE            # pide la contraseña
gh secret set MACOS_NOTARIZACION_P8 < AuthKey_XXXXXXXXXX.p8
gh secret set MACOS_NOTARIZACION_KEY_ID --body XXXXXXXXXX
gh secret set MACOS_NOTARIZACION_ISSUER_ID --body xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```
