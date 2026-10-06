# Pulse

Native Windows control for an Arduino piezo drum kit: automatic USB connection, a playable top-down kit, pad learning, stereo samples and MIDI.

![Top-down kit with a snare hit](artifacts/pulse-kit-hit.png)

## Install

Download **Pulse-Windows-x64.zip** from [Releases](https://github.com/Maxy747/pulse-drums/releases). Extract it and run `install.ps1` in PowerShell. This creates a Start menu shortcut, enables launch at Windows sign-in, and downloads the GSCW sample library if it is missing. No administrator access is needed. Use `-NoStartup` to skip sign-in launch or `-WithoutSamples` to install only the synth/MIDI app. `Pulse.exe` can also run directly; run `download-samples.ps1` once for the sample library.

Requires Windows 10/11 x64, .NET Framework 4.8, a Windows audio output and your board's USB driver. The app has no Python/browser dependency. Samples take roughly 301 MB and remain in `%LOCALAPPDATA%\PulseDrums\Samples\GSCW` across app updates. Internet access is needed for the initial download, not for playing.

## Play

### Pedal Nano (Pulse 2.3)

**Kick from main drums only** accepts kick hits from the main Arduino and ignores pedal kick hits while retaining the hi-hat pedal. It and **Kick from pedal only** automatically turn each other off. With both off, either kick source works. Both settings save on this PC and in named presets.

**Swap kick / hi-hat inputs** switches between A0 kick / A1 hi-hat and A1 kick / A0 hi-hat. The meters update and calibration follows each input. The switch is remembered on this PC; no Arduino firmware change is needed.

A second Nano can read **A0 kick / A1 hi-hat potentiometers**, independently of the main piezo board. Upload the included [PulsePedals sketch](firmware/PulsePedals/PulsePedals.ino) once, then use the **Pedal Nano** panel to enable auto-detection and capture each pedal's released/pressed positions. [Full setup and wiring](firmware/PulsePedals/README.md).

Pedal position selects V05 open/closed hi-hat sounds and closes ringing open hits with a short fade. An optional fast-close hit has an adjustable velocity minimum. **Kick from pedal only** ignores the main Arduino's kick while keeping other pads active. Auto-detection requires the pedal sketch's identity and data; the two connections share port reservations. The USB launch helper also recognizes a saved pedal Nano. Pedal sound options save in presets; calibration and device selection stay local. This release's firmware compiles for the Nano, but requires upload and physical calibration before live use.

### Main kit

Close Arduino Serial Monitor and the old drum bridge to free the port. Open Pulse, plug in the Nano, and hit a pad once to verify the serial protocol. Saved thresholds are sent automatically. No Connect, Apply or Save-settings button is needed.

The default view follows the numbered physical kit. Pieces glow on a hit, independently and simultaneously. The three colour dots at the top right switch between **Green**, **Red**, and **Blue**; the selected dot has an outline. The USB/COM indicator is centred. Theme and view choices are remembered. Click a piece to tune it; double-click, press **1–8**, or use **Audition** to test its sound. **Classic controls** switches to the previous pad-card view.

| Number | Instrument | Initial input | Default MIDI note |
| --- | --- | --- | ---: |
| 1 | Hi-hat | A2 | 42 |
| 2 | Crash | A6 | 49 |
| 3 | Low tom | A4 | 45 |
| 4 | Snare | A1 | 38 |
| 5 | Mid tom | A3 | 48 |
| 6 | Bass / Kick | A0 | 36 |
| 7 | Floor tom | A5 | 41 |
| 8 | Ride | A7 | 51 |

The numbers describe physical positions, **not wiring**. If inputs are swapped, learn them:

1. Choose **Set up my kit** for all eight pieces, or select a piece and choose **Assign input**.
2. Strike only the requested instrument **twice**. Pulse picks the largest raw peak in each short capture window and uses a brief ringing guard to keep one strike from counting twice.
3. Two matching strikes confirm the input and move to the next piece automatically. If the strikes disagree or an input was already assigned, it keeps listening. There are no Confirm or Next buttons.
4. Full setup saves after all eight pieces have been confirmed twice. Cancel discards the draft. One-piece assignment works the same way and swaps the displaced input so two pieces cannot accidentally share one sensor.

**Undo last part** restores the map from before the last confirmed assignment and listens for that piece again. It can be used repeatedly, including on the completion panel. Before any part is confirmed, it clears the first strike. Undoing a completed setup reopens the draft; cancelling that draft restores the map from before that setup session.

Sounds are paused during setup. Learning uses real serial sensor peaks, not mouse/keyboard auditions. The same assignment controls the kit glow, sample and MIDI note. Trigger calibration remains attached to each physical Arduino input. A sensor must exceed its current firmware threshold to be detected; adjust the threshold if a pad never registers.

## Sounds and Ableton

The first sample-enabled run loads **GSCW Kit 2** with **V05 for every piece** and selects direct playback. Both GSCW kit presets use V05 by default. Pick **GSCW Kit 1**, **GSCW Kit 2**, or **Pulse synth** in the bottom selector and choose **Apply sounds** to change the whole kit. Individual sample choices and named presets remain editable and are remembered.

The selected drum's **Sound** picker lists matching instruments: crash for Crash, snare for Snare, and tom recordings from **both kits** for all three tom pads. Tom labels show the actual drum size, velocity layer and **Single hit** or **Flam (double hit)** articulation. V05 single hits appear before flams. **Restore V05 tom set** restores three distinct Kit 2 recordings: Low 12-inch, Mid 10-inch and Floor 13-inch, without changing other pads or input assignments. Kit 1 alternatives retain their actual 10/13-inch labels. Splash samples are excluded from Crash. **Load WAV…** accepts your own matching sound; known filenames from another instrument are rejected. **Sample folder…** points Pulse at a different downloaded library. Sample loading happens off the hit-processing thread; failed loads retain the previous sound and show a message.

Output choices:

- **Samples** plays directly through the default Windows audio output.
- **Ableton / MIDI only** sends notes to the selected MIDI output without direct audio.
- **Samples + MIDI** enables both.

For Ableton, choose your existing loopMIDI output, then enable that input in Ableton and arm the instrument track. On this PC Windows calls the port **Nano Drums**, whereas the old Python UI displayed **Nano Drums 1**. Pulse remembers the output and retries when it becomes available. It does not install a virtual MIDI driver.

The audio-device selector offers **Windows default output** and installed **ASIO** drivers. For Scarlett, choose **Focusrite USB ASIO** to send stereo audio directly to outputs 1/2 through the Focusrite driver. **ASIO settings** opens its buffer/sample-rate panel; **Restart audio** retries a failed connection. Selection persists. MIDI-only mode releases the audio device for the DAW. Pulse uses 48 kHz; driver resets trigger reinitialization. A missing/busy ASIO device shows an error instead of silently routing to other speakers.

**Reverb** in the Output panel adds stereo room ambience to samples and synth sounds on either audio driver. Toggle it on, then set the amount with the 0–100% slider. It starts off with 25% ready when enabled. Amount changes fade smoothly; turning it off clears the effect after a brief fade. Silence all clears its tail immediately. Both controls are saved on this PC and in named presets. MIDI notes sent to Ableton remain unchanged; use Ableton's own reverb for that instrument.

**Master gain** adjusts the mixed drums and reverb from −60 to +60 dB with a rotary knob, fine Shift adjustment and a continuous slider before overload protection. It works with Windows and ASIO, changes smoothly, and is remembered across launches and saved presets. 0 dB preserves the original level; +6 dB approximately doubles signal amplitude below the limiter. The master volume still controls the final level. This does not change MIDI velocity or Ableton's instrument gain.

**Player stereo** places every drum across the stereo field according to its horizontal position in the kit drawing, viewed from the player's seat. It starts enabled at 100% width. Hi-hat and crash are far left; snare and low tom are left of centre; kick is centred; mid tom, floor tom and ride move progressively right. The width slider narrows the placement; zero width or the toggle off preserves the original WAV channels. Changes are smoothed, and the settings persist in saved presets. Position follows the logical instrument when sensor inputs are remapped. Samples and synth voices are positioned before the shared stereo reverb on Windows and ASIO. This is stereo panning, not HRTF/head-tracked 3D audio. MIDI-only instruments need panning in Ableton.

The status reports the driver buffer and output latency, not end-to-end drum latency. Low buffers can cause crackles; raise the buffer in [Focusrite Device Settings](https://support.focusrite.com/hc/en-gb/articles/208814065-How-to-change-sample-rate-buffer-size-in-Windows) if needed. On the development PC, the Scarlett successfully opened at 32 samples with 3.2 ms reported output latency. Audible performance still needs listening confirmation.

WAV playback supports mono/stereo PCM 8/16/24/32-bit and float32, with sample-rate conversion in memory to 48 kHz stereo. Original files are not rewritten. One-shots are limited to 60 seconds/128 MB. GSCW Kit 1 has no separate 12-inch tom, so its Low tom preset uses the matching 12-inch sample from Kit 2; Kit 2 has distinct 10/12/13-inch choices. See [sample source and license notes](THIRD-PARTY.md).

## Your defaults and saved presets

The supplied calibration is now the factory default:

| Input | Trigger | Re-arm |
| --- | ---: | ---: |
| A0 | 140 | 40 |
| A1 | 20 | 5 |
| A2 | 20 | 10 |
| A3 | 20 | 5 |
| A4 | 10 | 1 |
| A5 | 60 | 1 |
| A6 | 10 | 5 |
| A7 | 10 | 9 |

Velocity floor **50**, ceiling **127**, gain **1**, curve **0.6**, MIDI channel **1**, transpose **0**, and note length **10 ms**. The extra PC retrigger guard defaults to **0 ms**. Open **More MIDI controls** for velocity ceiling, transpose and note length.

**Save preset** creates a named `.pulse.xml` file. **Load preset** restores input assignments, calibration, sound selections, notes, volume, output mode, channel, transpose and note length. Device identity, the local sample folder and this PC's MIDI-port selection are retained. Missing sample paths are matched by unique filenames in the local library when possible. Otherwise the UI reports the missing sound.

**Your defaults** restores the supplied calibration and default musical MIDI values, keeping input assignments and samples. **Reset** restores only the selected input's calibration and that instrument's default MIDI note. Normal edits auto-save to `%LOCALAPPDATA%\PulseDrums\settings.xml`, with a `.bak` copy of the previous save. Existing saved settings are kept during upgrades.

Trigger values are constrained to 2–1022 and re-arm values to 1–(trigger−1). Response curves below 1 make softer hits louder.

### Sensitivity and unwanted triggers

The three sensitivity buttons change trigger/re-arm thresholds, ringing guards and crosstalk protection for all inputs while keeping mappings, sounds and MIDI notes:

| Preset | Threshold multiplier over your original calibration | Quiet-time guard | Crosstalk rejection |
| --- | ---: | ---: | ---: |
| High | 0.75× | 35 ms | 25% |
| Medium | 1.5× | 70 ms | 45% |
| Low | 2.5× | 110 ms | 60% |

The quiet-time guard ignores continuous sensor ringing until that input has been quiet for the selected interval. Crosstalk protection compares raw peaks over 6 ms, rejects weaker neighbouring triggers, and guards against vibration tails for 25 ms. At 45%, a neighbouring peak below 45% of a stronger hit is rejected. Only accepted hits reach session counting, samples and MIDI. Set crosstalk to zero to remove its comparison delay. High protection can suppress soft simultaneous strokes or fast rolls; use High sensitivity or lower the individual guard when needed.

On the first 2.1 launch, existing thresholds and mappings are retained, guards are raised to at least 70 ms, and crosstalk starts at 45%. **Your defaults** still restores the original screenshot calibration, including zero extra guard and no crosstalk filter. These settings are editable and saved in named presets. The filters operate on the PC; firmware is unchanged.

## Background operation

**Launch when drums connect** enables a separate background watcher that starts at Windows sign-in. It waits for USB arrival/removal notifications, briefly retries while Windows enumerates the port, and has a 30-second fallback scan. It opens no audio device or serial port. The saved Arduino identity is used when available; before first verification, only compatible USB-serial candidates qualify. Connecting the kit launches Pulse if it is closed. Exiting Pulse while the kit remains connected does not immediately relaunch it; unplug/replug to launch again. The toggle removes its sign-in entry and stops the watcher. The installer option `-EnableUsbLaunch` enables it explicitly. `-NoLaunch` leaves both processes stopped during installation.

This is separate from **Launch with Windows**, which starts the full drum app at sign-in. To run only the lightweight watcher until the kit connects, leave USB launch on and switch Launch with Windows off. USB enumeration and the Nano's boot still take time; the watcher cannot make an unready device connect instantly. Windows must be signed in.

When **Keep playing in tray** is enabled, closing the window keeps the kit active. Use the tray icon's **Exit** to stop it. **Launch with Windows** starts Pulse in the tray. Opening it again brings the existing instance forward. **Silence all** stops active sample voices and MIDI notes.

Diagnostics include USB state, sample load errors and MIDI availability. The bounded device log is saved in `%LOCALAPPDATA%\PulseDrums\device.log`.

## Arduino compatibility and limits

Based on the serial interface in [Durms.ino](https://github.com/marwans200/Arduino-Drums/blob/main/Durms.ino): **115200 baud, 8-N-1**, with `NOTE,note,velocity` followed by `RAW,pad,peak` within 250 ms. Raw input indexes are 0–7. Changed incoming note numbers are supported because identity comes from RAW. Assigned instrument notes are used for MIDI output.

Pulse sends `SET,RESET,pad,value` and `SET,HIT,pad,value`, paced 25 ms apart. The firmware is not flashed. It has no identification handshake, settings acknowledgment, heartbeat or EEPROM persistence. Pulse opens likely Arduino USB adapters and verifies the NOTE/RAW pair before restoring settings. Opening a Nano's port may reset it; allow about two seconds for boot and strike one pad to verify it. An idle kit cannot be distinguished from stalled firmware while its port stays open.

Arduino/CH340/FTDI/CP210x identifiers are candidate hints, not unique drum-kit IDs. With multiple candidates, Pulse cycles every eight seconds until one is verified. A port can also be selected once and remembered.

The reference sketch's blocking 10 ms peak scan remains unchanged. Windows output queues six 256-frame buffers at 48 kHz (32 ms), uses the multimedia scheduler, and reports empty-queue events. ASIO uses the driver's callback buffer instead of that Windows queue. End-to-end latency includes firmware, serial, any crosstalk window and the output device; it has not been measured. Use Restart audio after a device change or connection failure.

## Build and test

```powershell
.\build.ps1 -Test
.\bin\Pulse.exe --smoke
```

Uses the .NET Framework 4.8 compiler shipped with Windows. Pinned NAudio ASIO/Core libraries and a registry compatibility assembly are vendored and embedded, so the executable remains standalone with no package restore. Licenses and provenance are in `vendor/NAudio` and THIRD-PARTY.md. The local tests cover trigger bursts, crosstalk, mapping, learning, presets, WAV decoding and audio mixing. When the sample library is present, all 360 WAVs are decoded. CI uses `-NoAudio`. Optional `Pulse.Tests.exe --asio-probe --audio-stress` opens Focusrite ASIO silently and stress-tests the Windows queue at low volume.

The WPF smoke test renders kit/classic/hit/setup screenshots and exercises assignment, cancel, reset, preset restore and control changes without opening a serial port or writing user settings. Actual pad identification and audible DAW reception require hands-on testing.

Installed app: `%LOCALAPPDATA%\Programs\Pulse`. To remove it, exit the tray app, disable sign-in launch, and remove that folder and its Start menu shortcut. Settings, presets and samples can be retained or removed separately from `%LOCALAPPDATA%\PulseDrums`.


## iPad / browser control (2.4)

Pulse shows its LAN HTTPS address at the top. The desktop Browser preview button opens the same kit locally at `http://127.0.0.1:8765/`; that loopback address cannot reach the PC from an iPad.

1. Keep Pulse running and connect the iPad to the same home network as the PC.
2. Click **Allow iPad access** and approve the Windows administrator prompt. The firewall exception covers only Pulse TCP ports 8766–8785 from LocalSubnet; it does not expose the service to the internet.
3. Click **iPad setup** on the PC to see the setup URL (normally `http://<PC-IP>:8776/`). Open that address in Safari on the iPad.
4. Download the PC-specific Pulse certificate, install it in Settings → General → VPN & Device Management, then enable **Pulse Local Preview CA** under General → About → Certificate Trust Settings. Compare the certificate fingerprint with the PC setup page.
5. Open the HTTPS link on that page (normally `https://<PC-IP>:8766/`). Optional: Safari → Share → Add to Home Screen.

The private keys stay in the Windows current-user certificate store. Only the public CA certificate is downloadable. Setup HTTP exposes instructions and the certificate only; live LAN controls use TLS 1.2. Remove the installed profile to revoke trust on the iPad. Restart Pulse if your PC's network address changes. Ports automatically advance when occupied.

**Touch play** enables multi-touch drum auditions through the PC's selected audio output. **Edit** lets you tap a part to choose its sample and edit its tuning. **Settings** exposes audio/ASIO, MIDI, pedals and calibration, sensitivity, setup/undo, startup, sounds, and named presets. Native ASIO panels and file/folder pickers open on the PC. Master gain is available directly beside the toolbar and in settings. Settings save on the PC. Network touch adds Wi-Fi latency and does not stream audio to Safari.

The local network is the access boundary: devices that can reach the HTTPS page can use its controls. Commands require an exact matching Origin and a per-run request token; the server does not allow cross-origin access. Use on a trusted home network. Live hit publication never performs network I/O on the audio thread. Safari automatically reconnects after returning from the background. No cloud service or account is required.


### Hi-hat MIDI mapping (2.4.1)

Select the hi-hat in Kit view or browser Edit. **MIDI note** is the closed hi-hat note and also the optional pedal-close hit note. **Open hi-hat MIDI note** sets the open articulation (default 46). Transpose applies to both, and closing the pedal stops the configured open note. Without a connected pedal, pad hits retain the normal hi-hat note.

**Hi-hat pedal MIDI controller (CC)** in the Pedal Nano panel sets the position controller (default CC4). It follows the selected MIDI output and kit channel. These settings save automatically and are included in named presets. Existing presets gain the standard open-note/CC defaults without changing their existing closed hi-hat mapping.


### Per-drum sample gain and threshold learning (2.5)

**Sample gain** in pad settings adjusts that instrument's direct audio from −60 to +36 dB before reverb and master gain. It affects sample/synth playback and both hi-hat articulations, without changing MIDI velocity or notes. It follows the instrument when inputs are reassigned, survives output restarts, and saves in presets. Existing kits start at 0 dB per drum.

Use **Learn thresholds · all** or **Learn selected pad** with the main Arduino connected and verified. Keep the kit still for the six-second quiet phase, then play six separate soft-to-firm hits on the named drum about one second apart. Each piece advances automatically. Review the suggested hit/reset thresholds and click **Apply learned thresholds**; Cancel preserves the prior values. The browser Settings menu includes the same actions and live instructions.

Calibration temporarily requests hit=2/reset=1 on the main Nano and pauses performance triggering. It uses reported RAW peak events, groups vibration bursts as one strike, estimates idle noise and cross-pad peaks, and flags overlapping noise/hit ranges for review. It does not claim continuous ADC measurements or train an ML model. The existing sketch has no settings acknowledgement; unsupported firmware may ignore the temporary sensitivity. Missing/weak input waits indefinitely instead of inventing a result. Completion/cancel restores current device settings until suggestions are applied. Disconnect cancels and restores saved settings on reconnect; a normal app exit waits for restoration commands. A process crash/power loss during calibration requires reconnecting Pulse to restore settings. Calibration changes only hit/reset thresholds, not pad mapping, MIDI, velocity curves or sample gain.

## Android companion (Pulse Mobile 1.1 / Windows 2.7)

[Install and use Pulse Mobile](android/README.md) to connect both Nanos through a phone's USB OTG hub. Play standalone on Android or relay hits/pedals over local Wi-Fi to this PC's Pulse samples, ASIO output or MIDI routing. Switch on the Windows **Phone input**, then tap **Find Pulse PC automatically** on the phone within two minutes; it finds the PC and pairs itself. The address and code are also shown for manual entry. A signed APK is included with each release.
