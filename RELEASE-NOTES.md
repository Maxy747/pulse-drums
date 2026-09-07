Pulse 2.1.1 — adjustable stereo reverb.

- Reverb on/off toggle and 0–100% amount slider in the Output panel, remembered across launches and named presets.
- Damped stereo room ambience works on Windows and ASIO output, with smooth changes and immediate tail clearing on Silence all. Reverb starts off; MIDI output is unchanged.

Retained from 2.1.0:

- Direct stereo ASIO output, including Focusrite USB ASIO for Scarlett. Driver selection persists; ASIO settings and Restart audio are available in the output panel. MIDI-only mode releases the audio driver for Ableton.
- Ringing protection filters repeated events before session counting, samples and MIDI. Crosstalk protection compares raw sensor peaks and rejects weaker neighbouring vibrations.
- High, Medium and Low sensitivity buttons set threshold, quiet-time and crosstalk presets. Individual settings remain editable and preset-saveable. Your defaults still restores the original calibration.
- Existing installations gain a 70 ms minimum ringing guard and 45% crosstalk protection once, preserving thresholds, mappings and samples.
- Three independent colour-dot buttons on the right; COM status centred. Kick drum icon, classic view and hands-free setup with Undo are retained.
- Tom selectors explain actual pack coverage: 12-inch Low tom is Kit 2 only; 10-inch Mid and 13-inch Floor tom have both kits. Flam samples are recorded double strikes. No artificial substitute recordings are added.
- Windows playback uses ordered buffers, a manual event, multimedia thread scheduling and a 32 ms queue with dropout diagnostics. Always-on saturation was removed in 2.0.2.

Validation: 462 local assertions, 360 WAV decodes, WPF interaction/render checks, an eight-second polyphony/allocation stress run with no empty Windows audio queues, and actual Focusrite USB ASIO initialization/audio callbacks. The Scarlett reported 32 samples and 3.2 ms output latency; this is not end-to-end latency. Audible cleanliness and physical crosstalk tuning still require playing the kit.

Extract the ZIP and run install.ps1. Existing samples and settings are retained. Samples download directly from the upstream library only if missing. Pulse.exe remains standalone; embedded ASIO dependencies and their licenses are documented in THIRD-PARTY.md.
