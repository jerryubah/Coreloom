# dmgbuild settings for Coreloom's disk image: a window with Coreloom.app on the left, the Applications folder on the
# right and a background that says what to do (drag it across) and how the first opening goes.
# Used by build/mac/bundle.sh:  dmgbuild -s dmg-settings.py -D app=... -D background=... -D icon=... Coreloom out.dmg
import os.path

app = defines["app"]  # noqa: F821 (dmgbuild provides `defines`)
background = defines.get("background", "builtin-arrow")  # noqa: F821
icon = defines.get("icon")  # noqa: F821  (the disk's own icon in Finder and on the desktop)

format = "UDZO"
compression_level = 9
filesystem = "HFS+"
size = None

files = [app]
symlinks = {"Applications": "/Applications"}
hide_extensions = [os.path.basename(app)]

# The window: 660 x 420 points, the size of the background picture.
window_rect = ((240, 160), (660, 420))
default_view = "icon-view"
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False
icon_size = 112
text_size = 13
arrange_by = None
icon_locations = {
    os.path.basename(app): (175, 205),
    "Applications": (485, 205),
}
