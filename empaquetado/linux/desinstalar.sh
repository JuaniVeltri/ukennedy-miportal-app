#!/bin/sh
# SPDX-License-Identifier: MIT
# Quita la app instalada con instalar.sh.
set -eu
DATOS=${XDG_DATA_HOME:-$HOME/.local/share}
rm -rf "$DATOS/ukennedy-miportal-app"
rm -f "$HOME/.local/bin/ukennedy-miportal-app" \
      "$DATOS/applications/ukennedy-miportal-app.desktop" \
      "$DATOS/icons/hicolor/scalable/apps/ukennedy-miportal-app.svg" \
      "$DATOS"/icons/hicolor/*/apps/ukennedy-miportal-app.png
echo "Desinstalada."
