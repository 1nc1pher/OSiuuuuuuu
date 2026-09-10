# osu-dsp-project

Two decoupled halves:

1. **Backend (done):** a DSP pipeline that turns a raw audio file into
   a playable osu!(lazer)-importable `.osz` beatmap set — audio in,
   five difficulties out. See **[BACKEND.md](BACKEND.md)** for the full
   pipeline, usage, tuning knobs, and testing strategy.
2. **Frontend (planned):** a custom game client, built on the same
   `osu.Framework` that osu!(lazer) itself uses, to play those `.osz`
   files with lazer-grade animation/feel and room for personalization.
   See **[FRONTEND_PLAN.md](FRONTEND_PLAN.md)** for the framework
   decision, architecture, file structure, and phased roadmap.

The two are only connected by a file format: the backend writes
`.osz`, the frontend reads `.osz`. Neither depends on the other's
language or runtime.

## Quick start (backend)

```bash
python src/main.py data/raw/your_song.mp3 --artist "Artist" --title "Song"
```

Produces `data/output/<Artist> - <Song>.osz` — drag onto osu!(lazer) to
play, or (once built) onto the custom frontend.

## Project structure

```
osu-dsp-project/
├── README.md           # you are here — project overview
├── BACKEND.md           # backend pipeline docs (audio -> .osz)
├── FRONTEND_PLAN.md     # frontend architecture + roadmap (planned)
├── src/                 # backend: audio/onset/mapping/export pipeline
├── data/                # raw/ input audio, output/ generated beatmaps
├── tests/                # backend unit tests
├── configs/              # difficulty presets
└── frontend/             # (planned) osu.Framework client — see FRONTEND_PLAN.md
```

## Status

- Backend: **all six pipeline steps done end-to-end**, `pytest`-covered.
  Details in [BACKEND.md](BACKEND.md).
- Frontend: **planning stage**, not yet scaffolded. Plan in
  [FRONTEND_PLAN.md](FRONTEND_PLAN.md).
