package com.pulse.mobile;

import android.app.*;
import android.content.*;
import android.graphics.Typeface;
import android.graphics.drawable.ColorDrawable;
import android.media.*;
import android.os.*;
import android.text.InputType;
import android.text.TextUtils;
import android.view.*;
import android.widget.*;
import java.io.*;
import java.util.*;

public final class MainActivity extends Activity {
  static final int[] DEFAULT_HITS = {140, 20, 20, 20, 10, 60, 10, 10},
      DEFAULT_RESETS = {40, 5, 10, 5, 1, 1, 5, 9};
  static final String[] OUTPUTS = {"Phone", "Pulse PC", "Both"};
  static final int[] GUARDS = {110, 70, 35};

  final Handler ui = new Handler(Looper.getMainLooper());
  SharedPreferences prefs;
  DrumAudio audio;
  UsbHub usb;
  PcRelay relay;
  KitView kit;
  TextView liveText, hitsText, padName, padInput, soundName, thresholdNote;
  TextView learnTitle, learnText, setupText, pedalText, usbText, relayText;
  Ui.SliderRow gainRow, hitRow, resetRow, volumeRow;
  Ui.Meter kickMeter, hatMeter;
  Button openHat;
  LinearLayout learnCard;
  EditText host, code;
  String hostDraft, codeDraft;
  int outputMode, sessionHits;
  LinearLayout outputChoice;
  TextView findText;
  volatile boolean discovering;
  long lastDiscovery = -60000, lastConnected;
  android.net.ConnectivityManager.NetworkCallback network;
  int[] inputs = {2, 6, 4, 1, 3, 0, 5, 7};
  final long[] lastHit = new long[8];
  long lastStrong = -1000;
  int strongPeak, selected, learn = -1, candidate = -1, confirmations, importPart;
  final ArrayList<int[]> undo = new ArrayList<>();
  final ArrayList<Integer> undoParts = new ArrayList<>();
  boolean learningAll,
      swap,
      closed,
      kickArmed,
      pedalInitialized,
      closeHit = true,
      pedalOnly,
      drumsOnly;
  int kickRaw, hatRaw, kickRest, kickDown = 1023, hatRest, hatDown = 1023;
  long pedalTime;
  float previousKick, previousHat, kickSpeed, hatSpeed;
  long pedalSeen;
  int guard = 70;
  String usbStatus = "Looking for Nanos", drumDevice = "waiting", pedalDevice = "waiting";
  AudioManager manager;
  AudioFocusRequest focus;
  boolean audioFocused;

