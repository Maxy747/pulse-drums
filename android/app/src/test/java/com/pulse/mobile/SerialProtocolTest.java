package com.pulse.mobile;

import static org.junit.Assert.*;

import java.nio.charset.StandardCharsets;
import java.util.*;
import org.junit.Test;

public class SerialProtocolTest {
  static class Sink implements SerialProtocol.Sink {
    List<String> events = new ArrayList<>();

    public void hit(int i, int v, int p) {
      events.add("H" + i + ":" + v + ":" + p);
    }

    public void raw(int i, int p) {
      events.add("R" + i);
    }

    public void pedals(long t, int k, int h) {
      events.add("P" + t + ":" + k + ":" + h);
    }
  }

  static void feed(SerialProtocol p, String s, long t) {
    byte[] b = s.getBytes(StandardCharsets.US_ASCII);
    p.feed(b, b.length, t);
  }

  @Test
  public void splitAndTelemetry() {
    Sink s = new Sink();
    SerialProtocol p = new SerialProtocol(s);
    feed(p, "NO", 0);
    feed(p, "TE,38,90\nRAW,1,230\nRAW,1,200\n", 1);
    assertEquals(Arrays.asList("H1:90:230", "R1", "R1"), s.events);
  }

  @Test
  public void staleNoteAndInvalid() {
    Sink s = new Sink();
    SerialProtocol p = new SerialProtocol(s);
    feed(p, "NOTE,38,90\n", 0);
    feed(p, "RAW,1,200\nRAW,8,100\nNOTE,1,128\nPEDALS,0,1024,0\n", 251);
    assertEquals(Arrays.asList("R1"), s.events);
  }

  @Test
  public void pedalClockAndIndependentNanos() {
    Sink a = new Sink(), b = new Sink();
    SerialProtocol drums = new SerialProtocol(a), pedals = new SerialProtocol(b);
    feed(drums, "NOTE,36,100\n", 20);
    feed(pedals, "PEDALS,4294967295,1023,0\n", 21);
    feed(drums, "RAW,0,800\n", 22);
    assertEquals("H0:100:800", a.events.get(0));
    assertEquals("P4294967295:1023:0", b.events.get(0));
  }

  @Test
  public void boundsRecoverAtNewline() {
    Sink s = new Sink();
    SerialProtocol p = new SerialProtocol(s);
    feed(p, "x".repeat(200) + "RAW,0,5\nRAW,2,5\n", 0);
    assertEquals(Arrays.asList("R2"), s.events);
  }

  @Test
  public void assignmentSwapsAndCalibrationInverts() {
    int[] original = {2, 6, 4, 1, 3, 0, 5, 7};
    int[] n = SerialProtocol.assign(original, 0, 0);
    assertEquals(0, n[0]);
    assertEquals(2, n[5]);
    assertEquals(2, original[0]);
    assertEquals(8, Arrays.stream(n).distinct().count());
    assertEquals(1, SerialProtocol.position(100, 900, 100), 0);
    assertEquals(0, SerialProtocol.position(900, 900, 100), 0);
    assertEquals(0, SerialProtocol.position(100, 100, 110), 0);
  }
}
