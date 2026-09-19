Pulse 2.5.0 — per-drum audio gain and guided threshold learning.

- Added −60 to +36 dB sample gain for each logical instrument, independent of MIDI velocity, saved in settings/presets and exposed in browser Edit.
- Added selected-pad/all-pad calibration: quiet phase, six distinct strikes per part, automatic progression, suggested hit/reset thresholds, review/apply/cancel and live browser instructions.
- Calibration requests temporary low thresholds without saving them, suppresses performance triggers, and restores current settings after completion/cancel/disconnect. Normal exit waits for restoration writes.
- Uses reported peak events rather than continuous ADC data or ML. Firmware does not acknowledge threshold writes; real-kit calibration remains user-driven.
- Validated audio scaling/isolation, persistence, distinct-strike grouping, all-pad progression, threshold invariants and WPF/browser controls.

Pulse 2.4.1 — configurable hi-hat MIDI articulation.

- Open hi-hat has its own MIDI note selector in desktop and browser pad settings.
- Closed pad hits and optional pedal-close hits follow the hi-hat pad's configured MIDI note instead of a fixed note 42. Open hits and pedal choke follow the configured open note, including transpose.
- Pedal position uses a configurable CC (default 4) on the selected MIDI output and kit channel.
- Settings persist in app settings and presets. Existing files retain standard open note 46 / CC4 defaults.
- Validated 580 assertions and desktop/browser control smoke checks. No external DAW capture was performed.

Pulse 2.4.0 — iPad live kit, touch control and master gain.

- Added a local live kit website with hit feedback, themes, focus, labels and tablet layouts.
- Added LAN HTTPS, a top-of-app URL, one-time iPad certificate setup and a scoped Windows firewall helper.
- Touch play auditions drums through the PC output; Edit and Settings mirror pad sounds, tuning, routing, pedals, setup/undo and named presets. Native dialogs remain on the PC.
- Master gain now offers a knob and fine adjustment from −60 to +60 dB in desktop and browser, with smoothing and stereo overload protection.
- Validated 574 assertions, all 360 sample files, WPF smoke checks and local TLS certificate verification. Physical iPad Safari requires the user's certificate trust step.

Pulse 2.3.2 — main drums only kick.

- Added Kick from main drums only to suppress pedal kick events before sound, MIDI and session counting, while retaining hi-hat pedal operation.
- The two kick-only toggles are mutually exclusive. Both off permits both sources. Settings persist on this PC and in named presets.
- Validated 564 assertions and UI routing tests covering both exclusive modes, both-source mode and preserved hi-hat pedal state.

Retained from 2.3.1 — swap pedal inputs:

- Added Swap kick / hi-hat inputs: on maps A1 to kick and A0 to hi-hat; off restores A0 kick and A1 hi-hat.
- Calibration follows the physical input. Live labels, meters, hit detection and MIDI pedal position use the selected mapping. Switching resets motion tracking without generating a hit.
- Mapping persists with this PC's calibration across preset loads. The existing Arduino sketch is unchanged.
- Validated 563 assertions and UI tests for independent swapped kick and hi-hat routing.

Retained from 2.3.0 — separate pedal Nano:

- Included Nano firmware streams A0 kick and A1 hi-hat potentiometers at 115200 baud with a distinct identity. Compiled for Nano/ATmega328P using Arduino AVR Boards 1.8.7 (2,390 bytes flash, 200 bytes RAM).
- Separate auto/manual pedal device selection, reconnect, port reservations and saved identity. Main drums remain on their existing protocol; the USB launcher can also recognize the pedal Nano.
- Released/pressed calibration and live meters support either potentiometer direction. Hysteresis prevents repeated held hits; startup and stream gaps suppress phantom strokes.
- Main hi-hat strikes select V05 open/closed recordings from the selected hat kit. Closure fades open voices over 5 ms. Optional fast-close sound with minimum velocity, plus pedal-only kick mode before session counting/audio/MIDI.
- MIDI hat notes 42/46, CC4 pedal position and open-note termination on close. Existing ASIO, reverb and stereo placement apply to pedal sounds.
- Pedal musical options persist in named presets; device and calibration stay local. No firmware is automatically uploaded.
- Validation: 559 local assertions, 360 WAV decodes, WPF pedal controls/routing/calibration/render checks and Nano firmware compilation. Physical pedal travel and feel still need calibration after upload.

Retained from 2.2.2 — tom sound selection:

- Low, Mid and Floor tom selectors now offer both kits' tom recordings, labeled with actual size, velocity layer and single-hit versus flam articulation.
- V05 single hits sort before flams. Kit 1's real 10/13-inch recordings are available as alternatives; no missing 12-inch recording is invented.
- Restore V05 tom set loads distinct Kit 2 single hits: Low 12-inch, Mid 10-inch and Floor 13-inch. Other sounds and learned input assignments are preserved.
- Validated 497 assertions including all 360 downloaded WAVs, both-kit tom selection, instrument filtering and WPF tom-restore behavior.

Retained from 2.2.1 — launch on Arduino connection:

- Launch when drums connect toggle starts a background USB watcher at sign-in, independent of the full app's Windows-startup preference.
- The watcher uses Windows device notifications with short enumeration retries and a 30-second fallback scan; it does not open the audio engine or COM port.
- Saved device identity prevents unrelated USB devices launching Pulse. Closing Pulse while the Arduino stays connected is respected until reconnection.
- Installers stop the watcher before replacing the executable; disabling the toggle stops it and removes its sign-in entry.

Retained from 2.2.0:

- Every drum now has a stereo position derived from its place in the kit layout, including centred kick, left hi-hat/crash/snare/low tom and right mid tom/floor tom/ride.
- Player stereo toggle and 0–100% width slider; enabled at full width by default and saved in presets.
- Per-instrument positioning follows learned mappings, works on Windows and ASIO, and feeds the existing reverb. Off/zero width preserves original WAV channels. MIDI remains unchanged.
- Verified channel direction and panning power for all eight instruments, original-channel bypass, actual mixer routing and WPF controls.

Retained from 2.1.2:

- Overall gain slider in the Output panel adds 0 to +18 dB to drums and reverb on Windows and ASIO output.
- Gain changes are smoothed and overload protection remains active. The setting is saved on this PC and in named presets; MIDI remains unchanged.

Retained from 2.1.1:

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