  @Override
  public void onCreate(Bundle b) {
    super.onCreate(b);
    setVolumeControlStream(AudioManager.STREAM_MUSIC);
    getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
    getWindow().setBackgroundDrawable(new ColorDrawable(Palette.BG));
    getWindow().setStatusBarColor(Palette.BG);
    getWindow().setNavigationBarColor(Palette.BG);
    prefs = getSharedPreferences("pulse", 0);
    relay = new PcRelay();
    try {
      audio = new DrumAudio(this);
    } catch (IOException e) {
      new AlertDialog.Builder(this)
          .setMessage("Audio initialization failed: " + e.getMessage())
          .show();
    }
    for (int i = 0; i < 8; i++) {
      inputs[i] = prefs.getInt("input" + i, inputs[i]);
      if (audio != null) {
        audio.gains[i] = prefs.getFloat("gain" + i, 1);
        File f = new File(getFilesDir(), "custom-" + i + ".wav");
        if (f.exists()) audio.load(i, f);
      }
    }
    File open = new File(getFilesDir(), "custom-8.wav");
    if (audio != null && open.exists()) audio.load(8, open);
    swap = prefs.getBoolean("swap", false);
    kickRest = prefs.getInt("kr", 0);
    kickDown = prefs.getInt("kd", 1023);
    hatRest = prefs.getInt("hr", 0);
    hatDown = prefs.getInt("hd", 1023);
    guard = prefs.getInt("guard", 70);
    closeHit = prefs.getBoolean("closeHit", true);
    pedalOnly = prefs.getBoolean("pedalOnly", false);
    drumsOnly = prefs.getBoolean("drumsOnly", false);
    outputMode = prefs.getInt("mode", 0);
    hostDraft = prefs.getString("host", "");
    codeDraft = prefs.getString("code", "");
    Palette.theme = themeIndex();
    if (audio != null) audio.volume = prefs.getFloat("volume", .8f);
    manager = (AudioManager) getSystemService(AUDIO_SERVICE);
    focus =
        new AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN)
            .setAudioAttributes(
                new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_GAME).build())
            .setOnAudioFocusChangeListener(
                change -> {
                  audioFocused = change == AudioManager.AUDIOFOCUS_GAIN;
                  if (audio != null) {
                    audio.paused = !audioFocused;
                  }
                })
            .build();
    audioFocused = manager.requestAudioFocus(focus) == AudioManager.AUDIOFOCUS_REQUEST_GRANTED;
    build();
    configureRelay();
    if (prefs.getString("host", "").isEmpty()) discover(false);
    watchNetwork();
    usb =
        new UsbHub(
            this,
            new UsbHub.Listener() {
              public SerialProtocol.Sink sink(String device) {
                return new SerialProtocol.Sink() {
                  final long[] rawSent = new long[8];

                  public void hit(int input, int velocity, int peak) {
                    relay.send("HIT," + input + "," + velocity + "," + peak);
                    ui.post(
                        () -> {
                          drumDevice = device;
                          inputHit(input, velocity, peak);
                        });
                  }

                  public void raw(int input, int peak) {
                    long now = SystemClock.elapsedRealtime();
                    if (now - rawSent[input] >= 50) {
                      rawSent[input] = now;
                      relay.send("RAW," + input + "," + peak);
                    }
                  }

                  public void pedals(long time, int kick, int hat) {
                    relay.send("PEDALS," + time + "," + kick + "," + hat);
                    ui.post(
                        () -> {
                          pedalDevice = device;
                          pedals(time, kick, hat);
                        });
                  }
                };
              }

              public void status(String s) {
                ui.post(() -> usbStatus = s);
              }
            });
    ui.post(tick);
  }

  /** Theme index, migrating the 1.0 preference that stored the accent colour itself. */
  int themeIndex() {
    if (prefs.contains("themeIndex")) return prefs.getInt("themeIndex", 0);
    int old = prefs.getInt("theme", 0xff73f59d);
    return old == 0xffff7373 ? 1 : old == 0xff73b5ff ? 2 : 0;
  }

  void applyTheme(int index) {
    if (index == Palette.theme) return;
    Palette.theme = index;
    prefs.edit().putInt("themeIndex", index).apply();
    build();
  }

  int dp(float value) {
    return Math.round(value * getResources().getDisplayMetrics().density);
  }

  // ---------------------------------------------------------------- layout

  void build() {
    if (host != null) {
      hostDraft = host.getText().toString();
      codeDraft = code.getText().toString();
    }
    Ui u = new Ui(this);
    LinearLayout screen = u.column();
    screen.setBackgroundColor(Palette.BG);
    int side = dp(14);
    screen.setOnApplyWindowInsetsListener(
        (v, insets) -> {
          int l, t, r, b;
          if (Build.VERSION.SDK_INT >= 30) {
            android.graphics.Insets bars =
                insets.getInsets(
                    WindowInsets.Type.systemBars()
                        | WindowInsets.Type.displayCutout()
                        | WindowInsets.Type.ime());
            l = bars.left;
            t = bars.top;
            r = bars.right;
            b = bars.bottom;
          } else {
            l = insets.getSystemWindowInsetLeft();
            t = insets.getSystemWindowInsetTop();
            r = insets.getSystemWindowInsetRight();
            b = insets.getSystemWindowInsetBottom();
          }
          v.setPadding(l + side, t + dp(4), r + side, b + dp(10));
          return insets;
        });
    screen.addView(header(u), new LinearLayout.LayoutParams(-1, dp(52)));

    SplitLayout split = new SplitLayout(this, dp(12), dp(300));
    split.addView(stage(u));
    ScrollView scroll = new ScrollView(this);
    scroll.setVerticalScrollBarEnabled(false);
    scroll.setOverScrollMode(View.OVER_SCROLL_NEVER);
    scroll.setFillViewport(true);
    LinearLayout panel = u.column();
    panel.setPadding(0, 0, 0, dp(8));
    scroll.addView(panel);
    split.addView(scroll);
    screen.addView(split, new LinearLayout.LayoutParams(-1, 0, 1));

    learnCard = learnCard(u);
    learnCard.setVisibility(View.GONE);
    panel.addView(learnCard);
    u.add(panel, padCard(u), 0);
    u.add(panel, outputCard(u), 12);
    u.add(panel, setupCard(u), 12);
    u.add(panel, pedalCard(u), 12);
    u.add(panel, deviceCard(u), 12);
    ((LinearLayout.LayoutParams) panel.getChildAt(1).getLayoutParams()).topMargin = 0;
    setContentView(screen);
    screen.requestApplyInsets();
    select();
    showLearn();
    refreshStatus();
  }

  View header(Ui u) {
    LinearLayout h = u.row();
    h.addView(u.logo(), new LinearLayout.LayoutParams(dp(30), dp(30)));
    TextView word = u.text("pulse", 23, Palette.TEXT);
    word.setTypeface(Typeface.DEFAULT_BOLD);
    word.setLetterSpacing(-.03f);
    word.setPadding(dp(9), 0, 0, dp(2));
    h.addView(word);
    TextView tag = u.caption("Mobile");
    tag.setPadding(dp(12), dp(4), 0, 0);
    h.addView(tag);
    h.addView(u.spacer());
    for (int i = 0; i < 3; i++) {
      int index = i;
      h.addView(
          u.swatch(
              Palette.SWATCHES[i],
              i == Palette.theme,
              Palette.NAMES[i] + " theme",
              () -> applyTheme(index)),
          new LinearLayout.LayoutParams(dp(40), dp(44)));
    }
    return h;
  }

  View stage(Ui u) {
    final int strip = dp(30);
    class Stage extends LinearLayout implements SplitLayout.Stage {
      Stage() {
        super(MainActivity.this);
      }

      public int headerHeight() {
        return strip;
      }
    }
    Stage s = new Stage();
    s.setOrientation(LinearLayout.VERTICAL);
    LinearLayout top = u.row();
    top.setPadding(dp(4), 0, dp(4), 0);
    top.addView(u.caption("Live kit"));
    liveText = u.text("", 11, Palette.c(Palette.ACCENT));
    liveText.setSingleLine();
    liveText.setEllipsize(TextUtils.TruncateAt.END);
    liveText.setPadding(dp(10), 0, dp(10), 0);
    top.addView(liveText, new LinearLayout.LayoutParams(0, -2, 1));
    top.addView(u.caption("Hits"));
    hitsText = u.text(String.valueOf(sessionHits), 13, Palette.TEXT);
    hitsText.setTypeface(Typeface.MONOSPACE);
    hitsText.setPadding(dp(8), 0, 0, 0);
    top.addView(hitsText);
    s.addView(top, new LinearLayout.LayoutParams(-1, strip));
    kit =
        new KitView(
            this,
            p -> {
              selected = p;
              select();
              play(p, 110);
              relay.send("TOUCH," + p + ",110");
            });
    kit.selected = selected;
    s.addView(kit, new LinearLayout.LayoutParams(-1, 0, 1));
    return s;
  }

  LinearLayout learnCard(Ui u) {
    LinearLayout c = u.card();
    c.setBackground(u.shape(Palette.c(0xff1b2a18), Palette.c(0xff4d6b2f), 12));
    learnTitle = u.text("", 18, Palette.TEXT);
    learnTitle.setTypeface(Typeface.DEFAULT_BOLD);
    c.addView(learnTitle);
    learnText = u.text("", 13, Palette.SOFT_TEXT);
    u.add(c, learnText, 6);
    u.add(
        c,
        u.buttons(
            u.button("Undo last part", this::undoLearn),
            u.button(
                "Cancel setup",
                () -> {
                  learn = -1;
                  showLearn();
                })),
        12);
    LinearLayout wrap = u.column();
    wrap.addView(c);
    wrap.setPadding(0, 0, 0, dp(12));
    return wrap;
  }

  View padCard(Ui u) {
    LinearLayout c = u.card();
    LinearLayout head = u.row();
    head.addView(u.caption("Pad settings"), new LinearLayout.LayoutParams(0, -2, 1));
    head.addView(u.small("Assign input", () -> startLearn(false)));
    c.addView(head);
    LinearLayout name = u.row();
    padName = u.text("", 28, Palette.TEXT);
    padName.setTypeface(Typeface.create("sans-serif-light", Typeface.NORMAL));
    name.addView(padName, new LinearLayout.LayoutParams(0, -2, 1));
    padInput = u.text("", 13, Palette.MUTED);
    name.addView(padInput);
    u.add(c, name, 8);
    gainRow =
        u.new SliderRow(
            "Sample gain",
            36,
            24,
            p -> {
              if (audio != null) audio.gains[selected] = (float) Math.pow(10, (p - 24) / 20.0);
              gainRow.value.setText(String.format(Locale.ROOT, "%+.1f dB", (float) (p - 24)));
            },
            () ->
                prefs
                    .edit()
                    .putFloat("gain" + selected, audio == null ? 1 : audio.gains[selected])
                    .apply());
    u.add(c, gainRow, 14);

    u.add(c, u.caption("Sound"), 8);
    soundName = u.text("", 14, 0xffeaf0e4);
    soundName.setGravity(Gravity.CENTER_VERTICAL);
    soundName.setPadding(dp(12), 0, dp(12), 0);
    soundName.setBackground(u.shape(Palette.FIELD, Palette.FIELD_LINE, 6));
    u.add(c, soundName, 8);
    soundName.getLayoutParams().height = dp(40);
    u.add(
        c,
        u.buttons(
            u.button("Load WAV…", () -> importWave(selected)),
            u.button("Use synth", this::useSynth)),
        8);
    openHat = u.button("Load open hi-hat WAV…", () -> importWave(8));
    u.add(c, openHat, 8);

    hitRow =
        u.new SliderRow(
            "Trigger threshold",
            1000,
            0,
            p -> {
              int hit = curve(p, 2, 1022);
              hitRow.value.setText(String.valueOf(hit));
              int reset = currentReset();
              if (reset >= hit) {
                resetRow.slider.set(uncurve(hit - 1, 1, Math.max(2, hit - 1)));
                resetRow.value.setText(String.valueOf(hit - 1));
              }
            },
            this::saveThresholds);
    u.add(c, hitRow, 14);
    resetRow =
        u.new SliderRow(
            "Re-arm threshold",
            1000,
            0,
            p -> resetRow.value.setText(String.valueOf(currentReset())),
            this::saveThresholds);
    u.add(c, resetRow, 4);
    thresholdNote = u.muted("");
    u.add(c, thresholdNote, 2);

    Button audition =
        u.primary(
            "▶  Audition",
            () -> {
              play(selected, 110);
              relay.send("TOUCH," + selected + ",110");
            });
    Button reset = u.button("Reset", this::resetPad);
    LinearLayout actions = u.row();
    actions.addView(audition, new LinearLayout.LayoutParams(0, -2, 2.2f));
    LinearLayout.LayoutParams rp = new LinearLayout.LayoutParams(0, -2, 1);
    rp.leftMargin = dp(8);
    actions.addView(reset, rp);
    u.add(c, actions, 14);
    return c;
  }

  View outputCard(Ui u) {
    LinearLayout c = u.card();
    LinearLayout head = u.row();
    head.addView(u.caption("Output"), new LinearLayout.LayoutParams(0, -2, 1));
    head.addView(u.muted("Stereo sample engine"));
    c.addView(head);
    outputChoice =
        u.segmented(
            OUTPUTS,
            outputMode,
            n -> {
              outputMode = n;
              configureRelay();
              if (n != 0 && relay.host.isEmpty()) discover(true);
              refreshStatus();
            });
    u.add(c, outputChoice, 10);
    int level = Math.round(prefs.getFloat("volume", .8f) * 100);
    volumeRow =
        u.new SliderRow(
            "Master volume",
            100,
            level,
            p -> {
              if (audio != null) audio.volume = p / 100f;
              volumeRow.value.setText(p + "%");
            },
            () -> prefs.edit().putFloat("volume", volumeRow.slider.progress / 100f).apply());
    volumeRow.value.setText(level + "%");
    u.add(c, volumeRow, 14);

    u.add(c, u.caption("Pulse PC over Wi-Fi"), 14);
    u.add(c, u.primary("Find Pulse PC automatically", () -> discover(true)), 8);
    findText =
        u.muted(
            "On the PC, switch on Phone input. For two minutes it lets this phone set itself up"
                + " — no typing needed.");
    u.add(c, findText, 6);
    u.add(c, u.caption("Or enter manually"), 14);
    host =
        u.field(
            "PC IPv4 address shown in Pulse",
            hostDraft,
            InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
    code = u.field("Pairing code", codeDraft, InputType.TYPE_CLASS_NUMBER);
    LinearLayout fields = u.row();
    fields.addView(host, new LinearLayout.LayoutParams(0, -2, 1.6f));
    LinearLayout.LayoutParams cp = new LinearLayout.LayoutParams(0, -2, 1);
    cp.leftMargin = dp(8);
    fields.addView(code, cp);
    u.add(c, fields, 8);
    u.add(c, u.button("Save Wi-Fi connection", this::saveRelay), 8);
    relayText = u.muted("");
    u.add(c, relayText, 8);
    return c;
  }

  View setupCard(Ui u) {
    LinearLayout c = u.card();
    c.addView(u.caption("Kit setup"));
    u.add(
        c,
        u.buttons(
            u.button("Set up my kit", () -> startLearn(true)),
            u.button("Assign selected pad", () -> startLearn(false))),
        10);
    setupText =
        u.muted(
            "Strike each requested pad twice, at least 350 ms apart. The next part follows"
                + " automatically.");
    u.add(c, setupText, 8);
    u.add(c, u.text("Repeat-hit guard", 13, Palette.SOFT_TEXT), 14);
    int current = 1;
    for (int i = 0; i < GUARDS.length; i++) if (GUARDS[i] == guard) current = i;
    u.add(
        c,
        u.segmented(
            new String[] {"High · 110 ms", "Medium · 70", "Low · 35"},
            current,
            n -> {
              guard = GUARDS[n];
              prefs.edit().putInt("guard", guard).apply();
            }),
        8);
    return c;
  }

  View pedalCard(Ui u) {
    LinearLayout c = u.card();
    c.addView(u.caption("Pedals"));
    pedalText = u.text("", 13, Palette.SOFT_TEXT);
    u.add(c, pedalText, 8);
    kickMeter = u.new Meter();
    hatMeter = u.new Meter();
    u.add(c, meterRow(u, "Kick", kickMeter), 6);
    u.add(c, meterRow(u, "Hi-hat", hatMeter), 2);
    u.add(
        c,
        u.toggle(
            "Swap kick / hi-hat inputs",
            swap,
            b -> {
              swap = b;
              int r = kickRest, d = kickDown;
              kickRest = hatRest;
              kickDown = hatDown;
              hatRest = r;
              hatDown = d;
              pedalInitialized = false;
              savePedals();
            }),
        4);
    u.add(
        c,
        u.toggle(
            "Play hi-hat on fast pedal close",
            closeHit,
            b -> {
              closeHit = b;
              savePedals();
            }),
        0);
    u.add(c, u.text("Kick source", 13, Palette.SOFT_TEXT), 8);
    u.add(
        c,
        u.segmented(
            new String[] {"Drums + pedal", "Pedal only", "Drums only"},
            pedalOnly ? 1 : drumsOnly ? 2 : 0,
            n -> {
              pedalOnly = n == 1;
              drumsOnly = n == 2;
              savePedals();
            }),
        8);
    u.add(
        c,
        u.buttons(
            u.button(
                "Set resting",
                () -> {
                  kickRest = kickRaw;
                  hatRest = hatRaw;
                  pedalInitialized = false;
                  savePedals();
                  toast("Resting position saved");
                }),
            u.button(
                "Set pressed",
                () -> {
                  kickDown = kickRaw;
                  hatDown = hatRaw;
                  pedalInitialized = false;
                  savePedals();
                  toast("Pressed position saved");
                })),
        12);
    return c;
  }

  View meterRow(Ui u, String label, Ui.Meter meter) {
    LinearLayout r = u.row();
    r.addView(u.muted(label), new LinearLayout.LayoutParams(dp(54), -2));
    r.addView(meter, new LinearLayout.LayoutParams(0, dp(16), 1));
    return r;
  }

  View deviceCard(Ui u) {
    LinearLayout c = u.card();
    c.addView(u.caption("Sounds & USB"));
    u.add(c, u.button("Download V05 acoustic kit", this::downloadKit), 10);
    u.add(
        c,
        u.button(
            "Retry USB permissions",
            () -> {
              if (usb != null) usb.retry();
            }),
        8);
    usbText = u.text("", 13, Palette.SOFT_TEXT);
    u.add(c, usbText, 10);
    u.add(
        c,
        u.muted(
            "Keep Pulse Mobile open while playing. Use a powered OTG hub if the phone cannot power"
                + " both Nanos. Wired headphones, USB audio or the phone speaker work best;"
                + " Bluetooth adds delay. PC mode uses the PC's mappings, pedal settings, samples"
                + " and MIDI output."),
        10);
    return c;
  }

  // ---------------------------------------------------------------- state

  static int curve(int position, int low, int high) {
    double t = position / 1000.0;
    return low + (int) Math.round((high - low) * t * t);
  }

  static int uncurve(int value, int low, int high) {
    if (high <= low) return 0;
    double t = Math.max(0, Math.min(1, (value - low) / (double) (high - low)));
    return (int) Math.round(Math.sqrt(t) * 1000);
  }

  int currentHit() {
    return curve(hitRow.slider.progress, 2, 1022);
  }

  int currentReset() {
    int hit = currentHit();
    return Math.min(hit - 1, curve(resetRow.slider.progress, 1, Math.max(2, hit - 1)));
  }

  void saveThresholds() {
    int input = inputs[selected];
    int hit = currentHit(), reset = Math.max(1, currentReset());
    prefs.edit().putInt("threshold" + input, hit).putInt("reset" + input, reset).apply();
    if (usb != null) usb.applyThresholds();
    thresholdNote.setText("A" + input + " saved · sent to drum Nano when connected");
  }

  void loadThresholds() {
    int input = inputs[selected];
    int hit = prefs.getInt("threshold" + input, DEFAULT_HITS[input]);
    int reset = prefs.getInt("reset" + input, DEFAULT_RESETS[input]);
    hitRow.slider.set(uncurve(hit, 2, 1022));
    hitRow.value.setText(String.valueOf(hit));
    resetRow.slider.set(uncurve(reset, 1, Math.max(2, hit - 1)));
    resetRow.value.setText(String.valueOf(reset));
    thresholdNote.setText("Physical input A" + input + " · hit 2–1022, re-arm below hit");
  }

  void resetPad() {
    int input = inputs[selected];
    prefs
        .edit()
        .remove("threshold" + input)
        .remove("reset" + input)
        .putFloat("gain" + selected, 1)
        .apply();
    if (audio != null) audio.gains[selected] = 1;
    if (usb != null) usb.applyThresholds();
    select();
    toast(KitView.NAMES[selected] + " gain and thresholds reset");
  }

  void useSynth() {
    if (audio != null) {
      audio.load(selected, new File(getFilesDir(), "synth-" + selected + ".wav"));
      new File(getFilesDir(), "custom-" + selected + ".wav").delete();
      if (selected == 0) {
        audio.load(8, new File(getFilesDir(), "synth-8.wav"));
        new File(getFilesDir(), "custom-8.wav").delete();
      }
    }
    select();
  }

  void saveRelay() {
    configureRelay();
    View focus = getCurrentFocus();
    if (focus != null) {
      focus.clearFocus();
      android.view.inputmethod.InputMethodManager m =
          (android.view.inputmethod.InputMethodManager) getSystemService(INPUT_METHOD_SERVICE);
      if (m != null) m.hideSoftInputFromWindow(focus.getWindowToken(), 0);
    }
    toast(outputMode == 0 ? "Saved. Choose Pulse PC or Both to send hits." : "Wi-Fi connection saved");
  }

  void configureRelay() {
    if (host == null || code == null) return;
    String h = host.getText().toString().trim(), c = code.getText().toString().trim();
    prefs.edit().putString("host", h).putString("code", c).putInt("mode", outputMode).apply();
    if (h.equals(relay.host) && c.equals(relay.code) && (outputMode != 0) == relay.enabled) return;
    relay.configure(h, c, outputMode != 0);
  }

  /** Broadcasts for Pulse PCs and fills address (and code, while the PC's pairing is open). */
  void discover(boolean manual) {
    if (discovering) return;
    discovering = true;
    lastDiscovery = SystemClock.elapsedRealtime();
    if (manual && findText != null) findText.setText("Searching the local network…");
    String savedName = prefs.getString("pcName", "");
    new Thread(
            () -> {
              List<PcDiscovery.Found> all = PcDiscovery.find(1600);
              ui.post(
                  () -> {
                    discovering = false;
                    if (!isFinishing()) found(all, savedName, manual);
                  });
            },
            "Pulse discovery")
        .start();
  }

  void found(List<PcDiscovery.Found> all, String savedName, boolean manual) {
    PcDiscovery.Found pc = PcDiscovery.choose(all, savedName);
    if (pc == null) {
      if (manual)
        findText.setText(
            all.isEmpty()
                ? "No Pulse PC answered. Check both are on the same Wi-Fi, Phone input is on, and"
                    + " “Allow phone Wi-Fi” was accepted on the PC."
                : all.size() + " Pulse PCs found. Turn Phone input off and on at the one to use.");
      return;
    }
    // A PC without pairing open only refreshes the address of the one already paired.
    if (pc.code.isEmpty() && !pc.name.equals(savedName) && code.getText().length() == 0) {
      host.setText(pc.address);
      findText.setText(
          "Found " + pc.name + ". Enter its code, or turn Phone input off and on at the PC.");
      return;
    }
    host.setText(pc.address);
    if (!pc.code.isEmpty()) code.setText(pc.code);
    prefs.edit().putString("pcName", pc.name).apply();
    if (manual && outputMode == 0) {
      outputMode = 1;
      for (int i = 0; i < outputChoice.getChildCount(); i++)
        new Ui(this).mark((Button) outputChoice.getChildAt(i), i == outputMode);
    }
    configureRelay();
    findText.setText("Paired with " + pc.name + " · " + pc.address);
    refreshStatus();
    if (manual) toast("Connected to " + pc.name);
  }

  void select() {
    if (kit == null) return;
    kit.selected = selected;
    kit.invalidate();
    padName.setText(KitView.NAMES[selected]);
    padInput.setText("Input A" + inputs[selected]);
    int gain =
        audio == null ? 24 : (int) Math.round(20 * Math.log10(audio.gains[selected])) + 24;
    gainRow.slider.set(gain);
    gainRow.value.setText(String.format(Locale.ROOT, "%+.1f dB", (float) (gainRow.slider.progress - 24)));
    boolean custom = new File(getFilesDir(), "custom-" + selected + ".wav").exists();
    soundName.setText(custom ? "Custom WAV" : "Pulse synth");
    openHat.setVisibility(selected == 0 ? View.VISIBLE : View.GONE);
    if (selected == 0) {
      boolean openCustom = new File(getFilesDir(), "custom-8.wav").exists();
      openHat.setText(openCustom ? "Open hi-hat: custom WAV · replace…" : "Load open hi-hat WAV…");
    }
    loadThresholds();
  }

  void play(int part, int velocity) {
    kit.flash(part, velocity);
    sessionHits++;
    if (hitsText != null) hitsText.setText(String.valueOf(sessionHits));
    if (audio != null && audioFocused && outputMode != 1)
      audio.hit(
          part, velocity, part == 0 && SystemClock.elapsedRealtime() - pedalSeen < 500 && !closed);
  }

  void inputHit(int input, int velocity, int peak) {
    long now = SystemClock.elapsedRealtime();
    int part = -1;
    for (int i = 0; i < 8; i++) if (inputs[i] == input) part = i;
    if (part < 0) return;
    if (learn >= 0) {
      if (now - lastHit[input] < 350) return;
      lastHit[input] = now;
      if (candidate != input) {
        candidate = input;
        confirmations = 1;
      } else confirmations++;
      if (confirmations >= 2) {
        undo.add(inputs.clone());
        undoParts.add(learn);
        inputs = SerialProtocol.assign(inputs, learn, input);
        saveMap();
        learn = learningAll && learn < 7 ? learn + 1 : -1;
        candidate = -1;
        confirmations = 0;
      }
      showLearn();
      return;
    }
    if (now - lastHit[input] < guard) return;
    if (now - lastStrong < 14 && peak < strongPeak * .45) return;
    lastHit[input] = now;
    lastStrong = now;
    strongPeak = peak;
    if (part == 5 && pedalOnly) return;
    play(part, Math.max(50, Math.min(127, (int) (Math.pow(velocity / 127.0, .6) * 127))));
  }

  void startLearn(boolean all) {
    learningAll = all;
    learn = all ? 0 : selected;
    undo.clear();
    undoParts.clear();
    candidate = -1;
    confirmations = 0;
    showLearn();
  }

  void undoLearn() {
    if (undo.isEmpty()) {
      toast("Nothing to undo yet");
      return;
    }
    inputs = undo.remove(undo.size() - 1);
    learn = undoParts.remove(undoParts.size() - 1);
    candidate = -1;
    confirmations = 0;
    saveMap();
    showLearn();
  }

  void showLearn() {
    if (kit == null) return;
    kit.learning = learn;
    if (learn < 0) {
      learnCard.setVisibility(View.GONE);
    } else {
      learnCard.setVisibility(View.VISIBLE);
      String name = KitView.NAMES[learn];
      learnTitle.setText("Assign · " + name);
      learnText.setText(
          (learningAll ? "Part " + (learn + 1) + " of 8 · " : "")
              + confirmations
              + " of 2 heard. Strike "
              + name
              + (confirmations == 0 ? " twice." : " once more to confirm."));
      selected = learn;
      select();
    }
    kit.invalidate();
  }

  void saveMap() {
    SharedPreferences.Editor e = prefs.edit();
    for (int i = 0; i < 8; i++) e.putInt("input" + i, inputs[i]);
    e.apply();
    select();
  }

  void savePedals() {
    prefs
        .edit()
        .putBoolean("swap", swap)
        .putBoolean("closeHit", closeHit)
        .putBoolean("pedalOnly", pedalOnly)
        .putBoolean("drumsOnly", drumsOnly)
        .putInt("kr", kickRest)
        .putInt("kd", kickDown)
        .putInt("hr", hatRest)
        .putInt("hd", hatDown)
        .apply();
  }

  void pedals(long time, int a, int b) {
    pedalSeen = SystemClock.elapsedRealtime();
    kickRaw = swap ? b : a;
    hatRaw = swap ? a : b;
    float k = SerialProtocol.position(kickRaw, kickRest, kickDown),
        h = SerialProtocol.position(hatRaw, hatRest, hatDown);
    if (kickMeter != null) {
      kickMeter.set(k);
      hatMeter.set(h);
    }
    long dt = (time - pedalTime) & 0xffffffffL;
    pedalTime = time;
    if (!pedalInitialized || dt > 100) {
      pedalInitialized = true;
      closed = h >= .75;
      kickArmed = k <= .25;
      kickSpeed = hatSpeed = 0;
      previousKick = k;
      previousHat = h;
      return;
    }
    if (dt == 0) return;
    kickSpeed = Math.max(kickSpeed * (float) Math.exp(-dt / 80.0), (k - previousKick) * 1000 / dt);
    hatSpeed = Math.max(hatSpeed * (float) Math.exp(-dt / 80.0), (h - previousHat) * 1000 / dt);
    if (k <= .25) {
      kickArmed = true;
      kickSpeed = 0;
    }
    if (kickArmed && k >= .75) {
      kickArmed = false;
      if (!drumsOnly && learn < 0) play(5, Math.max(1, Math.min(127, (int) (kickSpeed * 16))));
    }
    if (closed && h <= .60) {
      closed = false;
      hatSpeed = 0;
    }
    if (!closed && h >= .75) {
      closed = true;
      if (audio != null) audio.choke();
      int v = Math.min(127, (int) (hatSpeed * 16));
      if (closeHit && v >= 50 && learn < 0) play(0, v);
      hatSpeed = 0;
    }
    previousKick = k;
    previousHat = h;
  }

  void refreshStatus() {
    if (liveText == null) return;
    boolean drums = !"waiting".equals(drumDevice);
    String target = outputMode == 0 ? "Phone audio" : outputMode == 1 ? "Pulse PC" : "Phone + PC";
    liveText.setText((drums ? "Drums connected" : "Waiting for drum Nano") + " · " + target);
    usbText.setText(usbStatus + "\nDrums: " + drumDevice + " · Pedals: " + pedalDevice);
    relayText.setText(outputMode == 0 ? "PC relay off · phone output only" : relay.status);
    long now = SystemClock.elapsedRealtime();
    if (relay.connected()) lastConnected = now;
    // PC address changed (DHCP) or PC restarted: look again quietly, at most every 15 s.
    else if (outputMode != 0 && now - lastConnected > 4000 && now - lastDiscovery > 15000)
      discover(false);
    boolean live = SystemClock.elapsedRealtime() - pedalSeen < 500;
    pedalText.setText(
        live
            ? "Kick " + kickRaw + " · Hi-hat " + hatRaw + " · " + (closed ? "Closed" : "Open")
            : "Waiting for pedal Nano");
  }

  final Runnable tick =
      new Runnable() {
        public void run() {
          refreshStatus();
          ui.postDelayed(this, 250);
        }
      };

  void importWave(int part) {
    importPart = part;
    Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT);
    i.setType("audio/*");
    i.addCategory(Intent.CATEGORY_OPENABLE);
    startActivityForResult(i, 10);
  }

  @Override
  protected void onActivityResult(int request, int result, Intent data) {
    super.onActivityResult(request, result, data);
    if (request != 10 || result != RESULT_OK || data == null) return;
    int part = importPart;
    new Thread(
            () -> {
              File temp = new File(getFilesDir(), "incoming.wav");
              try (InputStream in = getContentResolver().openInputStream(data.getData());
                  OutputStream out = new FileOutputStream(temp)) {
                if (in == null) throw new IOException("Cannot read file");
                byte[] b = new byte[8192];
                int n, total = 0;
                while ((n = in.read(b)) != -1) {
                  total += n;
                  if (total > 16 * 1024 * 1024)
                    throw new IOException("Use a WAV under 16 MB and 12 seconds");
                  out.write(b, 0, n);
                }
                out.flush();
                try (RandomAccessFile f = new RandomAccessFile(temp, "r")) {
                  if (f.readInt() != 0x52494646) {
                    throw new IOException("Choose a PCM WAV file");
                  }
                  f.seek(8);
                  if (f.readInt() != 0x57415645) throw new IOException("Not a WAV file");
                }
                WaveData.read(temp);
                File dest = new File(getFilesDir(), "custom-" + part + ".wav");
                try (InputStream copy = new FileInputStream(temp);
                    OutputStream target = new FileOutputStream(dest)) {
                  while ((n = copy.read(b)) != -1) target.write(b, 0, n);
                }
                ui.post(
                    () -> {
                      if (audio != null && audio.load(part, dest)) toast("Sample loaded");
                      else toast("Sample could not load");
                      select();
                    });
              } catch (Exception e) {
                ui.post(() -> toast(e.getMessage()));
              } finally {
                temp.delete();
              }
            },
            "Sample import")
        .start();
  }

  boolean downloading;

  void downloadKit() {
    if (downloading) {
      toast("Download already running");
      return;
    }
    String license;
    try (InputStream in = getAssets().open("GSCW-LICENSE.txt")) {
      ByteArrayOutputStream out = new ByteArrayOutputStream();
      byte[] b = new byte[4096];
      int n;
      while ((n = in.read(b)) > 0) out.write(b, 0, n);
      license = out.toString("UTF-8");
    } catch (IOException e) {
      toast(e.getMessage());
      return;
    }
    new AlertDialog.Builder(this)
        .setTitle("GSCW V05 acoustic kit")
        .setMessage(
            "Downloads nine original samples directly from the sample repository. Replaces phone"
                + " samples.\n\n"
                + license)
        .setNegativeButton("Cancel", null)
        .setPositiveButton(
            "Accept and download",
            (d, w) -> {
              downloading = true;
              new Thread(
                      () -> {
                        try {
                          for (int i = 0; i < 9; i++) {
                            int part = i;
                            ui.post(() -> toast("Downloading V05 " + (part + 1) + " / 9"));
                            File temp = AcousticKit.download(getFilesDir(), part),
                                dest = new File(getFilesDir(), "custom-" + part + ".wav");
                            java.nio.file.Files.move(
                                temp.toPath(),
                                dest.toPath(),
                                java.nio.file.StandardCopyOption.REPLACE_EXISTING);
                            if (audio != null && !audio.load(part, dest))
                              throw new IOException("Could not load sample " + (part + 1));
                          }
                          ui.post(
                              () -> {
                                toast("V05 kit ready. Samples stay available offline.");
                                select();
                              });
                        } catch (Exception e) {
                          ui.post(
                              () ->
                                  toast(
                                      "Download stopped: "
                                          + e.getMessage()
                                          + ". Retry to finish the kit."));
                        } finally {
                          downloading = false;
                        }
                      },
                      "V05 download")
                  .start();
            })
        .show();
  }

  void toast(String s) {
    Toast.makeText(this, s, Toast.LENGTH_LONG).show();
  }

  @Override
  protected void onNewIntent(Intent intent) {
    super.onNewIntent(intent);
    // Launched by plugging in a Nano: open it now instead of waiting for the next scan.
    if (usb != null) usb.scan();
  }

  @Override
  protected void onResume() {
    super.onResume();
    if (usb != null) usb.scan();
  }

  /** Re-runs discovery whenever Wi-Fi (re)connects. */
  void watchNetwork() {
    android.net.ConnectivityManager cm =
        (android.net.ConnectivityManager) getSystemService(CONNECTIVITY_SERVICE);
    if (cm == null) return;
    network =
        new android.net.ConnectivityManager.NetworkCallback() {
          @Override
          public void onAvailable(android.net.Network n) {
            ui.postDelayed(() -> discover(false), 800);
          }
        };
    try {
      cm.registerDefaultNetworkCallback(network);
    } catch (RuntimeException e) {
      network = null;
    }
  }

  @Override
  protected void onDestroy() {
    if (network != null) {
      android.net.ConnectivityManager cm =
          (android.net.ConnectivityManager) getSystemService(CONNECTIVITY_SERVICE);
      try {
        cm.unregisterNetworkCallback(network);
      } catch (RuntimeException ignored) {
      }
    }
    ui.removeCallbacksAndMessages(null);
    if (usb != null) usb.close();
    if (relay != null) relay.close();
    if (audio != null) audio.close();
    if (manager != null) manager.abandonAudioFocusRequest(focus);
    super.onDestroy();
  }
}
