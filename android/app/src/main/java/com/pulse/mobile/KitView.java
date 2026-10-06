package com.pulse.mobile;

import android.content.*;
import android.graphics.*;
import android.os.SystemClock;
import android.view.*;

/** Top-down kit, ported from the Windows KitView artwork (1000 x 620 virtual canvas). */
final class KitView extends View {
  interface Listener {
    void hit(int part);
  }

  static final String[] NAMES = {
    "Hi-hat", "Crash", "Low tom", "Snare", "Mid tom", "Kick", "Floor tom", "Ride"
  };
  static final float W = 1000, H = 620;
  static final float[][] CENTERS = {
    {125, 465}, {210, 155}, {410, 195}, {350, 370}, {590, 195}, {505, 373}, {665, 387}, {835, 350}
  };
  static final float[] RADII = {110, 126, 80, 72, 80, 94, 85, 139};
  static final int[] ORDER = {5, 2, 4, 3, 6, 1, 0, 7};

  final long[] hits = new long[8];
  final float[] strength = new float[8];
  final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
  final Paint text = new Paint(Paint.ANTI_ALIAS_FLAG);
  final RectF oval = new RectF();
  final DashPathEffect dash;
  final float density;
  int selected = 0, learning = -1;
  // Idle gradients are cached; only pieces that are glowing allocate a fresh one per frame.
  final Shader[] idle = new Shader[8];
  Shader stage;
  int cachedTheme = -1, stageW, stageH;
  Listener listener;

  KitView(Context c, Listener l) {
    super(c);
    listener = l;
    density = c.getResources().getDisplayMetrics().density;
    dash = new DashPathEffect(new float[] {6, 4}, 0);
    text.setTextAlign(Paint.Align.CENTER);
    setContentDescription("Drum kit. Tap a drum to play and select it.");
  }

  void flash(int p, int velocity) {
    hits[p] = SystemClock.elapsedRealtime();
    strength[p] = Math.max(.45f, Math.min(1, velocity / 127f));
    postInvalidateOnAnimation();
  }

  float energy(int p, long now) {
    float t = (now - hits[p]) / 320f;
    if (t >= 1 || hits[p] == 0) return 0;
    return strength[p] * (1 - t) * (1 - t);
  }

  float scale() {
    float pad = 6 * density;
    return Math.min((getWidth() - pad * 2) / W, (getHeight() - pad * 2) / H);
  }

  float offsetX() {
    return (getWidth() - W * scale()) / 2;
  }

  float offsetY() {
    return (getHeight() - H * scale()) / 2;
  }

  @Override
  protected void onDraw(Canvas c) {
    super.onDraw(c);
    float s = scale();
    if (s <= 0) return;
    long now = SystemClock.elapsedRealtime();
    float corner = 14 * density;
    paint.setStyle(Paint.Style.FILL);
    if (cachedTheme != Palette.theme || stageW != getWidth() || stageH != getHeight()) {
      cachedTheme = Palette.theme;
      stageW = getWidth();
      stageH = getHeight();
      java.util.Arrays.fill(idle, null);
      stage =
          new RadialGradient(
              stageW / 2f,
              stageH * .42f,
              Math.max(stageW, stageH) * .7f,
              Palette.c(0xff131d15),
              Palette.c(0xff090e0b),
              Shader.TileMode.CLAMP);
    }
    paint.setShader(stage);
    oval.set(0, 0, getWidth(), getHeight());
    c.drawRoundRect(oval, corner, corner, paint);
    paint.setShader(null);
    paint.setStyle(Paint.Style.STROKE);
    paint.setStrokeWidth(density);
    paint.setColor(Palette.c(0xff202f25));
    oval.inset(density / 2, density / 2);
    c.drawRoundRect(oval, corner, corner, paint);

    c.save();
    c.translate(offsetX(), offsetY());
    c.scale(s, s);
    stand(c, 195, 264, 139, 335);
    stand(c, 139, 335, 322, 501);
    stand(c, 322, 501, 354, 546);
    stand(c, 771, 439, 698, 527);
    stand(c, 113, 565, 102, 594);
    boolean active = false;
    for (int p : ORDER) {
      float e = energy(p, now);
      active |= e > 0;
      piece(c, p, e);
    }
    c.restore();
    for (int p : ORDER) label(c, p, energy(p, now), s);
    text.setTextSize(Math.max(10 * s, 8 * density));
    text.setColor(Palette.c(0xff496452));
    text.setLetterSpacing(.18f);
    text.setTypeface(Typeface.DEFAULT);
    c.drawText("PLAYER POSITION", offsetX() + 505 * s, offsetY() + 606 * s, text);
    text.setLetterSpacing(0);
    if (active) postInvalidateOnAnimation();
  }

