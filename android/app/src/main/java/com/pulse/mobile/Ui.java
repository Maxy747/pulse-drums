package com.pulse.mobile;

import android.content.*;
import android.graphics.*;
import android.graphics.drawable.*;
import android.text.TextUtils;
import android.view.*;
import android.widget.*;

/** Controls styled after the Windows app (Main.xaml), recoloured through {@link Palette}. */
final class Ui {
  interface IntChange {
    void changed(int value);
  }

  final Context context;
  final float density;

  Ui(Context context) {
    this.context = context;
    density = context.getResources().getDisplayMetrics().density;
  }

  int dp(float value) {
    return Math.round(value * density);
  }

  GradientDrawable shape(int fill, int line, float radius) {
    GradientDrawable d = new GradientDrawable();
    d.setColor(fill);
    d.setCornerRadius(dp(radius));
    if (line != 0) d.setStroke(Math.max(1, dp(1)), line);
    return d;
  }

  TextView text(CharSequence value, float sp, int color) {
    TextView v = new TextView(context);
    v.setText(value);
    v.setTextSize(sp);
    v.setTextColor(color);
    v.setIncludeFontPadding(false);
    v.setLineSpacing(0, 1.15f);
    return v;
  }

  TextView caption(String value) {
    TextView v = text(value.toUpperCase(java.util.Locale.ROOT), 10, Palette.MUTED);
    v.setTypeface(Typeface.DEFAULT_BOLD);
    v.setLetterSpacing(.12f);
    return v;
  }

  TextView muted(CharSequence value) {
    return text(value, 12, Palette.MUTED);
  }

  Button button(String label, Runnable action) {
    Button b = new Button(context);
    b.setText(label);
    b.setAllCaps(false);
    b.setTextSize(13);
    b.setTextColor(Palette.BUTTON_TEXT);
    b.setMinWidth(0);
    b.setMinimumWidth(0);
    b.setMinHeight(dp(44));
    b.setMinimumHeight(dp(44));
    b.setSingleLine();
    b.setEllipsize(TextUtils.TruncateAt.END);
    b.setPadding(dp(12), 0, dp(12), 0);
    b.setStateListAnimator(null);
    StateListDrawable bg = new StateListDrawable();
    bg.addState(
        new int[] {android.R.attr.state_pressed},
        shape(Palette.TRACK, Palette.c(Palette.ACCENT), 7));
    bg.addState(
        new int[] {android.R.attr.state_focused},
        shape(Palette.BUTTON, Palette.c(Palette.ACCENT), 7));
    bg.addState(new int[0], shape(Palette.BUTTON, Palette.BUTTON_LINE, 7));
    b.setBackground(bg);
    b.setOnClickListener(v -> action.run());
    return b;
  }

  Button primary(String label, Runnable action) {
    Button b = button(label, action);
    StateListDrawable bg = new StateListDrawable();
    bg.addState(
        new int[] {android.R.attr.state_pressed},
        shape(Palette.alpha(Palette.c(Palette.ACCENT), .65f), 0, 7));
    bg.addState(new int[0], shape(Palette.c(Palette.ACCENT), 0, 7));
    b.setBackground(bg);
    b.setTextColor(Palette.ON_ACCENT);
    b.setTypeface(Typeface.DEFAULT_BOLD);
    return b;
  }

  Button small(String label, Runnable action) {
    Button b = button(label, action);
    b.setTextSize(11);
    b.setMinHeight(dp(36));
    b.setMinimumHeight(dp(36));
    b.setPadding(dp(10), 0, dp(10), 0);
    return b;
  }

  LinearLayout card() {
    LinearLayout l = column();
    l.setBackground(shape(Palette.CARD, Palette.CARD_LINE, 12));
    l.setPadding(dp(16), dp(14), dp(16), dp(16));
    return l;
  }

  LinearLayout column() {
    LinearLayout l = new LinearLayout(context);
    l.setOrientation(LinearLayout.VERTICAL);
    return l;
  }

  LinearLayout row() {
    LinearLayout l = new LinearLayout(context);
    l.setOrientation(LinearLayout.HORIZONTAL);
    l.setGravity(Gravity.CENTER_VERTICAL);
    return l;
  }

  /** Equal-width row with small gaps, used for button groups. */
  LinearLayout buttons(View... views) {
    LinearLayout r = row();
    for (int i = 0; i < views.length; i++) {
      LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(0, -2, 1);
      if (i > 0) p.leftMargin = dp(8);
      r.addView(views[i], p);
    }
    return r;
  }

