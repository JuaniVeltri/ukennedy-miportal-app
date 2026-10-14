// SPDX-License-Identifier: MIT
// Prueba pura de detectarSilverlight con un DOM mínimo simulado. Correr: node detector.test.js
const assert = require("node:assert");
const { detectarSilverlight } = require("./detector.js");

function nodoFalso(attrs, params = []) {
  return {
    getAttribute: (n) => (n in attrs ? attrs[n] : null),
    querySelectorAll: () => params.map((p) => ({ getAttribute: (n) => (n in p ? p[n] : null) })),
  };
}

function docFalso(nodos) {
  return { querySelectorAll: () => nodos };
}

// 1) source relativo en un <object> se resuelve a absoluto.
{
  const doc = docFalso([nodoFalso({ type: "application/x-silverlight-2", source: "ClientBin/App.xap" })]);
  const apps = detectarSilverlight(doc, "https://sitio.example/pagina/");
  assert.strictEqual(apps.length, 1);
  assert.strictEqual(apps[0].xap, "https://sitio.example/pagina/ClientBin/App.xap");
}

// 2) source e initParams en <param> internos.
{
  const doc = docFalso([
    nodoFalso({ type: "application/x-silverlight-2" }, [
      { name: "source", value: "https://sitio.example/App.xap" },
      { name: "initParams", value: "k=1,v=2" },
    ]),
  ]);
  const apps = detectarSilverlight(doc, "https://sitio.example/");
  assert.strictEqual(apps[0].xap, "https://sitio.example/App.xap");
  assert.strictEqual(apps[0].initParams, "k=1,v=2");
}

// 3) sin source no se agrega.
{
  const doc = docFalso([nodoFalso({ type: "application/x-silverlight-2" })]);
  assert.strictEqual(detectarSilverlight(doc, "https://sitio.example/").length, 0);
}

console.log("detector: 3/3 OK");
