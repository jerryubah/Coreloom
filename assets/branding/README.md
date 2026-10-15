# Coreloom branding

Approved Coreloom Windows icon kit, used exactly as supplied (do not redraw, recolor or re-export it).

| File | Used for |
|---|---|
| `coreloom.ico` | Embedded in `Coreloom.exe` and `CoreloomDiag.exe` (`ApplicationIcon`), window icon, installer icon, shortcuts |
| `png/coreloom-16/24/32/48.png` | Notification-area (tray) icon, picked by DPI |
| `png/coreloom-64.png`, `png/coreloom-128.png` | Sidebar logo and the About page |
| `png/coreloom-512.png` | Startup splash icon (the splash kit's `coreloom-app-icon.png` is this same file, byte for byte) |
| `coreloom-app-icon-1024.png`, `png/coreloom-256.png` | Reference sizes for store/web/README use |
| `dark/coreloom-dark.ico`, `dark/png/coreloom-dark-*.png`, `dark/coreloom-dark-1024.png` | Dark-mode icon (1.4.1): the same mark on a dark glass tile with a blue rim, generated from `coreloom-app-icon-1024.png` by `build/dark-icon.py` (the mark itself is unchanged). The app shows it for its window, taskbar button, tray, sidebar, About page and splash while the dark theme is on; the .exe's own icon stays the light one |
| `coreloom-brand-lockup-reference.png` | Brand reference only; not an application icon |
| `coreloom-size-preview.png` | How the icon looks at several sizes on light and dark backgrounds |
| `splash/splash-screen-preview.png` | Approved look of the startup splash (from the Coreloom splash opener kit) |
| `splash/coreloom-banner-light.png`, `splash/coreloom-banner-dark.png` | Horizontal lockups for dark and light surfaces (brand references; the app draws its wordmark as text) |

`src/Coreloom.App/Coreloom.App.csproj` links these files in as resources, so this folder is the single copy.
Windows caches icons: if an old icon shows after a rebuild, test a freshly named copy of the executable or restart
Explorer before assuming the build is wrong.
