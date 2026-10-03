package com.pulse.mobile;

import java.io.*;
import java.net.*;

final class AcousticKit {
  static final String ROOT =
      "https://raw.githubusercontent.com/gregharvey/drum-samples/ea524c8a952545fc099565426fc673e79076136f/";
  static final String[] FILES = {
    "1/HHats-CL-V05-SABIAN-AAX.wav",
    "1/14-Crash-V05-SABIAN-14.wav",
    "2/V05-TTom-12.wav",
    "1/SNARE-V05-CustomWorks-6x13.wav",
    "1/TOM10-V05-StarClassic-10x10.wav",
    "1/Kick-V05-Yamaha-16x16.wav",
    "1/TOM13-V05-StarClassic-13x13.wav",
    "1/Ride-V05-ROBMOR-SABIAN-22.wav",
    "1/HHats-OP-V05-SABIAN-AAX.wav"
  };

  static File download(File folder, int part) throws IOException {
    String path =
        "GSCW Drums Kit " + FILES[part].charAt(0) + " Samples/" + FILES[part].substring(2);
    URL url = new URL(ROOT + path.replace(" ", "%20"));
    HttpURLConnection c = (HttpURLConnection) url.openConnection();
    c.setConnectTimeout(15000);
    c.setReadTimeout(20000);
    File temp = new File(folder, "download-" + part + ".wav");
    try {
      if (c.getResponseCode() != 200)
        throw new IOException("Sample download: HTTP " + c.getResponseCode());
      try (InputStream in = c.getInputStream();
          OutputStream out = new FileOutputStream(temp)) {
        byte[] b = new byte[8192];
        int n, total = 0;
        while ((n = in.read(b)) != -1) {
          total += n;
          if (total > 16 * 1024 * 1024) throw new IOException("Sample exceeds size limit");
          out.write(b, 0, n);
        }
      }
      WaveData.read(temp);
      return temp;
    } catch (IOException e) {
      temp.delete();
      throw e;
    } finally {
      c.disconnect();
    }
  }
}
