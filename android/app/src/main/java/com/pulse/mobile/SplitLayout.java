package com.pulse.mobile;

import android.content.Context;
import android.view.*;

/**
 * Two children: the kit stage and the settings panel. Landscape puts them side by side (kit left,
 * settings right like the Windows pad-settings column); portrait pins the kit on top and gives the
 * rest of the height to the scrolling settings. Re-evaluated on every measure, so rotation needs no
 * activity restart.
 */
final class SplitLayout extends ViewGroup {
  final int gap, minPanel;
  boolean wide;

  SplitLayout(Context c, int gap, int minPanel) {
    super(c);
    this.gap = gap;
    this.minPanel = minPanel;
  }

  @Override
  protected void onMeasure(int widthSpec, int heightSpec) {
    int w = MeasureSpec.getSize(widthSpec), h = MeasureSpec.getSize(heightSpec);
    View stage = getChildAt(0), panel = getChildAt(1);
    wide = w > h;
    if (wide) {
      int stageW = Math.max(w / 2, Math.min(Math.round(w * .62f), w - gap - minPanel));
      measure(stage, stageW, h);
      measure(panel, w - stageW - gap, h);
    } else {
      // Stage keeps the kit's 1000:620 aspect plus its header strip, capped at ~half the screen.
      int header = stage instanceof Stage ? ((Stage) stage).headerHeight() : 0;
      int stageH = Math.min(Math.round(w * KitView.H / KitView.W) + header, Math.round(h * .52f));
      measure(stage, w, stageH);
      measure(panel, w, Math.max(0, h - stageH - gap));
    }
    setMeasuredDimension(w, h);
  }

  static void measure(View v, int w, int h) {
    v.measure(
        MeasureSpec.makeMeasureSpec(w, MeasureSpec.EXACTLY),
        MeasureSpec.makeMeasureSpec(h, MeasureSpec.EXACTLY));
  }

  @Override
  protected void onLayout(boolean changed, int l, int t, int r, int b) {
    View stage = getChildAt(0), panel = getChildAt(1);
    stage.layout(0, 0, stage.getMeasuredWidth(), stage.getMeasuredHeight());
    if (wide) {
      int x = stage.getMeasuredWidth() + gap;
      panel.layout(x, 0, x + panel.getMeasuredWidth(), panel.getMeasuredHeight());
    } else {
      int y = stage.getMeasuredHeight() + gap;
      panel.layout(0, y, panel.getMeasuredWidth(), y + panel.getMeasuredHeight());
    }
  }

  /** Marker for the stage container so the split can reserve its header strip. */
  interface Stage {
    int headerHeight();
  }
}
