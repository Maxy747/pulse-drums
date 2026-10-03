# Pulse Mobile — Android 1.0

Connect the drum Nano and pedal Nano to an Android phone through a USB OTG hub. Play through the phone, send serial events over local Wi-Fi to Pulse on Windows, or use both outputs.

## Install and play

1. Copy **Pulse-Mobile-Android.apk** to the phone and open it. Android may ask you to allow installation from the browser/file manager you used. Requires **Android 8.0+ and USB host/OTG support**. This APK is not an iPhone app.
2. Open Pulse Mobile, connect the hub and both Nanos, and accept Android's USB permission prompt for each device. Use a powered hub if the phone cannot supply enough power. Some phones require enabling OTG in system settings.
3. Select **Phone output**. Tap a drum to audition; connect wired headphones/USB audio or use the phone speaker. The app follows Android's current media-output route. Bluetooth adds delay.
4. The app includes generated drum sounds. **Download V05 acoustic kit** fetches nine original GSCW samples directly from the user's sample repository after displaying its license. No GSCW audio is redistributed in the APK. Samples remain available offline. **Import WAV** replaces the selected drum; the open hi-hat has a separate import button.
5. Use **Assign all** or **Assign pad**, then strike the requested physical pad twice, at least 350 ms apart. The next part appears automatically. Undo restores the last assignment and listens for it again. Swapped inputs stay one-to-one.
6. For pedals, release both and tap **Set resting**, then fully press both and tap **Set pressed**. This supports inverted potentiometers. Swap inputs if necessary. Choose pedal-only, drums-only or both for kick, and optionally play a hi-hat note on a fast close. Electrical cross-coupling still requires fixing the wiring.

**Keep the app open while playing.** It keeps the screen awake; this version does not promise background or lock-screen operation. Unplugging a Nano is handled independently, and permitted devices are rescanned every two seconds. Android may ask permission again after a disconnect.

## Play through Pulse on the PC

1. Install/run **Pulse Windows 2.6.0+** on the same local network.
2. Enable **Phone input** near the top. It shows the PC IPv4 address and an eight-digit pairing code. If needed, click **Allow phone Wi-Fi** and accept the Windows administrator prompt for the local-subnet UDP 9876 firewall rule.
3. On the phone choose **Pulse PC over Wi-Fi** or **Phone + Pulse PC**. Enter the address (no `https://` and no port), enter the code, and press **Save Wi-Fi connection**. The status must say **Pulse PC connected**.
4. Pulse's existing audio/ASIO device, samples, MIDI output, note mappings and trigger protection are used on the PC. Enable pedals in the PC app if using them. The PC receives raw physical input numbers and pedal readings, so its mapping and pedal calibration are independent of the phone's standalone settings. Use the PC's kit setup if its assignments differ.

The receiver and pairing code persist locally. Disabling Phone input returns to direct PC USB input. Local USB performance input is ignored while Phone input is enabled, preventing duplicate routing. The website/browser preview continues to show received hits.

This transmits drum/pedal events, **not streamed audio or standard RTP-MIDI**. No cloud connection is needed for playing. UDP avoids retransmission delays; lost or out-of-order packets are discarded instead of replayed. Weak/congested Wi-Fi can still lose hits or add jitter. Use the same router, preferably 5 GHz, and avoid guest Wi-Fi client isolation. The pairing code authorizes one active phone session; packets are not encrypted. Do not expose port 9876 to the Internet.

## Controls and audio

- Kit layout follows the player's perspective; green, red and blue themes; multi-touch audition and hit flashes.
- Independent gain per part, phone master level, stereo positioning and a linked output peak limiter.
- 32 overlapping voices; predecoded/resampled 48 kHz stereo audio through Android AudioTrack with the low-latency performance request. Actual latency depends on phone, audio device and Android; it has not been measured on the user's phone. This is not ASIO on Android.
- PCM16/24/32 or float32 WAV import, mono/stereo, 8–192 kHz, up to 16 MB and 12 seconds. Longer files are rejected, never silently truncated.
- Separate V05 10-inch mid tom, 12-inch low tom and 13-inch floor tom.
- Repeat guards: high 110 ms, medium 70 ms, low 35 ms; weak cross-pad hits within 14 ms of a stronger hit are suppressed for phone playback.
- Original physical-input trigger defaults: hits `140,20,20,20,10,60,10,10`; resets `40,5,10,5,1,1,5,9`. Per-pad **Trigger thresholds** edits them and sends paced SET commands only to a device that has emitted drum RAW frames. Firmware without SET support can still send hits but cannot apply these commands.
- Thresholds are owned by the phone while its USB connection is used. PC threshold sliders cannot write to the phone's USB devices; PC automatic threshold learning explicitly requires direct PC USB. PC audio/MIDI processing controls still work.
- Standalone settings are saved automatically; this first mobile version does not include all desktop effects, named presets or automatic threshold learning.

## Firmware/protocol

Existing Pulse-compatible firmware is used unchanged. Serial is 115200, 8N1. Each Nano has its own reader/decoder so identical CH340 adapters are kept separate.

Drum Nano: `NOTE,note,velocity` then `RAW,input,peak` within 250 ms. The raw input index is A0–A7; peak is 0–1023. RAW telemetry alone never counts as a hit. Pedal Nano: `PULSE_PEDALS,1` and `PEDALS,unsignedMillis,A0,A1`. See [pedal firmware](../firmware/PulsePedals/README.md).

UDP envelope: `PULSE_M1|pairingCode|UUID|sequence|payload`. Payloads: `PING`, `HIT,input,velocity,peak`, `RAW,input,peak`, `PEDALS,time,kick,hat`, `TOUCH,logicalPart,velocity`. Heartbeat acknowledgement: `PULSE_ACK|UUID`. Bounded datagrams/queues, monotonic sequences and session ownership prevent duplicate playback. Phone drops queued events older than 100 ms.

## Build

JDK 17+ (tested with Temurin 21), Android SDK platform 35/build-tools 35.0.0, and Gradle 8.13 (wrapper included), AGP 8.13.0. USB drivers: usb-serial-for-android 3.11.0 via JitPack, MIT licensed.

```powershell
$env:JAVA_HOME = 'C:\path\to\jdk'
$env:ANDROID_HOME = 'C:\path\to\android-sdk'
.\android\build-android.ps1 -Release
```

The wrapper downloads Gradle as needed. The script builds the APK, runs JVM tests and Android lint, and copies it to `bin/Pulse-Mobile-Android.apk`. Without `-Release`, it uses Android's debug signing key. Release signing is local: `%LOCALAPPDATA%\PulseDrums\AndroidSigning\pulse-mobile.jks`, with a Windows-user-encrypted password in `password.dpapi`. **Keep a secure backup of the signing key and its password** for future updates; neither belongs in Git. The distributed APK is signed, not Play Store published.

The Windows companion builds using `./build.ps1 -Test -NoAudio`. Tests cover serial chunking/pairing, invalid payloads, mapping swaps, inverted calibration, PCM/float decoding/resampling, actual installed V05 files, UDP authentication/duplicates and a real loopback handshake. Android 15 emulator checks cover launch, layout, touch-to-PC relay, acoustic sample downloading and audio initialization. Physical OTG, two-Nano permission/reconnect behavior and real output latency still require a phone/hub test.