  void add(LinearLayout into, View child, float topMargin) {
    LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-1, -2);
    p.topMargin = dp(topMargin);
    into.addView(child, p);
  }

  View spacer() {
    View v = new View(context);
    v.setLayoutParams(new LinearLayout.LayoutParams(0, 1, 1));
    return v;
  }

  EditText field(String hint, String value, int inputType) {
    EditText e = new EditText(context);
    e.setSingleLine();
    e.setHint(hint);
    e.setText(value);
    e.setInputType(inputType);
    e.setTextSize(14);
    e.setTextColor(0xffeaf0e4);
    e.setHintTextColor(Palette.MUTED);
    e.setMinHeight(dp(44));
    e.setPadding(dp(12), dp(8), dp(12), dp(8));
    StateListDrawable bg = new StateListDrawable();
    bg.addState(
        new int[] {android.R.attr.state_focused},
        shape(Palette.FIELD, Palette.c(Palette.ACCENT), 6));
    bg.addState(new int[0], shape(Palette.FIELD, Palette.FIELD_LINE, 6));
    e.setBackground(bg);
    e.setBackgroundTintList(null);
    return e;
  }

  /** Label + value header above a PC-style slider. */
  final class SliderRow extends LinearLayout {
    final TextView value;
    final Slider slider;

    SliderRow(String label, int max, int progress, IntChange change, Runnable release) {
      super(context);
      setOrientation(VERTICAL);
      LinearLayout head = row();
      head.addView(text(label, 13, Palette.SOFT_TEXT), new LinearLayout.LayoutParams(0, -2, 1));
      value = text("", 13, Palette.c(Palette.ACCENT));
      head.addView(value);
      addView(head);
      slider = new Slider(max, progress, change, release);
      slider.setContentDescription(label);
      addView(slider, new LayoutParams(-1, dp(34)));
    }
  }

  /** Thin track, accent fill and round thumb, as in the Windows Slider template. */
  final class Slider extends View {
    final IntChange change;
    final Runnable release;
    final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    int max, progress;

    Slider(int max, int progress, IntChange change, Runnable release) {
      super(context);
      this.max = max;
      this.progress = Math.max(0, Math.min(max, progress));
      this.change = change;
      this.release = release;
      setFocusable(true);
    }

    void set(int value) {
      progress = Math.max(0, Math.min(max, value));
      invalidate();
    }

    @Override
    protected void onDraw(Canvas c) {
      float r = dp(7), left = r, right = getWidth() - r, y = getHeight() / 2f;
      float x = left + (right - left) * progress / Math.max(1, max);
      paint.setStrokeCap(Paint.Cap.ROUND);
      paint.setStrokeWidth(dp(3));
      paint.setColor(Palette.TRACK);
      c.drawLine(left, y, right, y, paint);
      paint.setColor(Palette.c(Palette.ACCENT));
      c.drawLine(left, y, x, y, paint);
      c.drawCircle(x, y, isPressed() ? r * 1.25f : r, paint);
    }

    @Override
    public boolean onTouchEvent(MotionEvent e) {
      if (!isEnabled()) return false;
      switch (e.getActionMasked()) {
        case MotionEvent.ACTION_DOWN:
          getParent().requestDisallowInterceptTouchEvent(true);
          setPressed(true);
          // fall through
        case MotionEvent.ACTION_MOVE:
          float r = dp(7);
          float t = (e.getX() - r) / Math.max(1, getWidth() - 2 * r);
          int next = Math.round(Math.max(0, Math.min(1, t)) * max);
          if (next != progress) {
            progress = next;
            if (change != null) change.changed(progress);
            invalidate();
          }
          return true;
        case MotionEvent.ACTION_UP:
        case MotionEvent.ACTION_CANCEL:
          setPressed(false);
          getParent().requestDisallowInterceptTouchEvent(false);
          invalidate();
          if (release != null) release.run();
          if (e.getActionMasked() == MotionEvent.ACTION_UP) performClick();
          return true;
      }
      return true;
    }

    @Override
    public boolean performClick() {
      super.performClick();
      return true;
    }
  }

  /** Pill switch with label, as in the Windows CheckBox template. */
  View toggle(String label, boolean checked, java.util.function.Consumer<Boolean> change) {
    LinearLayout r = row();
    boolean[] state = {checked};
    View pill =
        new View(context) {
          final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
          final RectF box = new RectF();

          @Override
          protected void onDraw(Canvas c) {
            box.set(0, (getHeight() - dp(18)) / 2f, dp(32), (getHeight() + dp(18)) / 2f);
            p.setColor(state[0] ? Palette.c(Palette.ACCENT) : 0xff373d33);
            c.drawRoundRect(box, dp(9), dp(9), p);
            p.setColor(state[0] ? Palette.ON_ACCENT : Palette.MUTED);
            c.drawCircle(state[0] ? dp(23) : dp(9), getHeight() / 2f, dp(6), p);
          }
        };
    r.addView(pill, new LinearLayout.LayoutParams(dp(32), dp(44)));
    TextView t = text(label, 13, Palette.SOFT_TEXT);
    t.setPadding(dp(12), 0, 0, 0);
    r.addView(t, new LinearLayout.LayoutParams(0, -2, 1));
    r.setClickable(true);
    r.setFocusable(true);
    r.setContentDescription(label);
    r.setOnClickListener(
        v -> {
          state[0] = !state[0];
          pill.invalidate();
          change.accept(state[0]);
        });
    return r;
  }

  /** One-of-N buttons; the active one gets the accent outline (site: aria-pressed). */
  LinearLayout segmented(String[] labels, int selected, IntChange change) {
    LinearLayout r = row();
    Button[] all = new Button[labels.length];
    for (int i = 0; i < labels.length; i++) {
      int index = i;
      all[i] =
          button(
              labels[i],
              () -> {
                for (int j = 0; j < all.length; j++) mark(all[j], j == index);
                change.changed(index);
              });
      all[i].setTextSize(12);
      all[i].setPadding(dp(6), 0, dp(6), 0);
      mark(all[i], i == selected);
      LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(0, -2, 1);
      if (i > 0) p.leftMargin = dp(6);
      r.addView(all[i], p);
    }
    return r;
  }

  void mark(Button b, boolean on) {
    b.setSelected(on);
    if (on) {
      b.setBackground(shape(Palette.c(0xff1d2a17), Palette.c(Palette.ACCENT), 7));
      b.setTextColor(Palette.c(Palette.ACCENT));
    } else {
      StateListDrawable bg = new StateListDrawable();
      bg.addState(
          new int[] {android.R.attr.state_pressed},
          shape(Palette.TRACK, Palette.c(Palette.ACCENT), 7));
      bg.addState(new int[0], shape(Palette.BUTTON, Palette.BUTTON_LINE, 7));
      b.setBackground(bg);
      b.setTextColor(Palette.BUTTON_TEXT);
    }
  }

  /** Horizontal level bar (pedal position). */
  final class Meter extends View {
    final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    float level;

    Meter() {
      super(context);
    }

    void set(float value) {
      value = Math.max(0, Math.min(1, value));
      if (value != level) {
        level = value;
        invalidate();
      }
    }

    @Override
    protected void onDraw(Canvas c) {
      float y = getHeight() / 2f, h = dp(3);
      paint.setStrokeCap(Paint.Cap.ROUND);
      paint.setStrokeWidth(h);
      paint.setColor(0xff26352a);
      c.drawLine(h, y, getWidth() - h, y, paint);
      paint.setColor(Palette.c(Palette.ACCENT));
      if (level > 0) c.drawLine(h, y, h + (getWidth() - 2 * h) * level, y, paint);
    }
  }

  /** The Pulse mark from the Windows header. */
  View logo() {
    return new View(context) {
      final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
      final RectF r = new RectF();

      @Override
      protected void onDraw(Canvas c) {
        float s = Math.min(getWidth(), getHeight()) / 64f;
        c.save();
        c.translate((getWidth() - 64 * s) / 2, (getHeight() - 64 * s) / 2);
        c.scale(s, s);
        int accent = Palette.c(Palette.ACCENT);
        p.setStyle(Paint.Style.STROKE);
        p.setStrokeCap(Paint.Cap.ROUND);
        p.setColor(accent);
        p.setStrokeWidth(4);
        c.drawLine(15, 43, 9, 56, p);
        c.drawLine(49, 43, 55, 56, p);
        p.setStyle(Paint.Style.FILL);
        p.setColor(Palette.c(0xff172010));
        c.drawCircle(32, 27, 23, p);
        p.setStyle(Paint.Style.STROKE);
        p.setColor(accent);
        c.drawCircle(32, 27, 23, p);
        p.setStrokeWidth(1);
        c.drawCircle(32, 27, 17, p);
        p.setStrokeWidth(3);
        p.setStrokeCap(Paint.Cap.BUTT);
        c.drawLine(32, 35, 32, 52, p);
        p.setStyle(Paint.Style.FILL);
        r.set(27, 29, 37, 38);
        c.drawRoundRect(r, 2, 2, p);
        r.set(26, 49, 38, 61);
        c.drawRoundRect(r, 3, 3, p);
        c.restore();
      }
    };
  }

  /** Theme swatch with the selection ring from the Windows ThemeDot style. */
  View swatch(int color, boolean active, String label, Runnable pick) {
    View v =
        new View(context) {
          final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);

          @Override
          protected void onDraw(Canvas c) {
            float cx = getWidth() / 2f, cy = getHeight() / 2f;
            p.setStyle(Paint.Style.FILL);
            p.setColor(color);
            c.drawCircle(cx, cy, dp(8), p);
            if (active) {
              p.setStyle(Paint.Style.STROKE);
              p.setStrokeWidth(dp(1.5f));
              p.setColor(Palette.TEXT);
              c.drawCircle(cx, cy, dp(13), p);
            }
          }
        };
    v.setContentDescription(label);
    v.setClickable(true);
    v.setFocusable(true);
    v.setOnClickListener(x -> pick.run());
    return v;
  }
}
