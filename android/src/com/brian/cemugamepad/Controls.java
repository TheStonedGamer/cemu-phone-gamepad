package com.brian.cemugamepad;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import android.os.SystemClock;

final class Controls {
  int buttons;
  float lx, ly, rx, ry, tx, ty;
  boolean touch;
  float[] accel = {0, -1, 0}, gyro = {0, 0, 0};
  long accelTime, gyroTime;
  synchronized void clear() { buttons = 0; lx = ly = rx = ry = 0; touch = false; }
  synchronized byte[] packet(int sequence) {
    long now = SystemClock.elapsedRealtimeNanos();
    boolean av = accelTime != 0 && now - accelTime < 200000000L;
    boolean gv = gyroTime != 0 && now - gyroTime < 200000000L;
    ByteBuffer p = ByteBuffer.allocate(80).order(ByteOrder.BIG_ENDIAN);
    p.put(new byte[] {'P','G','P','D',1,(byte)((touch ? 1 : 0) | (av ? 2 : 0) | (gv ? 4 : 0)),0,0});
    p.putInt(buttons).putInt(sequence);
    p.putFloat(lx).putFloat(ly).putFloat(rx).putFloat(ry).putFloat(tx).putFloat(ty);
    for (float value : accel) p.putFloat(value);
    for (float value : gyro) p.putFloat(value);
    p.putLong(now / 1000);
    return p.array();
  }
}
