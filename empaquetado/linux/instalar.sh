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
mkdir -p "$DESTINO" "$HOME/.local/bin" "$DATOS/applications" "$DATOS/icons/hicolor/scalable/apps"
cp -r "$DIR/app" "$DIR/ukennedy-miportal-app" "$DIR/desinstalar.sh" "$DESTINO/"
ln -sf "$DESTINO/ukennedy-miportal-app" "$HOME/.local/bin/ukennedy-miportal-app"
cp "$DIR/icono.svg" "$DATOS/icons/hicolor/scalable/apps/ukennedy-miportal-app.svg"
sed "s|^Exec=.*|Exec=$DESTINO/ukennedy-miportal-app|" "$DIR/ukennedy-miportal-app.desktop" \
  > "$DATOS/applications/ukennedy-miportal-app.desktop"
command -v update-desktop-database >/dev/null && update-desktop-database "$DATOS/applications" || true
echo "Instalada. Buscala en el menú como \"Universidad Kennedy MiPortal App\" o corré: ukennedy-miportal-app"
