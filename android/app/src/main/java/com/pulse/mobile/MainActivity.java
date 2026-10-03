package com.pulse.mobile;

import android.app.*;
import android.content.*;
import android.graphics.Color;
import android.media.*;
import android.os.*;
import android.view.*;
import android.widget.*;
import java.io.*;
import java.util.*;

public final class MainActivity extends Activity {
  final Handler ui = new Handler(Looper.getMainLooper());
  SharedPreferences prefs;
  DrumAudio audio;
  UsbHub usb;
  PcRelay relay;
  KitView kit;
  TextView status, selection, learnText, pedalText;
  EditText host, code;
  Spinner mode;
  LinearLayout root;
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

  int dp(float value) {
    return Math.round(value * getResources().getDisplayMetrics().density);
  }

  TextView text(String value, int size) {
    TextView v = new TextView(this);
    v.setText(value);
    v.setTextSize(size);
    v.setTextColor(0xffe5eee8);
    v.setPadding(8, 8, 8, 8);
    return v;
  }

  Button button(String label, Runnable action) {
    Button b = new Button(this);
    b.setText(label);
    b.setTextSize(13);
    b.setTextColor(0xffdbe8df);
    b.setMinWidth(0);
    b.setMinimumWidth(0);
    b.setMinHeight(dp(44));
    b.setPadding(dp(12), dp(4), dp(12), dp(4));
    android.graphics.drawable.GradientDrawable bg =
        new android.graphics.drawable.GradientDrawable();
    bg.setColor(0xff1a261e);
    bg.setCornerRadius(dp(10));
    bg.setStroke(dp(1), 0xff314b3a);
    b.setBackground(bg);
    b.setAllCaps(false);
    b.setOnClickListener(v -> action.run());
    return b;
  }

  LinearLayout row() {
    LinearLayout r =
        new LinearLayout(this) {
          @Override
          public void addView(View child) {
            LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(0, dp(44), 1);
            p.setMargins(dp(3), dp(3), dp(3), dp(3));
            super.addView(child, p);
          }
        };
    r.setOrientation(LinearLayout.HORIZONTAL);
    return r;
  }

  void addCheck(
      LinearLayout into,
      String label,
      boolean checked,
      java.util.function.Consumer<Boolean> change) {
    CheckBox c = new CheckBox(this);
    c.setText(label);
    c.setChecked(checked);
    c.setOnCheckedChangeListener((v, b) -> change.accept(b));
    into.addView(c);
  }

