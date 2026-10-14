# Extensión de navegador de Plata

Detecta aplicaciones Silverlight en una página web y ofrece abrirlas con el
reproductor Plata (Mac, Linux, Windows). Es genérica: funciona con **cualquier**
aplicación Silverlight y no trata ningún sitio de forma especial.

## Cómo funciona

El script `detector.js` busca en la página elementos
`<object type="application/x-silverlight-2">` (o `<embed>`), extrae la URL del
`.xap` y sus `initParams`, y muestra un aviso con un botón **"Abrir con Plata"**.
Al tocarlo, el navegador abre un enlace `plata:<url del .xap>`, que el sistema
operativo deriva al reproductor Plata (que registra ese esquema al instalarse).

La extensión **no** descarga ni analiza el `.xap`: solo detecta su URL y se la
pasa a Plata. Tampoco cambia el identificador del navegador ni elude controles
de ningún sitio.

## Probar

- Lógica de detección: `node detector.test.js` (3 casos).
- En Chrome/Edge: `chrome://extensions` → Modo desarrollador → "Cargar
  descomprimida" → carpeta `extension/`.
- En Firefox: `about:debugging` → "Cargar complemento temporal" →
  `manifest.json`.

## Responsabilidad sobre aplicaciones de terceros

La extensión y el reproductor son herramientas neutrales de interoperabilidad,
como Ruffle para Flash o Wine para Windows. Ejecutar una aplicación Silverlight
de un tercero puede estar sujeto a su licencia. Es responsabilidad de quien la
usa tener los permisos necesarios para las aplicaciones propietarias que abra.
