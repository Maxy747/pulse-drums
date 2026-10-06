package com.pulse.mobile;

import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

/**
 * Finds Pulse PCs on the local network. Broadcasts PULSE_FIND1|nonce to UDP 9876; Pulse on Windows
 * (Phone input on) answers PULSE_HERE1|nonce|name|code, where the code is only present during the
 * PC's pairing window. Nothing leaves the local subnet.
 */
final class PcDiscovery {
  static final int PORT = 9876;

  static final class Found {
    final String address, name, code;

    Found(String address, String name, String code) {
      this.address = address;
      this.name = name;
      this.code = code;
    }
  }

  /** Parses one reply; returns null unless it answers this nonce and is well formed. */
  static Found parse(String reply, String nonce, String address) {
    if (reply == null || reply.length() > 128) return null;
    String[] p = reply.split("\\|", -1);
    if (p.length != 4 || !p[0].equals("PULSE_HERE1") || !p[1].equals(nonce)) return null;
    if (p[2].isEmpty() || p[2].length() > 40) return null;
    if (!p[3].isEmpty() && !p[3].matches("\\d{8}")) return null;
    return new Found(address, p[2], p[3]);
  }

  /** Blocking; call off the main thread. Returns each answering PC once. */
  static List<Found> find(int timeoutMs) {
    String nonce = UUID.randomUUID().toString();
    byte[] request = ("PULSE_FIND1|" + nonce).getBytes(StandardCharsets.US_ASCII);
    Map<String, Found> found = new LinkedHashMap<>();
    try (DatagramSocket socket = new DatagramSocket()) {
      socket.setBroadcast(true);
      Set<InetAddress> targets = broadcastAddresses();
      for (int round = 0; round < 2; round++) {
        for (InetAddress target : targets) {
          try {
            socket.send(new DatagramPacket(request, request.length, target, PORT));
          } catch (Exception ignored) {
            // One unreachable interface must not stop the others.
          }
        }
        long end = System.currentTimeMillis() + timeoutMs / 2;
        byte[] buffer = new byte[256];
        while (true) {
          long left = end - System.currentTimeMillis();
          if (left <= 0) break;
          socket.setSoTimeout((int) Math.max(1, left));
          DatagramPacket reply = new DatagramPacket(buffer, buffer.length);
          try {
            socket.receive(reply);
          } catch (SocketTimeoutException e) {
            break;
          }
          if (reply.getPort() != PORT) continue;
          String address = reply.getAddress().getHostAddress();
          Found f =
              parse(
                  new String(buffer, 0, reply.getLength(), StandardCharsets.US_ASCII),
                  nonce,
                  address);
          if (f != null && (!found.containsKey(address) || !f.code.isEmpty()))
            found.put(address, f);
        }
        if (!found.isEmpty()) break;
      }
    } catch (Exception ignored) {
      // No network: report nothing found.
    }
    return new ArrayList<>(found.values());
  }

  static Set<InetAddress> broadcastAddresses() {
    Set<InetAddress> out = new LinkedHashSet<>();
    try {
      for (NetworkInterface n : Collections.list(NetworkInterface.getNetworkInterfaces())) {
        if (!n.isUp() || n.isLoopback()) continue;
        for (InterfaceAddress a : n.getInterfaceAddresses())
          if (a.getBroadcast() != null) out.add(a.getBroadcast());
      }
    } catch (Exception ignored) {
    }
    try {
      out.add(InetAddress.getByName("255.255.255.255"));
    } catch (UnknownHostException ignored) {
    }
    return out;
  }

  /** Picks the PC to use: the previously paired name, else one offering a code, else the only one. */
  static Found choose(List<Found> all, String savedName) {
    for (Found f : all) if (f.name.equals(savedName)) return f;
    for (Found f : all) if (!f.code.isEmpty()) return f;
    return all.size() == 1 ? all.get(0) : null;
  }

  private PcDiscovery() {}
}
