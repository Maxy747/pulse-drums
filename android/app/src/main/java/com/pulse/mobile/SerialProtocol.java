package com.pulse.mobile;

/** Bounded streaming decoder and pairing: never turn telemetry into extra hits. */
final class SerialProtocol {
  interface Sink {
    void hit(int input, int velocity, int peak);

    void raw(int input, int peak);

    void pedals(long time, int kick, int hat);
  }

  private final Sink sink;
  private final StringBuilder line = new StringBuilder();
  private boolean dropping, pending;
  private int velocity;
  private long noteAt;

  SerialProtocol(Sink sink) {
    this.sink = sink;
  }

  void feed(byte[] bytes, int length, long now) {
    for (int i = 0; i < length; i++) {
      char c = (char) (bytes[i] & 255);
      if (c == '\n') {
        if (!dropping) parse(line.toString().trim(), now);
        line.setLength(0);
        dropping = false;
      } else if (!dropping) {
        if (line.length() == 96) {
          line.setLength(0);
          dropping = true;
        } else line.append(c);
      }
    }
  }

  private void parse(String s, long now) {
    try {
      String[] p = s.split(",");
      if (p.length == 3) {
        int a = Integer.parseInt(p[1]), b = Integer.parseInt(p[2]);
        if (p[0].equals("NOTE") && a >= 0 && a < 128 && b >= 0 && b < 128) {
          pending = true;
          velocity = b;
          noteAt = now;
        } else if (p[0].equals("RAW") && a >= 0 && a < 8 && b >= 0 && b <= 1023) {
          boolean hit = pending && now >= noteAt && now - noteAt < 250;
          pending = false;
          if (hit && velocity > 0) sink.hit(a, velocity, b);
          sink.raw(a, b);
        }
      } else if (p.length == 4 && p[0].equals("PEDALS")) {
        long t = Long.parseLong(p[1]);
        int k = Integer.parseInt(p[2]), h = Integer.parseInt(p[3]);
        if (t >= 0 && t <= 0xffffffffL && k >= 0 && k <= 1023 && h >= 0 && h <= 1023)
          sink.pedals(t, k, h);
      }
    } catch (NumberFormatException ignored) {
    }
  }

  static int[] assign(int[] original, int part, int input) {
    int[] n = original.clone();
    for (int i = 0; i < 8; i++)
      if (n[i] == input) {
        n[i] = n[part];
        break;
      }
    n[part] = input;
    return n;
  }

  static float position(int raw, int rest, int down) {
    return Math.abs(down - rest) < 20
        ? 0
        : Math.max(0, Math.min(1, (raw - rest) / (float) (down - rest)));
  }
}