  void stand(Canvas c, float ax, float ay, float bx, float by) {
    paint.setStyle(Paint.Style.STROKE);
    paint.setStrokeCap(Paint.Cap.ROUND);
    paint.setStrokeWidth(9);
    paint.setColor(Palette.c(0xff183d25));
    c.drawLine(ax, ay, bx, by, paint);
    paint.setStrokeWidth(2);
    paint.setColor(Palette.c(0xff285c36));
    c.drawLine(ax, ay, bx, by, paint);
    paint.setStyle(Paint.Style.FILL);
    paint.setColor(Palette.c(0xff102c1a));
    c.drawCircle(bx, by, 8, paint);
    paint.setStyle(Paint.Style.STROKE);
    paint.setColor(Palette.c(0xff285c36));
    c.drawCircle(bx, by, 8, paint);
    paint.setStrokeCap(Paint.Cap.BUTT);
  }

  void ellipse(Canvas c, float x, float y, float rx, float ry) {
    oval.set(x - rx, y - ry, x + rx, y + ry);
    c.drawOval(oval, paint);
  }

  void piece(Canvas c, int i, float energy) {
    float x = CENTERS[i][0], y = CENTERS[i][1], r = RADII[i], ry = r * .91f;
    boolean cymbal = i == 0 || i == 1 || i == 7;
    int rim = Palette.mix(0xff27683b, 0xffb6ffb0, energy);
    if (energy > .02f) {
      paint.setStyle(Paint.Style.STROKE);
      for (int g = 8; g >= 1; g--) {
        paint.setStrokeWidth(g * 3);
        paint.setColor(Palette.alpha(Palette.c(0xff57ff75), energy * .05f * (9 - g)));
        ellipse(c, x, y, r + 3, ry + 3);
      }
    }
    if (!cymbal) {
      paint.setStyle(Paint.Style.FILL);
      paint.setColor(Palette.c(0xff07130c));
      ellipse(c, x, y + 15, r, ry);
      paint.setStyle(Paint.Style.STROKE);
      paint.setStrokeWidth(2);
      paint.setColor(rim);
      ellipse(c, x, y + 15, r, ry);
      for (int lug = 0; lug < 8; lug++) {
        double angle = (lug + .5) * Math.PI / 4;
        float lx = x + (float) Math.cos(angle) * r, ly = y + (float) Math.sin(angle) * ry;
        oval.set(lx - 5, ly - 5, lx + 5, ly + 14);
        paint.setStyle(Paint.Style.FILL);
        paint.setColor(Palette.c(0xff163b23));
        c.drawRoundRect(oval, 2, 2, paint);
        paint.setStyle(Paint.Style.STROKE);
        paint.setStrokeWidth(1);
        paint.setColor(rim);
        c.drawRoundRect(oval, 2, 2, paint);
      }
    }
    paint.setStyle(Paint.Style.FILL);
    if (energy > 0 || idle[i] == null) {
      Shader head =
          new RadialGradient(
              x - r * .36f,
              y - ry * .56f,
              r * 1.75f,
              Palette.mix(0xff124021, 0xff176f2c, energy),
              Palette.mix(0xff091b10, 0xff073c18, energy),
              Shader.TileMode.CLAMP);
      if (energy == 0) idle[i] = head;
      paint.setShader(head);
    } else paint.setShader(idle[i]);
    ellipse(c, x, y, r, ry);
    paint.setShader(null);
    paint.setStyle(Paint.Style.STROKE);
    paint.setStrokeWidth(2.4f);
    paint.setColor(rim);
    ellipse(c, x, y, r, ry);
    paint.setStrokeWidth(4);
    paint.setColor(Palette.c(0xff06140b));
    ellipse(c, x, y, r - 5, ry - 5);
    paint.setStrokeWidth(cymbal ? .7f : 1.3f);
    paint.setColor(rim);
    ellipse(c, x, y, r - 9, ry - 9);
    if (cymbal) {
      paint.setStrokeWidth(.65f);
      paint.setColor(Palette.alpha(rim, .3f + energy * .35f));
      for (float groove = 18; groove < r - 9; groove += 5) ellipse(c, x, y, groove, groove * .91f);
      paint.setStyle(Paint.Style.FILL);
      paint.setColor(Palette.c(0xff0a2212));
      ellipse(c, x, y, 16, 14);
      paint.setStyle(Paint.Style.STROKE);
      paint.setStrokeWidth(2);
      paint.setColor(rim);
      ellipse(c, x, y, 16, 14);
      paint.setStrokeWidth(4);
      c.drawLine(x, y, x + 49, y - 45, paint);
      paint.setStyle(Paint.Style.FILL);
      paint.setColor(Palette.c(0xff112c17));
      c.drawCircle(x, y, 5, paint);
      paint.setStyle(Paint.Style.STROKE);
      paint.setStrokeWidth(2);
      paint.setColor(rim);
      c.drawCircle(x, y, 5, paint);
    }
    if (i == selected || i == learning) {
      paint.setStyle(Paint.Style.STROKE);
      paint.setStrokeWidth(i == learning ? 2.2f : 1.6f);
      paint.setColor(i == learning ? Palette.WARN : Palette.c(0xff86b997));
      paint.setPathEffect(dash);
      ellipse(c, x, y, r + 11, ry + 11);
      paint.setPathEffect(null);
    }
    if (i == 5) {
      paint.setStyle(Paint.Style.STROKE);
      paint.setStrokeWidth(4);
      paint.setColor(rim);
      c.drawLine(505, 444, 505, 531, paint);
      for (int p = 0; p < 2; p++) {
        oval.set(461 + p * 49, 467, 501 + p * 49, 537);
        paint.setStyle(Paint.Style.FILL);
        paint.setColor(Palette.c(energy > .1f ? 0xff204f27 : 0xff112b1b));
        c.drawRoundRect(oval, 5, 5, paint);
        paint.setStyle(Paint.Style.STROKE);
        paint.setStrokeWidth(2);
        paint.setColor(rim);
        c.drawRoundRect(oval, 5, 5, paint);
      }
      oval.set(497, 424, 513, 447);
      paint.setStyle(Paint.Style.FILL);
      paint.setColor(Palette.c(0xff215734));
      c.drawRoundRect(oval, 3, 3, paint);
      paint.setStyle(Paint.Style.STROKE);
      paint.setColor(rim);
      c.drawRoundRect(oval, 3, 3, paint);
    }
  }

