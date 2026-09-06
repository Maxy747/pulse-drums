Pulse 2.0.2 — themes, kick drum icon, setup undo and cleaner playback.

- A bass drum icon replaces the old logo in the app, executable and tray.
- Green, Red and Blue themes are available at the top; the kit, hit glows and controls update together and remember your choice.
- Undo last part reverses the last setup assignment, including after completing setup. Repeated undo and re-recording preserve one-to-one input mappings.
- Audio buffers now refill in playback order across ring boundaries, avoiding reordered chunks when Windows coalesces completion events.
- Removed always-on saturation. Samples play linearly with headroom and stereo-linked overload protection.
- Verified with 451 assertions, all 360 WAV decodes and WPF interaction/render checks. Audible improvement on the user's output still needs listening confirmation.

- Both GSCW kit presets now default to V05 for every piece. Individual choices and saved presets remain editable.

- Top-down kit in your numbered arrangement, with independent green hit glows. Classic controls remain switchable.
- Hands-free setup for one input or all eight: strike each requested piece twice to confirm and automatically advance. No Confirm/Next clicks. Cancel preserves previous assignments.
- The learned map drives the diagram, samples and MIDI together, while sensor thresholds stay with their inputs.
- Native stereo WAV playback, two GSCW kit presets, instrument-only sound lists, matching custom WAV loading and a synth fallback.
- All 360 GSCW samples downloaded and decoded locally. Samples are fetched directly from their source by the installer and stored separately from app updates.
- Direct samples, Ableton/MIDI, or both as the output mode.
- The supplied calibration is the factory default. Named presets save assignments, tuning, sounds and musical MIDI settings.
- Configurable velocity ceiling, transpose and note length, with USB reconnect and tray/startup behavior retained.

Extract the ZIP and run install.ps1. It keeps existing settings and downloads the sample library only if missing. Use -WithoutSamples for the synth/MIDI app alone. See THIRD-PARTY.md for sample source/license notes.

Validated locally with automated checks, all 360 WAV decodes and rendered WPF interaction tests. Physical pad learning and audible DAW reception still require hands-on testing.
