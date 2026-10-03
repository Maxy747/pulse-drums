package com.pulse.mobile;

import java.io.*;

/**
 * Decode/resample outside the render thread; supports desktop Pulse PCM16/24/32 and float32 WAVs.
 */
final class WaveData {
  static float[] read(File file) throws IOException {
    try (RandomAccessFile f = new RandomAccessFile(file, "r")) {
      if (f.readInt() != 0x52494646) throw new IOException("Choose a WAV file");
      f.skipBytes(4);
      if (f.readInt() != 0x57415645) throw new IOException("Not a WAV file");
      int format = 0, channels = 0, rate = 0, bits = 0, align = 0;
      byte[] data = null;
      while (f.getFilePointer() + 8 <= f.length()) {
        int id = f.readInt();
        long len = Integer.toUnsignedLong(Integer.reverseBytes(f.readInt())),
            start = f.getFilePointer();
        if (len > f.length() - start) throw new IOException("Truncated WAV");
        if (id == 0x666d7420) {
          if (len < 16) throw new IOException("Invalid WAV format");
          format = Short.toUnsignedInt(Short.reverseBytes(f.readShort()));
          channels = Short.toUnsignedInt(Short.reverseBytes(f.readShort()));
          rate = Integer.reverseBytes(f.readInt());
          f.skipBytes(4);
          align = Short.toUnsignedInt(Short.reverseBytes(f.readShort()));
          bits = Short.toUnsignedInt(Short.reverseBytes(f.readShort()));
          if (format == 65534 && len >= 40) {
            f.seek(start + 24);
            format = Short.toUnsignedInt(Short.reverseBytes(f.readShort()));
          }
        } else if (id == 0x64617461) {
          if (len > 16 * 1024 * 1024) throw new IOException("Sample is too large (16 MB maximum)");
          data = new byte[(int) len];
          f.readFully(data);
        }
        f.seek(start + len + (len & 1));
      }
      if (data == null
          || channels < 1
          || channels > 2
          || rate < 8000
          || rate > 192000
          || !((format == 1 && (bits == 16 || bits == 24 || bits == 32))
              || (format == 3 && bits == 32))
          || align != channels * (bits / 8))
        throw new IOException("Use mono/stereo PCM 16/24/32-bit or float32 WAV");
      int frames = data.length / align;
      if (frames < 2 || frames > rate * 12)
        throw new IOException("Samples must be between 2 frames and 12 seconds");
      int count = (int) ((long) frames * 48000 / rate);
      float[] result = new float[count * 2];
      for (int i = 0; i < count; i++) {
        double source = i * (double) rate / 48000;
        int a = Math.min(frames - 1, (int) source), b = Math.min(frames - 1, a + 1);
        float fraction = (float) (source - a);
        for (int channel = 0; channel < 2; channel++) {
          int c = Math.min(channel, channels - 1);
          float x = value(data, a * align + c * (bits / 8), bits, format),
              y = value(data, b * align + c * (bits / 8), bits, format);
          result[i * 2 + channel] = x + (y - x) * fraction;
        }
      }
      return result;
    }
  }

  static float value(byte[] b, int p, int bits, int format) {
    int n = 0;
    for (int i = 0; i < bits / 8; i++) n |= (b[p + i] & 255) << (8 * i);
    float v;
    if (format == 3) v = Float.intBitsToFloat(n);
    else if (bits == 16) v = (short) n / 32768f;
    else if (bits == 24) v = (n << 8 >> 8) / 8388608f;
    else v = n / 2147483648f;
    return Float.isFinite(v) ? Math.max(-1, Math.min(1, v)) : 0;
  }
}
