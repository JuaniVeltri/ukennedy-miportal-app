#!/bin/sh
# SPDX-License-Identifier: MIT
# Arma el .dmg para macOS (corre en una Mac: usa sips, iconutil, codesign y hdiutil).
# Uso: empaquetado/macos/empaquetar.sh [versión] [arm64|x64]
#   → dist/ukennedy-miportal-app-<versión>-macos-<arq>.dmg
# Firma: con MACOS_IDENTIDAD (un certificado "Developer ID Application" en el llavero) firma con
# hardened runtime; si además están MACOS_NOTARIZACION_CLAVE (archivo .p8), _KEY_ID y _ISSUER_ID,
# notariza el .dmg con Apple y le agrega el ticket. Sin MACOS_IDENTIDAD, firma ad hoc.
set -eu
RAIZ=$(cd "$(dirname "$0")/../.." && pwd)
VERSION=${1:-0.1.0}
ARQ=${2:-arm64}
NOMBRE="Universidad Kennedy MiPortal App"
TRABAJO=$RAIZ/dist/macos-$ARQ
APP=$TRABAJO/disco/$NOMBRE.app

rm -rf "$TRABAJO"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# El ejecutable y todo lo que necesita (.NET incluido) van en Contents/MacOS, con el motor al lado.
dotnet publish "$RAIZ/src/Plata.Host" -c Release -r "osx-$ARQ" --self-contained -o "$APP/Contents/MacOS"
dotnet publish "$RAIZ/src/Plata.MotorWasm" -c Release -o "$TRABAJO/motor"
cp -R "$TRABAJO/motor/wwwroot" "$APP/Contents/MacOS/motor"
cp "$RAIZ/empaquetado/icono.png" "$APP/Contents/MacOS/"
{ cat "$RAIZ/empaquetado/argumentos.txt"; printf -- '--registro\n~/Library/Logs/UKennedyMiPortalApp/registro.txt\n'; } \
  > "$APP/Contents/MacOS/argumentos.txt"

ICONOS=$TRABAJO/icono.iconset
mkdir -p "$ICONOS"
for t in 16 32 128 256 512; do
  sips -z "$t" "$t" "$RAIZ/empaquetado/icono.png" --out "$ICONOS/icon_${t}x${t}.png" >/dev/null
  sips -z $((t * 2)) $((t * 2)) "$RAIZ/empaquetado/icono.png" --out "$ICONOS/icon_${t}x${t}@2x.png" >/dev/null
done
iconutil -c icns "$ICONOS" -o "$APP/Contents/Resources/icono.icns"
sed "s/__VERSION__/$VERSION/g" "$RAIZ/empaquetado/macos/Info.plist" > "$APP/Contents/Info.plist"
cp "$RAIZ/LICENSE" "$APP/Contents/Resources/"

DMG=$RAIZ/dist/ukennedy-miportal-app-$VERSION-macos-$ARQ.dmg
if [ -n "${MACOS_IDENTIDAD:-}" ]; then
  # De adentro hacia afuera: cada binario (ejecutables y bibliotecas) y después el .app.
  find "$APP/Contents/MacOS" -type f | while read -r f; do
    if file -b "$f" | grep -q "Mach-O"; then
      codesign --force --timestamp --options runtime --entitlements "$RAIZ/empaquetado/macos/permisos.plist" \
        --sign "$MACOS_IDENTIDAD" "$f"
    fi
  done
  codesign --force --timestamp --options runtime --entitlements "$RAIZ/empaquetado/macos/permisos.plist" \
    --sign "$MACOS_IDENTIDAD" "$APP"
  codesign --verify --deep --strict "$APP"
else
  # Firma ad hoc: sin cuenta de Apple Developer no hay firma ni notarización, pero las Mac con
  # Apple Silicon no corren código sin firmar. La primera vez, macOS pide confirmar la apertura.
  codesign --force --deep --sign - "$APP"
fi

ln -s /Applications "$TRABAJO/disco/Aplicaciones"
hdiutil create -volname "$NOMBRE" -srcfolder "$TRABAJO/disco" -ov -format UDZO "$DMG"

if [ -n "${MACOS_IDENTIDAD:-}" ]; then
  codesign --force --timestamp --sign "$MACOS_IDENTIDAD" "$DMG"
  if [ -n "${MACOS_NOTARIZACION_CLAVE:-}" ]; then
    xcrun notarytool submit "$DMG" --key "$MACOS_NOTARIZACION_CLAVE" \
      --key-id "$MACOS_NOTARIZACION_KEY_ID" --issuer "$MACOS_NOTARIZACION_ISSUER_ID" --wait
    xcrun stapler staple "$DMG"
  fi
fi
echo "listo: $DMG"
