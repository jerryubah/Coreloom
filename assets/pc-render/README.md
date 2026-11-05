# The PC render

`render.png` is the picture of a PC in the Overview header. Since 1.9.3 it is cut out of `render-source.png` (Jerry's
render on its dark background) by `build/pc-render-cutout.py`: a segmentation model takes the background and the
floor's reflection away, then the case is made solid inside its outline and its feet solid too. It's split into layers
by `build/pc-render.py`, which writes them to `src/Coreloom.App/Assets/PcRender/` and generates `src/Coreloom.App/Views/PcIllustration.xaml`:

- the glow is separated from the rest, so the app can relight it in any color;
- the fans' blades are separated from their lighting, so the app can turn them;
- the text on the pump screen and the graphics card is painted out, so the app can draw the live values.

## Replacing the render

1. Export the new render as a PNG, about 1100 × 1400 px, lit in one blue (the script separates that hue). Keep the fans
   visible from the front or at a shallow angle. With a transparent background, save it here as `render.png`; with a
   background, save it as `render-source.png` and run `python3 build/pc-render-cutout.py` (needs `pip install rembg`;
   check its `FEET` boxes) to make `render.png`.
2. Look at `render.png` over a bright color: nothing of the background or floor, nothing see-through inside the case.
3. Update the coordinates at the top of `build/pc-render.py` if anything moved: `CROP`, the fan ellipses (`FANS`),
   the text boxes to paint out (`TEXT_BOXES`) and where the live text goes (`SCREEN_VALUE`, `SCREEN_LABEL`,
   `GPU_LABEL`).
4. Run `python3 build/pc-render.py --preview previews/` (needs Python 3.10+, `pip install numpy pillow
   opencv-python-headless`) and check the recolored previews.
5. Commit the render, the regenerated layers and `PcIllustration.xaml`.
