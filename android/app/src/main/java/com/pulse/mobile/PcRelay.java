package com.pulse.mobile;

import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.UUID;
import java.util.concurrent.*;

/** Timestamped, bounded low-latency queue: stale strikes are dropped rather than replayed late. */
final class PcRelay implements AutoCloseable {
  static final class Pending {
    final String text;
    final long at = System.nanoTime();

    Pending(String text) {
      this.text = text;
    }
  }

  final ArrayBlockingQueue<Pending> queue = new ArrayBlockingQueue<>(128);
  volatile boolean running = true, enabled;
  volatile String host = "", code = "", status = "PC relay off";
  volatile long acknowledged;
  final String session = UUID.randomUUID().toString();
  long sequence;
  DatagramSocket socket;
  final Thread worker;

  PcRelay() {
    worker = new Thread(this::run, "Pulse Wi-Fi");
    worker.start();
  }

  void send(String payload) {
    if (!enabled) return;
    Pending p = new Pending(payload);
    if (!queue.offer(p)) {
      queue.poll();
      queue.offer(p);
    }
  }

  void configure(String host, String code, boolean enabled) {
    this.enabled = false;
    this.host = host.trim();
    this.code = code.trim();
    queue.clear();
    acknowledged = 0;
    status = enabled ? "Waiting for Pulse PC" : "PC relay off";
    this.enabled = enabled;
  }

  void transmit(String payload, InetAddress address) throws java.io.IOException {
    byte[] b =
        ("PULSE_M1|" + code + "|" + session + "|" + (++sequence) + "|" + payload)
            .getBytes(StandardCharsets.US_ASCII);
    socket.send(new DatagramPacket(b, b.length, address, 9876));
  }

  void run() {
    try {
      socket = new DatagramSocket();
      socket.setSoTimeout(1);
      String previous = "";
      InetAddress address = null;
      long heartbeat = 0, poll = 0;
      byte[] replyBytes = new byte[256];
      while (running) {
        if (!enabled || host.isEmpty() || code.isEmpty()) {
          Thread.sleep(50);
          continue;
        }
        try {
          if (!host.equals(previous)) {
            address = InetAddress.getByName(host);
            previous = host;
          }
          Pending pending = queue.poll(1, TimeUnit.MILLISECONDS);
          long now = System.nanoTime() / 1000000;
          if (now - heartbeat >= 500) {
            transmit("PING", address);
            heartbeat = now;
          }
          if (pending != null && System.nanoTime() - pending.at < 100000000)
            transmit(pending.text, address);
          if (now - poll >= 10) {
            poll = now;
            DatagramPacket reply = new DatagramPacket(replyBytes, replyBytes.length);
            try {
              socket.receive(reply);
              String text = new String(replyBytes, 0, reply.getLength(), StandardCharsets.US_ASCII);
              if (reply.getAddress().equals(address)
                  && reply.getPort() == 9876
                  && text.equals("PULSE_ACK|" + session)) {
                acknowledged = now;
                status = "Pulse PC connected";
              }
            } catch (SocketTimeoutException ignored) {
            }
          }
          if (now - acknowledged > 2500) status = "No PC reply · check address, code and firewall";
        } catch (Exception e) {
          status = "PC: " + e.getMessage();
          Thread.sleep(500);
        }
      }
    } catch (Exception e) {
      if (running) status = e.getMessage();
    } finally {
      if (socket != null) socket.close();
    }
  }

  public void close() {
    running = false;
    if (socket != null) socket.close();
    worker.interrupt();
  }
}
