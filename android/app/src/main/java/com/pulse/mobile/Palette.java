package com.pulse.mobile;

/** PC palette (green is the source of truth); red/blue use the same transform as Theme.cs. */
final class Palette {
  static final String[] NAMES = {"Green", "Red", "Blue"};
  static int theme;

  static final int BG = 0xff101211,
      CARD = 0xff181c17,
      CARD_LINE = 0xff2b3227,
      INSET = 0xff1b2018,
      INSET_LINE = 0xff39432d,
      BUTTON = 0xff242923,
      BUTTON_LINE = 0xff363d31,
      FIELD = 0xff282f23,
      FIELD_LINE = 0xff46513a,
      TRACK = 0xff343b30,
      TEXT = 0xffeef0e8,
      BUTTON_TEXT = 0xffe7ebdd,
      SOFT_TEXT = 0xffd2d9cb,
      MUTED = 0xff92998e,
      ACCENT = 0xffc5f36b,
      ON_ACCENT = 0xff1a2712,
      WARN = 0xffffd783;

  static final int[] SWATCHES = {0xffc5f36b, 0xfff36b76, 0xff6bb6f3};

  /** Recolours a green-palette colour for the active theme, keeping luminance range and alpha. */
  static int c(int argb) {
    int a = argb >>> 24, r = (argb >> 16) & 255, g = (argb >> 8) & 255, b = argb & 255;
    if (theme == 0 || g <= r || g <= b) return argb;
    int low = Math.min(r, b), high = g;
    if (theme == 1) return argb(a, high, low, (int) (low + (high - low) * .08));
    return argb(a, low, (int) (low + (high - low) * .55), high);
  }

  static int argb(int a, int r, int g, int b) {
    return (a << 24) | (r << 16) | (g << 8) | b;
  }

  static int mix(int from, int to, float amount) {
    int a = c(from), b = c(to);
    return argb(
        255,
        lerp((a >> 16) & 255, (b >> 16) & 255, amount),
        lerp((a >> 8) & 255, (b >> 8) & 255, amount),
        lerp(a & 255, b & 255, amount));
  }

  static int alpha(int argb, float alpha) {
    return ((int) (Math.max(0, Math.min(1, alpha)) * 255) << 24) | (argb & 0xffffff);
  }

  private static int lerp(int a, int b, float t) {
    return Math.round(a + (b - a) * t);
  }

  private Palette() {}
}
