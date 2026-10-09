# Presentation: From Audio to Beatmap

A Beamer deck on the backend pipeline, with an emphasis on the signal processing.
It walks from audio loading to the exported `.osz`.

- `main.tex`: the slides (Beamer + TikZ + pgfplots). Overlays step through each explanation.
- `make_data.py`: runs the generator's own functions on one song and writes everything
  the slides plot into `data/`. That covers CSVs, the spectrogram image, the playfield
  snippet, the `.osu` excerpt and `stats.tex`, which holds every number the slides quote.
- `data/`: generated. Don't edit it by hand; rerun the script instead.
- `backend_short.tex`: the 3-minute cut. It has 10 static pages, one idea each, styled after
  the app's retro theme (RetroPalette colours, Audiowide + Press Start 2P from `fonts/`).
  It reads the same `data/`. It needs XeLaTeX or Tectonic (fontspec), not pdflatex.

## Build

```bash
# 1. regenerate the data (from the repo root, using the project venv)
.venv/Scripts/python.exe presentation/make_data.py data/raw/bad_apple.mp3 --window 40 46

# 2. compile (any of these)
tectonic main.tex          # self-contained, fetches packages on demand
xelatex main.tex           # TeX Live / MiKTeX
pdflatex main.tex
```

`--window` picks the 6-second excerpt the zoomed plots show. `--tier` picks the
difficulty used for the per-tier examples (default `Hard`). With a different song,
every figure and every quoted number follows automatically.
