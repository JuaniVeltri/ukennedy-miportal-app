# SPDX-License-Identifier: MIT
# Diseño de la ventana del .dmg para dmgbuild (sin Finder ni AppleScript: anda en el CI).
# empaquetar.sh pasa -D app=<.app> -D fondo=<.tiff> -D icono=<.icns>.
import os.path

app = defines["app"]  # noqa: F821 (dmgbuild define "defines")
nombre_app = os.path.basename(app)

files = [app]
symlinks = {"Aplicaciones": "/Applications"}
icon = defines["icono"]  # noqa: F821  ícono del volumen
background = defines["fondo"]  # noqa: F821  fondo-dmg.svg: título, flecha y pie
format = "UDZO"
filesystem = "HFS+"

default_view = "icon-view"
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False
window_rect = ((200, 140), (660, 420))
icon_size = 112
text_size = 13
arrange_by = None
icon_locations = {nombre_app: (180, 205), "Aplicaciones": (480, 205)}
hide_extension = [nombre_app]
