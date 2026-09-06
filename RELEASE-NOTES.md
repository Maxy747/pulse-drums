Pulse 1.0.0 — native Windows control for Arduino piezo drums.

- Automatic USB discovery and reconnect; no Connect button.
- Saved pad thresholds restored after valid drum data arrives.
- Eight pad meters, velocity controls, mute, note mapping, and MIDI channel.
- Built-in synthesized percussion and optional routing to an existing MIDI output.
- Tray mode, Windows sign-in launch, and automatic saving.
- Includes the current kit profile transcribed from the supplied settings screenshot.

Download the ZIP and run Pulse.exe. Run install.ps1 for Start menu installation and sign-in launch. Use `install.ps1 -ImportCurrentKit` for the included calibrated profile. The app requires Windows 10/11 x64, .NET Framework 4.8, and the board's USB driver.

Validated locally with 42 automated assertions, rendered WPF control checks, native audio initialization, and actual NOTE/RAW drum data on COM5. Threshold transmission was observed; the reference sketch has no acknowledgment. End-to-end latency, every physical pad, unplug/replug, and DAW reception still need hands-on testing.

Firmware is not changed. The first pad hit identifies the serial protocol after connection. See README for compatibility and multi-device limitations.
