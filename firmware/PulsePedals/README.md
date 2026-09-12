# Pulse pedal Nano

Upload `PulsePedals.ino` to the **pedal Nano on COM4**, using Arduino IDE. A0 is kick and A1 is hi-hat. Keep the piezo Nano's existing sketch.

1. Turn **Enable pedal Nano** off in Pulse to release the serial port before uploading. Close Serial Monitor too.
2. Open `PulsePedals.ino`. Choose **Arduino Nano**, your Nano's processor/bootloader, and **COM4**. Upload. If the normal ATmega328P setting cannot upload to an older clone, try **ATmega328P (Old Bootloader)**. [Arduino's processor-selection guide](https://support.arduino.cc/hc/en-us/articles/4401874304274-Select-the-right-processor-for-Arduino-Nano).
3. In Pulse, turn **Enable pedal Nano** on. Leave its selector at **Auto-detect pedal Nano**, or select COM4 explicitly. Its status must say **pedals ready**.
4. Hold the kick pedal released and press **Capture kick released**; hold it fully pressed and press **Capture kick pressed**. Repeat for the hi-hat. Each pot needs at least 20 ADC counts of travel. Values and device identity are saved on this PC.

Each pot's middle terminal (wiper) connects to its analog input; its outside terminals connect to Nano 5V and GND. Capture calibration handles either rotation direction. The sketch averages ADC readings and streams both inputs every 5 ms at 115200 baud; it does not require any extra library.

## Playing

- **Kick from main drums only** disables pedal kick sounds/counts while allowing the main kick and hi-hat pedal. Enabling either kick-only toggle disables the other; both off allows both sources. The choice saves in named presets.

- **Swap kick / hi-hat inputs** changes to A1 kick / A0 hi-hat. Turn it off for A0 kick / A1 hi-hat. Calibration follows the physical inputs, and the choice stays local with calibration across preset loads. Keep the same Arduino sketch; no re-upload is needed.

- Strike the main hi-hat pad: pedal pressed selects a closed V05 hi-hat, released selects open V05. The pair follows the kit of the selected hi-hat WAV. Without the sample library, distinct synthesized open/closed hats are used.
- Closing the hi-hat fades the open voice over 5 ms, even with the close-hit toggle off.
- **Play a closed hi-hat on a fast pedal close** optionally adds a closed hit. **Minimum close velocity** filters slow closures. Velocity is estimated from pot movement speed, not force.
- A kick pedal stroke produces one kick hit, then must return below 25% travel to rearm. It triggers at 75% travel. Hi-hat closes at 75% and reopens below 60%, preventing threshold chatter.
- **Kick from pedal only** suppresses the main Arduino's assigned kick before counting/playback. Other main pads keep working. It stays pedal-only when disconnected; turn it off to use the main kick again. Audition remains available.
- MIDI sends hi-hat note 42 closed, 46 open and CC4 (0 released to 127 pressed), using the chosen MIDI channel and note transpose. Closing sends note-off for the open hat. A DAW instrument may need its own hi-hat choke mapping. Kick uses the configured kick MIDI note.
- With the pedal device disabled or unavailable, main hi-hat strikes revert to the normal selected hi-hat sound. Input maps and piezo thresholds are unchanged.
- Named presets include the close-hit toggle, minimum close velocity and kick-only mode. Device selection, enabled state and physical calibration remain local to this PC.

## Serial protocol v1

The sketch sends `PULSE_PEDALS,1` at startup and every 500 ms. Each data line is `PEDALS,millis,kickADC,hatADC`, with ADC values 0–1023. Pulse requires both the identity and valid data before accepting the device. The main piezo protocol stays unchanged. Opening a classic Nano serial port can reset it, so discovery allows its bootloader to finish; reconnection is automatic but not instantaneous.

Firmware validation: compiled with Arduino AVR Boards 1.8.7 for `arduino:avr:nano:cpu=atmega328old`; 2,390 bytes flash and 200 bytes static RAM. The firmware has not been uploaded automatically.
