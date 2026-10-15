#!/bin/sh
# SPDX-License-Identifier: MIT
# Instala la app para el usuario actual (sin permisos de administrador) en ~/.local.
set -eu
DIR=$(cd "$(dirname "$0")" && pwd)
DATOS=${XDG_DATA_HOME:-$HOME/.local/share}
DESTINO=$DATOS/ukennedy-miportal-app

if ! ldconfig -p 2>/dev/null | grep -q 'libwebkit2gtk-4.1.so.0'; then
  echo "Falta WebKitGTK 4.1, el navegador que usa la app. Instalalo y volvé a correr este script:"
  echo "  Fedora:        sudo dnf install webkit2gtk4.1"
  echo "  Ubuntu/Debian: sudo apt install libwebkit2gtk-4.1-0"
  echo "  Arch:          sudo pacman -S webkit2gtk-4.1"
  exit 1
fi

rm -rf "$DESTINO"
mkdir -p "$DESTINO" "$HOME/.local/bin" "$DATOS/applications"
cp -r "$DIR/app" "$DIR/ukennedy-miportal-app" "$DIR/desinstalar.sh" "$DESTINO/"
ln -sf "$DESTINO/ukennedy-miportal-app" "$HOME/.local/bin/ukennedy-miportal-app"
# Ícono en el tema hicolor: el SVG y PNG de los tamaños más usados (algunos escritorios no usan SVG).
mkdir -p "$DATOS/icons/hicolor/scalable/apps"
cp "$DIR/icono.svg" "$DATOS/icons/hicolor/scalable/apps/ukennedy-miportal-app.svg"
for t in 48 64 128 256 512; do
  mkdir -p "$DATOS/icons/hicolor/${t}x${t}/apps"
  if command -v magick >/dev/null; then magick "$DIR/icono.png" -resize "${t}x${t}" "$DATOS/icons/hicolor/${t}x${t}/apps/ukennedy-miportal-app.png"
  else cp "$DIR/icono.png" "$DATOS/icons/hicolor/${t}x${t}/apps/ukennedy-miportal-app.png"; fi
done
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -f -t "$DATOS/icons/hicolor" >/dev/null 2>&1 || true
sed "s|^Exec=.*|Exec=$DESTINO/ukennedy-miportal-app|" "$DIR/ukennedy-miportal-app.desktop" \
  > "$DATOS/applications/ukennedy-miportal-app.desktop"
command -v update-desktop-database >/dev/null && update-desktop-database "$DATOS/applications" || true
echo "Instalada. Buscala en el menú como \"Universidad Kennedy MiPortal App\" o corré: ukennedy-miportal-app"
