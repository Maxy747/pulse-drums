package com.pulse.mobile;

import android.app.*;
import android.content.*;
import android.hardware.usb.*;
import android.os.*;
import com.hoho.android.usbserial.driver.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;

/** One independent bounded reader per USB device, including two identical CH340 Nanos. */
final class UsbHub implements AutoCloseable {
  interface Listener {
    SerialProtocol.Sink sink(String device);

    void status(String status);
  }

  final Context context;
  final UsbManager manager;
  final Listener listener;
  final Map<Integer, UsbSerialPort> ports = new ConcurrentHashMap<>();
  final Set<Integer> mainIds = ConcurrentHashMap.newKeySet();
  volatile int thresholdVersion;
  final Set<Integer> opening = ConcurrentHashMap.newKeySet();
  final ExecutorService workers = Executors.newCachedThreadPool();
  final Handler handler = new Handler(Looper.getMainLooper());
  final String permission;
  volatile boolean stopped;
  Integer permissionPending;
  final Set<Integer> denied = new HashSet<>();
  final BroadcastReceiver receiver =
      new BroadcastReceiver() {
        public void onReceive(Context c, Intent i) {
          if (permission.equals(i.getAction())) {
            UsbDevice d = i.getParcelableExtra(UsbManager.EXTRA_DEVICE);
            permissionPending = null;
            if (d != null && !manager.hasPermission(d)) denied.add(d.getDeviceId());
            scan();
          } else if (UsbManager.ACTION_USB_DEVICE_DETACHED.equals(i.getAction())) {
            UsbDevice d = i.getParcelableExtra(UsbManager.EXTRA_DEVICE);
            if (d != null) {
              denied.remove(d.getDeviceId());
              closePort(d.getDeviceId());
            }
          } else scan();
        }
      };

  UsbHub(Context c, Listener l) {
    context = c;
    listener = l;
    manager = (UsbManager) c.getSystemService(Context.USB_SERVICE);
    permission = c.getPackageName() + ".USB_PERMISSION";
    IntentFilter f = new IntentFilter(permission);
    f.addAction(UsbManager.ACTION_USB_DEVICE_ATTACHED);
    f.addAction(UsbManager.ACTION_USB_DEVICE_DETACHED);
    if (Build.VERSION.SDK_INT >= 33) c.registerReceiver(receiver, f, Context.RECEIVER_NOT_EXPORTED);
    else c.registerReceiver(receiver, f);
    handler.post(scanLoop);
  }

  final Runnable scanLoop =
      new Runnable() {
        public void run() {
          if (!stopped) {
            scan();
            handler.postDelayed(this, 2000);
          }
        }
      };

  void applyThresholds() {
    thresholdVersion++;
  }

  void retry() {
    denied.clear();
    scan();
  }

  void scan() {
    if (stopped) return;
    for (UsbSerialDriver driver : UsbSerialProber.getDefaultProber().findAllDrivers(manager)) {
      UsbDevice d = driver.getDevice();
      int id = d.getDeviceId();
      if (ports.containsKey(id) || opening.contains(id) || denied.contains(id)) continue;
      if (!manager.hasPermission(d)) {
        if (permissionPending == null) {
          permissionPending = id;
          manager.requestPermission(
              d,
              PendingIntent.getBroadcast(
                  context,
                  id,
                  new Intent(permission).setPackage(context.getPackageName()),
                  PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_MUTABLE));
        }
        continue;
      }
      opening.add(id);
      workers.execute(() -> read(driver));
    }
    listener.status(
        ports.size() + " Nano USB connection" + (ports.size() == 1 ? "" : "s") + " · 115200 baud");
  }

  void read(UsbSerialDriver driver) {
    int id = driver.getDevice().getDeviceId();
    UsbSerialPort port = driver.getPorts().get(0);
    UsbDeviceConnection connection = null;
    try {
      connection = manager.openDevice(driver.getDevice());
      if (connection == null) throw new Exception("USB permission unavailable");
      port.open(connection);
      port.setParameters(115200, 8, UsbSerialPort.STOPBITS_1, UsbSerialPort.PARITY_NONE);
      port.setDTR(true);
      port.setRTS(true);
      ports.put(id, port);
      SerialProtocol.Sink target = listener.sink("USB " + id);
      SerialProtocol decoder =
          new SerialProtocol(
              new SerialProtocol.Sink() {
                public void hit(int i, int v, int p) {
                  target.hit(i, v, p);
                }

                public void raw(int i, int p) {
                  mainIds.add(id);
                  target.raw(i, p);
                }

                public void pedals(long t, int k, int h) {
                  target.pedals(t, k, h);
                }
              });
      long opened = SystemClock.elapsedRealtime();
      int applied = -1;
      java.util.ArrayDeque<String> commands = new java.util.ArrayDeque<>();
      byte[] buffer = new byte[1024];
      while (!stopped && ports.get(id) == port) {
        if (mainIds.contains(id)
            && SystemClock.elapsedRealtime() - opened >= 1800
            && applied != thresholdVersion) {
          applied = thresholdVersion;
          commands.clear();
          android.content.SharedPreferences prefs = context.getSharedPreferences("pulse", 0);
          int[] hits = {140, 20, 20, 20, 10, 60, 10, 10}, resets = {40, 5, 10, 5, 1, 1, 5, 9};
          for (int i = 0; i < 8; i++) {
            commands.add("SET,RESET," + i + "," + prefs.getInt("reset" + i, resets[i]) + "\n");
            commands.add("SET,HIT," + i + "," + prefs.getInt("threshold" + i, hits[i]) + "\n");
          }
        }
        if (!commands.isEmpty()) {
          port.write(commands.remove().getBytes(StandardCharsets.US_ASCII), 1000);
          Thread.sleep(25);
        }
        int n = port.read(buffer, 20);
        if (n > 0) decoder.feed(buffer, n, SystemClock.elapsedRealtime());
      }
    } catch (Exception e) {
      listener.status("USB " + id + ": " + e.getMessage());
    } finally {
      ports.remove(id, port);
      mainIds.remove(id);
      try {
        port.close();
      } catch (Exception ignored) {
      }
      if (connection != null) connection.close();
      opening.remove(id);
    }
  }

  void closePort(int id) {
    UsbSerialPort p = ports.remove(id);
    if (p != null)
      try {
        p.close();
      } catch (Exception ignored) {
      }
  }

  public void close() {
    stopped = true;
    handler.removeCallbacksAndMessages(null);
    context.unregisterReceiver(receiver);
    for (int id : ports.keySet()) closePort(id);
    workers.shutdownNow();
  }
}