  /** Labels are drawn in screen space so they stay readable when the kit is phone-sized. */
  void label(Canvas c, int i, float energy, float s) {
    boolean cymbal = i == 0 || i == 1 || i == 7;
    float x = offsetX() + CENTERS[i][0] * s;
    float top = offsetY() + (CENTERS[i][1] + (cymbal ? 29 : -18)) * s;
    float number = Math.max(21 * s, 12 * density), name = Math.max(17 * s, 9.5f * density);
    text.setTypeface(Typeface.DEFAULT);
    text.setTextSize(name);
    float width = Math.max(118 * s, text.measureText(NAMES[i]) + 10 * density);
    float height = number + name + 7 * density;
    paint.setStyle(Paint.Style.FILL);
    paint.setColor(0xb007100a);
    oval.set(x - width / 2, top - 3 * density, x + width / 2, top + height);
    float radius = 6 * density;
    c.drawRoundRect(oval, radius, radius, paint);
    text.setTextSize(number);
    text.setTypeface(Typeface.DEFAULT_BOLD);
    text.setColor(energy > .1f ? Palette.c(0xffe0ffce) : Palette.c(0xffa2c7ac));
    c.drawText(String.valueOf(i + 1), x, top + number * .85f, text);
    text.setTypeface(Typeface.DEFAULT);
    text.setTextSize(name);
    text.setColor(0xffc4d9ca);
    c.drawText(NAMES[i], x, top + number + name * .9f, text);
  }

  int partAt(float sx, float sy) {
    float s = scale();
    if (s <= 0) return -1;
    float x = (sx - offsetX()) / s, y = (sy - offsetY()) / s;
    for (int j = ORDER.length - 1; j >= 0; j--) {
      int i = ORDER[j];
      float dx = (x - CENTERS[i][0]) / RADII[i], dy = (y - CENTERS[i][1]) / (RADII[i] * .91f);
      if (dx * dx + dy * dy <= 1.08f) return i;
    }
    if (x >= 459 && x <= 554 && y >= 464 && y <= 554) return 5;
    return -1;
  }

  @Override
  public boolean onTouchEvent(MotionEvent e) {
    int action = e.getActionMasked();
    if (action == MotionEvent.ACTION_DOWN || action == MotionEvent.ACTION_POINTER_DOWN) {
      int n = e.getActionIndex();
      int p = partAt(e.getX(n), e.getY(n));
      if (p >= 0) {
        selected = p;
        listener.hit(p);
        invalidate();
      }
      if (action == MotionEvent.ACTION_DOWN) performClick();
    }
    return true;
  }

  @Override
  public boolean performClick() {
    super.performClick();
    return true;
  }
}
