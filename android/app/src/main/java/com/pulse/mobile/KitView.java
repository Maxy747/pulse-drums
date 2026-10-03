package com.pulse.mobile;

import android.content.*;
import android.graphics.*;
import android.os.SystemClock;
import android.view.*;

final class KitView extends View {
  interface Listener {
    void hit(int part);
  }

  static final String[] NAMES = {
    "Hi-hat", "Crash", "Low tom", "Snare", "Mid tom", "Kick", "Floor tom", "Ride"
  };
  final float[][] shapes = {
    {.13f, .75f, .115f},
    {.23f, .24f, .14f},
    {.43f, .29f, .09f},
    {.39f, .58f, .085f},
    {.62f, .29f, .09f},
    {.54f, .60f, .115f},
    {.70f, .68f, .092f},
    {.86f, .46f, .135f}
  };
  final long[] hits = new long[8];
  final Paint paint = new Paint(3);
  int accent = Color.rgb(115, 245, 157), selected = 0;
  Listener listener;

  KitView(Context c, Listener l) {
    super(c);
    listener = l;
    setContentDescription("Drum kit. Tap a drum to play and select it.");
    setLayerType(View.LAYER_TYPE_SOFTWARE, null);
  }

  @Override
  protected void onMeasure(int widthSpec, int heightSpec) {
    int width = MeasureSpec.getSize(widthSpec);
    setMeasuredDimension(width, Math.round(width * .72f));
  }

  void flash(int p) {
    hits[p] = SystemClock.elapsedRealtime();
    postInvalidate();
  }

  protected void onDraw(Canvas c) {
    super.onDraw(c);
    float w = getWidth(), h = getHeight(), scale = Math.min(w, h * 1.6f);
    long now = SystemClock.elapsedRealtime();
    boolean active = false;
    c.drawColor(Color.rgb(8, 16, 12));
    int[] order = {5, 2, 4, 3, 6, 1, 0, 7};
    for (int p : order) {
      float x = shapes[p][0] * w, y = shapes[p][1] * h, r = shapes[p][2] * scale;
      float glow = Math.max(0, 1 - (now - hits[p]) / 240f);
      active |= glow > 0;
      paint.setStyle(Paint.Style.FILL);
      paint.setColor(Color.rgb(12 + (int) (glow * 12), 35 + (int) (glow * 40), 23));
      c.drawCircle(x, y, r, paint);
      paint.setStyle(Paint.Style.STROKE);
      paint.setStrokeWidth(p == selected ? 3 : 1.5f);
      paint.setColor(accent);
      paint.setAlpha((int) (glow * 155 + 100));
      if (glow > 0) paint.setShadowLayer(14 * glow, 0, 0, accent);
      c.drawCircle(x, y, r, paint);
      paint.clearShadowLayer();
      if (p == 0 || p == 1 || p == 7) {
        paint.setAlpha(55);
        for (int ring = 1; ring < 10; ring++) c.drawCircle(x, y, r * ring / 10, paint);
      } else {
        paint.setAlpha(90);
        c.drawCircle(x, y, r * .9f, paint);
      }
      paint.setStyle(Paint.Style.FILL);
      paint.setColor(Color.WHITE);
      paint.setTextAlign(Paint.Align.CENTER);
      paint.setTextSize(Math.max(11, scale * .028f));
      c.drawText((p + 1) + "", x, y - scale * .012f, paint);
      paint.setTextSize(Math.max(10, scale * .023f));
      c.drawText(NAMES[p], x, y + scale * .025f, paint);
    }
    if (active) postInvalidateOnAnimation();
  }

  public boolean onTouchEvent(android.view.MotionEvent e) {
    int action = e.getActionMasked();
    if (action == MotionEvent.ACTION_DOWN || action == MotionEvent.ACTION_POINTER_DOWN) {
      int n = e.getActionIndex();
      float x = e.getX(n), y = e.getY(n);
      float scale = Math.min(getWidth(), getHeight() * 1.6f);
      int[] order = {7, 0, 1, 6, 3, 4, 2, 5};
      for (int p : order) {
        if (Math.hypot(x - shapes[p][0] * getWidth(), y - shapes[p][1] * getHeight())
            <= shapes[p][2] * scale) {
          selected = p;
          listener.hit(p);
          invalidate();
          break;
        }
      }
      performClick();
      return true;
    }
    return true;
  }

  public boolean performClick() {
    super.performClick();
    return true;
  }
}
