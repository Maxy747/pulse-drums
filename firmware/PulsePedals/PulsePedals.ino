// Pulse pedal Nano v1. A0 = kick pot wiper; A1 = hi-hat pot wiper.
// Each potentiometer's outer terminals connect to 5V and GND.
// Calibrate released/pressed positions in Pulse; either rotation direction works.
// Upload to the PEDAL Nano only (currently COM4), not the piezo drum Nano.
const uint8_t KICK_PIN = A0;
const uint8_t HAT_PIN = A1;
uint32_t lastSample = 0, lastIdentity = 0;
int kick = 0, hat = 0;

int readPot(uint8_t pin) {
  analogRead(pin); // Discard first conversion after switching ADC channels.
  long total = 0;
  for (uint8_t i = 0; i < 4; ++i) total += analogRead(pin);
  return total / 4;
}
void setup() {
  Serial.begin(115200);
  kick = readPot(KICK_PIN); hat = readPot(HAT_PIN);
  Serial.println(F("PULSE_PEDALS,1"));
}
void loop() {
  const uint32_t now = millis();
  if ((uint32_t)(now - lastIdentity) >= 500) {
    lastIdentity = now; Serial.println(F("PULSE_PEDALS,1"));
  }
  if ((uint32_t)(now - lastSample) >= 5) {
    lastSample = now;
    kick = (kick + readPot(KICK_PIN)) / 2;
    hat = (hat + readPot(HAT_PIN)) / 2;
    Serial.print(F("PEDALS,")); Serial.print(now);
    Serial.print(','); Serial.print(kick);
    Serial.print(','); Serial.println(hat);
  }
}
