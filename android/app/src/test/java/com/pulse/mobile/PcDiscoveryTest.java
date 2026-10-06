package com.pulse.mobile;

import static org.junit.Assert.*;

import java.util.Arrays;
import java.util.List;
import org.junit.Test;

public class PcDiscoveryTest {
  @Test
  public void parsesReplyForThisNonce() {
    PcDiscovery.Found f =
        PcDiscovery.parse("PULSE_HERE1|abc-123|DESKTOP|12345678", "abc-123", "192.168.1.5");
    assertEquals("192.168.1.5", f.address);
    assertEquals("DESKTOP", f.name);
    assertEquals("12345678", f.code);
    assertEquals("", PcDiscovery.parse("PULSE_HERE1|abc-123|DESKTOP|", "abc-123", "x").code);
  }

  @Test
  public void rejectsForeignOrMalformedReplies() {
    for (String bad :
        new String[] {
          null,
          "PULSE_HERE1|other|DESKTOP|12345678",
          "PULSE_HERE1|abc-123||12345678",
          "PULSE_HERE1|abc-123|DESKTOP|1234",
          "PULSE_HERE1|abc-123|DESKTOP|12345678|extra",
          "PULSE_ACK|abc-123"
        }) assertNull(bad, PcDiscovery.parse(bad, "abc-123", "x"));
  }

  @Test
  public void collapsesOnePcSeenOnSeveralAdapters() {
    List<PcDiscovery.Found> all =
        Arrays.asList(
            new PcDiscovery.Found("192.168.56.1", "MAX-PC", "12345678"),
            new PcDiscovery.Found("192.168.1.85", "MAX-PC", "12345678"),
            new PcDiscovery.Found("192.168.1.90", "STUDIO", ""));
    List<int[]> phone = Arrays.<int[]>asList(new int[] {PcDiscovery.ipv4("192.168.1.40"), 24});
    List<PcDiscovery.Found> one = PcDiscovery.onePerPc(all, phone);
    assertEquals(2, one.size());
    assertEquals("192.168.1.85", one.get(0).address);
  }

  @Test
  public void choosesSavedThenPairableThenOnly() {
    PcDiscovery.Found a = new PcDiscovery.Found("1", "Studio", ""),
        b = new PcDiscovery.Found("2", "Laptop", "12345678");
    assertSame(a, PcDiscovery.choose(Arrays.asList(a, b), "Studio"));
    assertSame(b, PcDiscovery.choose(Arrays.asList(a, b), ""));
    assertSame(a, PcDiscovery.choose(Arrays.asList(a), ""));
    assertNull(PcDiscovery.choose(Arrays.asList(a, new PcDiscovery.Found("3", "Other", "")), ""));
  }
}
