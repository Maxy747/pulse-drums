package com.pulse.mobile;

import android.content.*;
import android.media.*;
import java.io.*;
import java.util.*;

/** Preloaded stereo mixer. WAV decoding and resampling never run on the render thread. */
final class DrumAudio {
  final float[][] sounds = new float[9][];
  final float[] pan = {-.75f, -.65f, -.25f, -.35f, .25f, 0, .5f, .8f};
  volatile float volume = .8f;
  final float[] gains = {1, 1, 1, 1, 1, 1, 1, 1};
  volatile boolean paused, running = true;
  final ArrayList<Voice> voices = new ArrayList<>();
  final AudioTrack track;
  final Thread render;

  static final class Voice {
    float[] data;
    int at, part, fade = -1;
    float left, right;

    Voice(float[] d, int p, float l, float r) {
      data = d;
      part = p;
      left = l;
      right = r;
    }
  }

  DrumAudio(Context c) throws IOException {
    for (int i = 0; i < 9; i++) {
      File f = new File(c.getFilesDir(), "synth-" + i + ".wav");
      if (!f.exists()) generate(f, i);
      sounds[i] = WaveData.read(f);
    }
    int minimum =
        AudioTrack.getMinBufferSize(
            48000, AudioFormat.CHANNEL_OUT_STEREO, AudioFormat.ENCODING_PCM_16BIT);
    track =
        new AudioTrack.Builder()
            .setAudioAttributes(
                new AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_GAME)
                    .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                    .build())
            .setAudioFormat(
                new AudioFormat.Builder()
                    .setSampleRate(48000)
                    .setChannelMask(AudioFormat.CHANNEL_OUT_STEREO)
                    .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                    .build())
            .setTransferMode(AudioTrack.MODE_STREAM)
            .setBufferSizeInBytes(Math.max(2048, minimum))
            .setPerformanceMode(AudioTrack.PERFORMANCE_MODE_LOW_LATENCY)
            .build();
    if (track.getState() != AudioTrack.STATE_INITIALIZED)
      throw new IOException("Phone audio output unavailable");
    short[] silence = new short[Math.max(2048, minimum) / 2];
    track.write(silence, 0, silence.length, AudioTrack.WRITE_NON_BLOCKING);
    track.play();
    render = new Thread(this::mix, "Pulse audio");
    render.start();
  }

  boolean load(int part, File file) {
    try {
      float[] data = WaveData.read(file);
      synchronized (voices) {
        sounds[part] = data;
      }
      return true;
    } catch (IOException e) {
      return false;
    }
  }

  void hit(int part, int velocity, boolean open) {
    synchronized (voices) {
      float[] data = sounds[part == 0 && open ? 8 : part];
      if (data == null) return;
      float level = volume * gains[part] * velocity / 127f, p = pan[part];
      if (part == 0) choke();
      if (voices.size() == 32) voices.remove(0);
      voices.add(
          new Voice(
              data,
              part,
              level * (float) Math.sqrt((1 - p) / 2),
              level * (float) Math.sqrt((1 + p) / 2)));
    }
  }

  void choke() {
    synchronized (voices) {
      for (Voice v : voices) if (v.part == 0 && v.fade < 0) v.fade = 240;
    }
  }

  void mix() {
    android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_AUDIO);
    float[] mix = new float[512];
    short[] out = new short[512];
    float limiter = 1;
    try {
      while (running) {
        Arrays.fill(mix, 0);
        synchronized (voices) {
          if (paused) voices.clear();
          for (Iterator<Voice> it = voices.iterator(); it.hasNext(); ) {
            Voice v = it.next();
            for (int i = 0; i < mix.length && v.at < v.data.length; i += 2) {
              float fade = v.fade < 0 ? 1 : v.fade / 240f;
              if (v.fade == 0) break;
              mix[i] += v.data[v.at++] * v.left * fade;
              mix[i + 1] += v.data[v.at++] * v.right * fade;
              if (v.fade > 0) v.fade--;
            }
            if (v.at >= v.data.length || v.fade == 0) it.remove();
          }
        }
        for (int i = 0; i < 512; i += 2) {
          float peak = Math.max(Math.abs(mix[i]), Math.abs(mix[i + 1]));
          float target = peak > .94f ? .94f / peak : 1;
          limiter = target < limiter ? target : limiter + (target - limiter) * .001f;
          out[i] = (short) (mix[i] * limiter * 32767);
          out[i + 1] = (short) (mix[i + 1] * limiter * 32767);
        }
        int offset = 0;
        while (running && offset < out.length) {
          int n = track.write(out, offset, out.length - offset, AudioTrack.WRITE_BLOCKING);
          if (n <= 0) {
            running = false;
            break;
          }
          offset += n;
        }
      }
    } finally {
      track.stop();
      track.release();
    }
  }

  void close() {
    running = false;
    try {
      render.join(1000);
    } catch (InterruptedException e) {
      Thread.currentThread().interrupt();
    }
  }

  static void le(DataOutputStream out, int n, int bytes) throws IOException {
    for (int i = 0; i < bytes; i++) out.writeByte(n >> (8 * i));
  }

  static void generate(File file, int part) throws IOException {
    int rate = 44100, n = (int) (rate * (part == 1 || part == 7 ? 1.4 : part == 8 ? .7 : .45));
    Random random = new Random(820 + part);
    double phase = 0, prev = 0;
    try (DataOutputStream o = new DataOutputStream(new FileOutputStream(file))) {
      o.writeBytes("RIFF");
      le(o, 36 + n * 2, 4);
      o.writeBytes("WAVEfmt ");
      le(o, 16, 4);
      le(o, 1, 2);
      le(o, 1, 2);
      le(o, rate, 4);
      le(o, rate * 2, 4);
      le(o, 2, 2);
      le(o, 16, 2);
      o.writeBytes("data");
      le(o, n * 2, 4);
      for (int j = 0; j < n; j++) {
        double t = j / (double) rate,
            noise = random.nextDouble() * 2 - 1,
            high = noise - prev * .85;
        prev = noise;
        double v;
        if (part == 5) {
          phase += 2 * Math.PI * (48 + 95 * Math.exp(-t * 35)) / rate;
          v = Math.sin(phase) * Math.exp(-t * 12);
        } else if (part == 2 || part == 4 || part == 6) {
          double f = part == 4 ? 180 : part == 2 ? 125 : 85;
          phase += 2 * Math.PI * (f + 45 * Math.exp(-t * 22)) / rate;
          v = (Math.sin(phase) * .9 + noise * .1) * Math.exp(-t * 9);
        } else if (part == 3) {
          v = (noise * .75 + Math.sin(t * 2 * Math.PI * 180) * .25) * Math.exp(-t * 16);
        } else {
          double decay = part == 0 ? 55 : part == 8 ? 9 : part == 1 ? 4 : 6;
          v =
              (high * .5
                      + Math.sin(t * 2 * Math.PI * 5431) * .12
                      + Math.sin(t * 2 * Math.PI * 8123) * .1)
                  * Math.exp(-t * decay);
        }
        v *= Math.min(1, t * 1500) * Math.min(1, (n - j) / 220.0);
        le(o, (int) (Math.max(-1, Math.min(1, v * .8)) * 32767), 2);
      }
    }
  }
}