  void build() {
    ScrollView scroll = new ScrollView(this);
    root =
        new LinearLayout(this) {
          @Override
          public void addView(View child) {
            if (child instanceof Button) {
              LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-1, dp(44));
              p.setMargins(0, dp(4), 0, dp(4));
              super.addView(child, p);
            } else super.addView(child);
          }
        };
    root.setOrientation(LinearLayout.VERTICAL);
    root.setPadding(dp(14), dp(12), dp(14), dp(30));
    root.setBackgroundColor(0xff0b100d);
    scroll.addView(root);
    scroll.setOnApplyWindowInsetsListener(
        (v, insets) -> {
          v.setPadding(0, insets.getSystemWindowInsetTop(), 0, insets.getSystemWindowInsetBottom());
          return insets;
        });
    setContentView(scroll);
    LinearLayout title = row();
    TextView brand = text("PULSE / MOBILE", 20);
    title.addView(brand, new LinearLayout.LayoutParams(0, -2, 1));
    for (int color : new int[] {0xff73f59d, 0xffff7373, 0xff73b5ff}) {
      Button dot =
          button(
              "●",
              () -> {
                kit.accent = color;
                prefs.edit().putInt("theme", color).apply();
                kit.invalidate();
              });
      dot.setTextColor(color);
      dot.setTextSize(22);
      dot.setPadding(0, 0, 0, 0);
      dot.setBackgroundColor(Color.TRANSPARENT);
      dot.setContentDescription(
          color == 0xff73f59d ? "Green theme" : color == 0xffff7373 ? "Red theme" : "Blue theme");
      title.addView(dot, new LinearLayout.LayoutParams(dp(34), dp(44)));
    }
    root.addView(title);
    status = text("USB OTG · Phone audio · Local Wi-Fi", 12);
    root.addView(status);
    kit =
        new KitView(
            this,
            p -> {
              selected = p;
              select();
              play(p, 110);
              relay.send("TOUCH," + p + ",110");
            });
    kit.accent = prefs.getInt("theme", 0xff73f59d);
    root.addView(kit, new LinearLayout.LayoutParams(-1, -2));
    selection = text("Hi-hat · A2", 20);
    root.addView(selection);
    LinearLayout actions = row();
    actions.addView(button("Assign pad", () -> startLearn(false)));
    actions.addView(button("Import WAV", () -> importWave(selected)));
    actions.addView(
        button(
            "Synth",
            () -> {
              if (audio != null) {
                audio.load(selected, new File(getFilesDir(), "synth-" + selected + ".wav"));
                new File(getFilesDir(), "custom-" + selected + ".wav").delete();
              }
            }));
    root.addView(actions);
    root.addView(button("Selected pad gain", () -> gainDialog()));
    root.addView(button("Trigger thresholds", this::thresholdDialog));
    root.addView(button("Import open hi-hat WAV", () -> importWave(8)));
    root.addView(button("Download V05 acoustic kit", this::downloadKit));
    root.addView(text("OUTPUT", 12));
    mode = new Spinner(this);
    mode.setAdapter(
        new ArrayAdapter<>(
            this,
            android.R.layout.simple_spinner_dropdown_item,
            new String[] {"Phone output", "Pulse PC over Wi-Fi", "Phone + Pulse PC"}));
    mode.setSelection(prefs.getInt("mode", 0));
    root.addView(mode);
    root.addView(text("Phone master volume", 14));
    SeekBar volume = new SeekBar(this);
    volume.setMax(100);
    volume.setProgress((int) (prefs.getFloat("volume", .8f) * 100));
    if (audio != null) audio.volume = volume.getProgress() / 100f;
    volume.setOnSeekBarChangeListener(
        new SeekBar.OnSeekBarChangeListener() {
          public void onProgressChanged(SeekBar s, int p, boolean u) {
            if (audio != null) audio.volume = p / 100f;
            prefs.edit().putFloat("volume", p / 100f).apply();
          }

          public void onStartTrackingTouch(SeekBar s) {}

          public void onStopTrackingTouch(SeekBar s) {}
        });
    root.addView(volume);
    host = new EditText(this);
    host.setSingleLine();
    host.setHint("PC IPv4 address shown in Pulse");
    host.setText(prefs.getString("host", ""));
    root.addView(host);
    code = new EditText(this);
    code.setSingleLine();
    code.setHint("Pairing code shown in Pulse");
    code.setText(prefs.getString("code", ""));
    root.addView(code);
    root.addView(button("Save Wi-Fi connection", this::configureRelay));
    mode.setOnItemSelectedListener(
        new android.widget.AdapterView.OnItemSelectedListener() {
          public void onItemSelected(android.widget.AdapterView<?> p, View v, int n, long id) {
            configureRelay();
          }

          public void onNothingSelected(android.widget.AdapterView<?> p) {}
        });
    root.addView(text("KIT SETUP", 12));
    LinearLayout setup = row();
    setup.addView(button("Assign all", () -> startLearn(true)));
    setup.addView(
        button(
            "Undo",
            () -> {
              if (!undo.isEmpty()) {
                inputs = undo.remove(undo.size() - 1);
                learn = undoParts.remove(undoParts.size() - 1);
                candidate = -1;
                confirmations = 0;
                saveMap();
                showLearn();
              }
            }));
    setup.addView(
        button(
            "Done / cancel",
            () -> {
              learn = -1;
              showLearn();
            }));
    root.addView(setup);
    learnText = text("Hit each requested pad twice. Advances automatically.", 14);
    root.addView(learnText);
    LinearLayout protections = row();
    for (int g : new int[] {110, 70, 35})
      protections.addView(
          button(
              g == 110 ? "Guard: high" : g == 70 ? "Medium" : "Low",
              () -> {
                guard = g;
                prefs.edit().putInt("guard", g).apply();
                toast("Repeat-hit guard: " + g + " ms");
              }));
    root.addView(protections);
    root.addView(text("PEDALS", 12));
    pedalText = text("Waiting for pedal Nano", 13);
    root.addView(pedalText);
    addCheck(
        root,
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
        });
    addCheck(
        root,
        "Play hi-hat on fast pedal close",
        closeHit,
        b -> {
          closeHit = b;
          savePedals();
        });
    Spinner kickMode = new Spinner(this);
    kickMode.setAdapter(
        new ArrayAdapter<>(
            this,
            android.R.layout.simple_spinner_dropdown_item,
            new String[] {"Kick: drums + pedal", "Kick: pedal only", "Kick: drums only"}));
    kickMode.setSelection(pedalOnly ? 1 : drumsOnly ? 2 : 0);
    kickMode.setOnItemSelectedListener(
        new android.widget.AdapterView.OnItemSelectedListener() {
          public void onItemSelected(android.widget.AdapterView<?> p, View v, int n, long id) {
            pedalOnly = n == 1;
            drumsOnly = n == 2;
            savePedals();
          }

          public void onNothingSelected(android.widget.AdapterView<?> p) {}
        });
    root.addView(kickMode);
    LinearLayout endpoints = row();
    endpoints.addView(
        button(
            "Set resting",
            () -> {
              kickRest = kickRaw;
              hatRest = hatRaw;
              pedalInitialized = false;
              savePedals();
            }));
    endpoints.addView(
        button(
            "Set pressed",
            () -> {
              kickDown = kickRaw;
              hatDown = hatRaw;
              pedalInitialized = false;
              savePedals();
            }));
    root.addView(endpoints);
    root.addView(
        button(
            "Retry USB permissions",
            () -> {
              if (usb != null) usb.retry();
            }));
    root.addView(
        text(
            "Keep Pulse Mobile open while playing. Use a powered OTG hub if the phone cannot power"
                + " both Nanos. Connect wired headphones, USB audio or the phone speaker; Bluetooth"
                + " adds delay. PC mode uses the PC's mappings, pedal settings, samples and MIDI"
                + " output. Phone controls apply to phone playback.",
            12));
    select();
  }

  void configureRelay() {
    if (host == null || code == null) return;
    int m = mode.getSelectedItemPosition();
    String h = host.getText().toString().trim(), c = code.getText().toString().trim();
    prefs.edit().putString("host", h).putString("code", c).putInt("mode", m).apply();
    relay.configure(h, c, m != 0);
  }

  void select() {
    kit.selected = selected;
    selection.setText(KitView.NAMES[selected] + " · A" + inputs[selected]);
  }

  void play(int part, int velocity) {
    kit.flash(part);
    if (audio != null && audioFocused && mode.getSelectedItemPosition() != 1)
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

  void showLearn() {
    learnText.setText(
        learn < 0
            ? "Setup finished / idle. Assignments saved."
            : "Play " + KitView.NAMES[learn] + " twice · " + confirmations + " / 2 confirmed");
    if (learn >= 0) {
      selected = learn;
      select();
      kit.invalidate();
    }
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

  final Runnable tick =
      new Runnable() {
        public void run() {
          status.setText(
              usbStatus
                  + "\nDrums: "
                  + drumDevice
                  + " · Pedals: "
                  + pedalDevice
                  + "\n"
                  + relay.status);
          pedalText.setText(
              "Kick "
                  + kickRaw
                  + " · Hi-hat "
                  + hatRaw
                  + (SystemClock.elapsedRealtime() - pedalSeen < 500
                      ? (closed ? " · Closed" : " · Open")
                      : " · No pedal data"));
          ui.postDelayed(this, 250);
        }
      };

  void thresholdDialog() {
    int input = inputs[selected];
    int[] defaults = {140, 20, 20, 20, 10, 60, 10, 10}, resets = {40, 5, 10, 5, 1, 1, 5, 9};
    LinearLayout box = new LinearLayout(this);
    box.setOrientation(LinearLayout.VERTICAL);
    box.addView(text("Physical A" + input + " · 2–1022 hit, reset below hit", 13));
    EditText hit = new EditText(this), reset = new EditText(this);
    hit.setInputType(android.text.InputType.TYPE_CLASS_NUMBER);
    reset.setInputType(android.text.InputType.TYPE_CLASS_NUMBER);
    hit.setText("" + prefs.getInt("threshold" + input, defaults[input]));
    reset.setText("" + prefs.getInt("reset" + input, resets[input]));
    box.addView(text("Hit threshold", 14));
    box.addView(hit);
    box.addView(text("Reset threshold", 14));
    box.addView(reset);
    new AlertDialog.Builder(this)
        .setTitle(KitView.NAMES[selected] + " trigger")
        .setView(box)
        .setNegativeButton("Cancel", null)
        .setPositiveButton(
            "Apply",
            (d, w) -> {
              try {
                int h = Integer.parseInt(hit.getText().toString()),
                    r = Integer.parseInt(reset.getText().toString());
                if (h < 2 || h > 1022 || r < 1 || r >= h) throw new NumberFormatException();
                prefs.edit().putInt("threshold" + input, h).putInt("reset" + input, r).apply();
                if (usb != null) usb.applyThresholds();
                toast("Thresholds saved and queued to the drum Nano");
              } catch (NumberFormatException e) {
                toast("Invalid thresholds. Hit: 2–1022; reset: 1 to hit minus 1.");
              }
            })
        .show();
  }

  void gainDialog() {
    int part = selected;
    LinearLayout box = new LinearLayout(this);
    box.setOrientation(LinearLayout.VERTICAL);
    TextView value = text("", 16);
    SeekBar gain = new SeekBar(this);
    gain.setMax(36);
    gain.setProgress((int) (20 * Math.log10(audio == null ? 1 : audio.gains[part])) + 24);
    value.setText((gain.getProgress() - 24) + " dB");
    gain.setOnSeekBarChangeListener(
        new SeekBar.OnSeekBarChangeListener() {
          public void onProgressChanged(SeekBar s, int p, boolean u) {
            value.setText((p - 24) + " dB");
            if (audio != null) audio.gains[part] = (float) Math.pow(10, (p - 24) / 20.0);
            prefs.edit().putFloat("gain" + part, audio == null ? 1 : audio.gains[part]).apply();
          }

          public void onStartTrackingTouch(SeekBar s) {}

          public void onStopTrackingTouch(SeekBar s) {}
        });
    box.addView(value);
    box.addView(gain);
    new AlertDialog.Builder(this)
        .setTitle(KitView.NAMES[part] + " gain")
        .setView(box)
        .setPositiveButton("Done", null)
        .show();
  }

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
                          ui.post(() -> toast("V05 kit ready. Samples stay available offline."));
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
  protected void onDestroy() {
    ui.removeCallbacksAndMessages(null);
    if (usb != null) usb.close();
    if (relay != null) relay.close();
    if (audio != null) audio.close();
    if (manager != null) manager.abandonAudioFocusRequest(focus);
    super.onDestroy();
  }
}
