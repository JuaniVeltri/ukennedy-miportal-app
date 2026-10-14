// SPDX-License-Identifier: MIT
// Detecta aplicaciones Silverlight en la página y ofrece abrirlas con Plata.
// Genérico: no trata ningún sitio de forma especial ni cambia el identificador del navegador.
(function () {
  "use strict";

  // Devuelve la lista de apps Silverlight halladas: su .xap absoluto y sus initParams.
  // Separado para poder probarlo (ver detector.test.js).
  function detectarSilverlight(doc, baseHref) {
    const apps = [];
    const nodos = doc.querySelectorAll(
      'object[type="application/x-silverlight-2"], embed[type="application/x-silverlight-2"]'
    );
    for (const nodo of nodos) {
      let source = nodo.getAttribute("source") || nodo.getAttribute("data") || "";
      let initParams = "";
      for (const p of nodo.querySelectorAll("param")) {
        const n = (p.getAttribute("name") || "").toLowerCase();
        if (n === "source") source = p.getAttribute("value") || source;
        if (n === "initparams") initParams = p.getAttribute("value") || "";
      }
      if (!source) continue;
      const absoluto = new URL(source, baseHref).href;
      apps.push({ xap: absoluto, initParams });
    }
    return apps;
  }

  function mostrarAviso(apps) {
    if (document.getElementById("plata-aviso")) return;
    const barra = document.createElement("div");
    barra.id = "plata-aviso";
    barra.textContent = "Esta página usa Silverlight. ";
    const boton = document.createElement("button");
    boton.textContent = "Abrir con Plata";
    boton.addEventListener("click", () => {
      const a = apps[0];
      // El sistema operativo abre Plata con este enlace (esquema registrado por Plata).
      const q = a.initParams ? "?initParams=" + encodeURIComponent(a.initParams) : "";
      window.location.href = "plata:" + a.xap + q;
    });
    barra.appendChild(boton);
    document.documentElement.appendChild(barra);
  }

  if (typeof module !== "undefined" && module.exports) {
    module.exports = { detectarSilverlight }; // para las pruebas
  } else {
    const apps = detectarSilverlight(document, document.baseURI);
    if (apps.length) mostrarAviso(apps);
  }
})();
