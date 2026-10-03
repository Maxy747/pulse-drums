package com.pulse.mobile;

import static org.junit.Assert.*;

import java.io.*;
import org.junit.Test;

public class WaveDataTest {
  @Test
  public void pcmAndFloatDecoding() {
    assertEquals(-1, WaveData.value(new byte[] {0, (byte) 128}, 0, 16, 1), 0);
    assertEquals(-1, WaveData.value(new byte[] {0, 0, (byte) 128}, 0, 24, 1), 0);
    assertEquals(.5f, WaveData.value(new byte[] {0, 0, 0, 64}, 0, 32, 1), 0);
    assertEquals(.5f, WaveData.value(new byte[] {0, 0, 0, 63}, 0, 32, 3), 0);
    assertEquals(0, WaveData.value(new byte[] {0, 0, (byte) 128, 127}, 0, 32, 3), 0);
  }

  @Test
  public void resamplesStereoWithoutChannelMixing() throws Exception {
    File f = File.createTempFile("pulse-wave", ".wav");
    try (DataOutputStream o = new DataOutputStream(new FileOutputStream(f))) {
      o.writeBytes("RIFF");
      le(o, 52, 4);
      o.writeBytes("WAVEfmt ");
      le(o, 16, 4);
      le(o, 1, 2);
      le(o, 2, 2);
      le(o, 24000, 4);
      le(o, 96000, 4);
      le(o, 4, 2);
      le(o, 16, 2);
      o.writeBytes("data");
      le(o, 16, 4);
      for (int i = 0; i < 4; i++) {
        le(o, 16384, 2);
        le(o, -8192, 2);
      }
    }
    try {
      float[] a = WaveData.read(f);
      assertEquals(16, a.length);
      for (int i = 0; i < a.length; i += 2) {
        assertEquals(.5, a[i], .00001);
        assertEquals(-.25, a[i + 1], .00001);
      }
    } finally {
      f.delete();
    }
  }

  @Test
  public void validatesInstalledV05SamplesWhenAvailable() throws Exception {
    String local = System.getenv("LOCALAPPDATA");
    if (local == null) return;
    File root = new File(local, "PulseDrums/Samples/GSCW");
    if (!root.exists()) return;
    for (String s : AcousticKit.FILES) {
      File f = new File(root, "GSCW Drums Kit " + s.charAt(0) + " Samples/" + s.substring(2));
      float[] wave = WaveData.read(f);
      assertTrue(wave.length > 1000);
      float peak = 0;
      for (float v : wave) {
        assertTrue(Float.isFinite(v));
        peak = Math.max(peak, Math.abs(v));
      }
      assertTrue(peak > .01);
    }
  }

  static void le(DataOutputStream o, int n, int bytes) throws IOException {
    for (int i = 0; i < bytes; i++) o.writeByte(n >> (8 * i));
  }
}
