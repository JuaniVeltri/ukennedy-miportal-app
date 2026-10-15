#!/bin/sh
# SPDX-License-Identifier: MIT
# Arma el paquete para Linux (x64): el host con .NET incluido, el motor y el instalador.
# Uso: empaquetado/linux/empaquetar.sh [versión]   → dist/ukennedy-miportal-app-<versión>-linux-x64.tar.gz
set -eu
RAIZ=$(cd "$(dirname "$0")/../.." && pwd)
VERSION=${1:-dev}
NOMBRE=ukennedy-miportal-app-$VERSION-linux-x64
SALIDA=$RAIZ/dist/$NOMBRE
TEMP=$RAIZ/dist/.motor

rm -rf "$SALIDA" "$TEMP"
mkdir -p "$SALIDA"
dotnet publish "$RAIZ/src/Plata.Host" -c Release -r linux-x64 --self-contained -p:PublishSingleFile=false -o "$SALIDA/app"
dotnet publish "$RAIZ/src/Plata.MotorWasm" -c Release -o "$TEMP"
cp -r "$TEMP/wwwroot" "$SALIDA/app/motor"
rm -rf "$TEMP"
cp "$RAIZ/empaquetado/argumentos.txt" "$RAIZ/empaquetado/icono.png" "$SALIDA/app/"

cp "$RAIZ/empaquetado/ukennedy-miportal-app" "$RAIZ/empaquetado/icono.svg" "$RAIZ/empaquetado/icono.png" \
   "$RAIZ/empaquetado/linux/ukennedy-miportal-app.desktop" \
   "$RAIZ/empaquetado/linux/instalar.sh" "$RAIZ/empaquetado/linux/desinstalar.sh" \
   "$RAIZ/LICENSE" "$SALIDA/"
tar -C "$RAIZ/dist" -czf "$RAIZ/dist/$NOMBRE.tar.gz" "$NOMBRE"
echo "listo: dist/$NOMBRE.tar.gz"
