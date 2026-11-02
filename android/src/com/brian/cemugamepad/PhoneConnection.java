package com.brian.cemugamepad;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.media.AudioAttributes;
import android.media.AudioFormat;
import android.media.AudioTrack;
import android.os.Build;
import android.os.SystemClock;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;
import org.json.JSONObject;

final class PhoneConnection {
  interface Listener {
    void frame(Bitmap bitmap); void status(String text); void paired(String host, String token); void connected(boolean value);
  }
  private static final class Sound {
    final byte[] bytes; final long received = SystemClock.elapsedRealtime();
    Sound(byte[] value) { bytes = value; }
  }
  private static final class Session {
    volatile boolean running = true, connected;
    volatile Socket socket;
    volatile DatagramSocket udp;
    volatile byte[] audioId;
    final AtomicReference<byte[]> video = new AtomicReference<>();
    final ArrayBlockingQueue<Sound> sound = new ArrayBlockingQueue<>(12);
    volatile long frames, packets, bytes, audioPackets, audioDrops, lastAudio;
    volatile int queuedMs, actualBufferMs;
    volatile String transport = "waiting";
    void stop() {
      running = false; connected = false;
      try { if (socket != null) socket.close(); } catch (IOException ignored) { }
      if (udp != null) udp.close(); video.set(null); sound.clear();
    }
  }
  private final Controls controls;
  private final Listener listener;
  private volatile Session current;
  private volatile boolean muted, udpAudio = true;
  private volatile float volume = 1;
  private volatile int targetMs = 30;
  private long statsAt, statsFrames, statsPackets, statsBytes;
  private String stats = "Not connected";
  PhoneConnection(Controls controls, Listener listener) { this.controls = controls; this.listener = listener; }
  void mute(boolean value) { if(muted==value) return; muted=value; Session s=current; if(s!=null) s.sound.clear(); }
  void configure(float value, int bufferMs, boolean udp) { volume = value; targetMs = bufferMs; udpAudio = udp; }
  synchronized String statistics() {
    Session s = current;
    if (s == null || !s.connected) return "Not connected";
    long now = SystemClock.elapsedRealtime(), elapsed = now - statsAt;
    if (elapsed >= 1000) {
      stats = Math.round((s.frames-statsFrames)*1000f/elapsed) + " fps  ·  " + Math.round((s.packets-statsPackets)*1000f/elapsed)
          + " input/s  ·  " + String.format(java.util.Locale.US,"%.1f",(s.bytes-statsBytes)*8.0/elapsed/1000) + " Mb/s";
      statsAt = now; statsFrames = s.frames; statsPackets = s.packets; statsBytes = s.bytes;
    }
    int queued=s.queuedMs; for(Sound pending:s.sound) queued+=pending.bytes.length/192;
    String audio = muted ? "muted" : s.lastAudio == 0 || now-s.lastAudio>2000 ? "waiting for sound" : queued + " ms queued";
    return stats + "\nAudio " + s.transport + " · " + audio + " · " + s.audioDrops + " dropped";
  }
  synchronized void start(String host, String code, String savedToken, String deviceId, String deviceName) {
    stop(); Session s = new Session(); current = s;
    statsAt = SystemClock.elapsedRealtime(); statsFrames = statsPackets = statsBytes = 0;
    new Thread(() -> run(s, host, code, savedToken, deviceId, deviceName), "gamepad-network").start();
  }
  synchronized void stop() {
    Session old = current; current = null; if (old != null) old.stop();
    controls.clear(); listener.connected(false);
  }
  private boolean alive(Session s) { return s.running && current == s; }
  private void status(Session s, String text) { if (alive(s)) listener.status(text); }
  private void markConnected(Session s, boolean value) { s.connected = value; if (alive(s)) listener.connected(value); }
  private void run(Session s, String host, String code, String savedToken, String deviceId, String deviceName) {
    String credential = savedToken;
    int attempt = 0;
    while (alive(s)) {
      Thread sender = null, decoder = null, playback = null, receiver = null;
      try (Socket peer = new Socket()) {
        s.socket = peer; s.audioId = null; s.lastAudio = 0; s.transport = "TCP";
        status(s, attempt == 0 ? "Connecting to " + host : "Reconnecting to " + host + " · attempt " + (attempt+1));
        peer.setTcpNoDelay(true); peer.connect(new InetSocketAddress(host, 26762), 4000); peer.setSoTimeout(5000);
        if (!alive(s)) break;
        if (udpAudio) {
          try { s.udp = new DatagramSocket(); s.udp.setSoTimeout(500); s.udp.setReceiveBufferSize(8192); }
          catch (SocketException ignored) { s.udp = null; }
        }
        OutputStream output = peer.getOutputStream();
        JSONObject request = new JSONObject().put("deviceId", deviceId).put("name", deviceName).put("audioPort", s.udp == null ? 0 : s.udp.getLocalPort());
        if (!credential.isEmpty()) request.put("token", credential); else request.put("code", code);
        byte[] hello = request.toString().getBytes(StandardCharsets.UTF_8);
        DataOutputStream handshake = new DataOutputStream(output);
        handshake.writeBytes("PGP2"); handshake.writeInt(hello.length); handshake.write(hello); handshake.flush();
        sender = new Thread(() -> send(s, peer, output), "gamepad-input"); sender.start();
        decoder = new Thread(() -> decode(s, peer), "gamepad-video"); decoder.start();
        playback = new Thread(() -> playAudio(s, peer), "gamepad-audio"); playback.start();
        if (s.udp != null) {
          final DatagramSocket udp = s.udp;
          receiver = new Thread(() -> receiveAudio(s, peer, udp), "gamepad-audio-network"); receiver.start();
        }
        DataInputStream input = new DataInputStream(peer.getInputStream());
        while (alive(s) && !peer.isClosed()) {
          int type = input.readUnsignedByte(), length = input.readInt();
          if (length < 0 || length > 2000000) throw new IOException("Invalid host frame");
          byte[] payload = new byte[length]; input.readFully(payload); s.bytes += length+5;
          if (!alive(s)) break;
          if (type == 5) { status(s, new String(payload, StandardCharsets.UTF_8)); markConnected(s, false); s.running = false; break; }
          if (type == 4) {
            String issued = new JSONObject(new String(payload, StandardCharsets.UTF_8)).optString("token", "");
            if (!issued.isEmpty()) {
              if (!issued.matches("[0-9A-Fa-f]{64}")) throw new IOException("Invalid saved pairing");
              credential = issued; listener.paired(host, issued);
            }
            markConnected(s, true); attempt = 0; continue;
          }
          if (type == 6) {
            JSONObject config = new JSONObject(new String(payload, StandardCharsets.UTF_8));
            String id = config.optString("session", "");
            if (s.udp != null && id.matches("[0-9A-Fa-f]{32}") && config.optInt("sampleRate") == 48000 && config.optInt("channels") == 2) {
              byte[] key = new byte[16]; for (int i=0;i<16;i++) key[i] = (byte)Integer.parseInt(id.substring(i*2,i*2+2),16);
              s.audioId = key; s.transport = "UDP";
            }
            continue;
          }
          if (!s.connected) markConnected(s, true);
          if (type == 1) s.video.set(payload);
          else if (type == 2 && length % 4 == 0) offerAudio(s, payload);
          else if (type == 3) status(s, new String(payload, StandardCharsets.UTF_8));
        }
      } catch (Exception e) {
        if (alive(s)) status(s, "Connection lost · check Windows host and Wi-Fi. Retrying…");
      } finally {
        markConnected(s, false);
        // Close this attempt before starting another; queues belong to this session.
        Socket peer = s.socket; if (peer != null) try { peer.close(); } catch (IOException ignored) { }
        if (s.udp != null) s.udp.close(); s.udp = null;
        s.video.set(null); s.sound.clear(); if (current == s) controls.clear();
        for (Thread thread : new Thread[] { sender, decoder, playback, receiver }) if (thread != null) {
          thread.interrupt(); try { thread.join(1000); } catch (InterruptedException ignored) { Thread.currentThread().interrupt(); }
        }
      }
      if (alive(s)) { attempt++; try { Thread.sleep(Math.min(5000, 750*attempt)); } catch (InterruptedException ignored) { } }
    }
    s.stop();
  }
  private void send(Session s, Socket peer, OutputStream output) {
    int sequence = 0;
    try { while (alive(s) && !peer.isClosed()) { output.write(controls.packet(sequence++)); s.packets++; Thread.sleep(16); } }
    catch (Exception ignored) { try { peer.close(); } catch (IOException ignoredClose) { } }
  }
  private void decode(Session s, Socket peer) {
    while (alive(s) && !peer.isClosed()) {
      byte[] data = s.video.getAndSet(null);
      if (data == null) { try { Thread.sleep(4); } catch (InterruptedException e) { break; } continue; }
      BitmapFactory.Options bounds = new BitmapFactory.Options(); bounds.inJustDecodeBounds = true;
      BitmapFactory.decodeByteArray(data,0,data.length,bounds);
      if (bounds.outWidth<1 || bounds.outHeight<1 || bounds.outWidth>1920 || bounds.outHeight>1080) continue;
      Bitmap bitmap = BitmapFactory.decodeByteArray(data,0,data.length);
      if (bitmap != null) { if (alive(s) && !peer.isClosed()) { s.frames++; listener.frame(bitmap); } else bitmap.recycle(); }
    }
  }
  private void offerAudio(Session s, byte[] bytes) {
    if (muted || bytes.length == 0) return;
    if (!s.sound.offer(new Sound(bytes))) { s.sound.poll(); s.sound.offer(new Sound(bytes)); s.audioDrops++; }
    s.audioPackets++; s.lastAudio = SystemClock.elapsedRealtime();
  }
  private void receiveAudio(Session s, Socket peer, DatagramSocket udp) {
    android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_AUDIO);
    byte[] buffer = new byte[1200]; boolean haveSequence = false; int last = 0; long clockOffset = Long.MAX_VALUE;
    while (alive(s) && !peer.isClosed() && !udp.isClosed()) {
      try {
        DatagramPacket packet = new DatagramPacket(buffer,buffer.length); udp.receive(packet);
        int length = packet.getLength(); byte[] key = s.audioId;
        if (key == null || !packet.getAddress().equals(peer.getInetAddress()) || length<=32 || (length-32)%4!=0
            || buffer[0]!='P' || buffer[1]!='G' || buffer[2]!='P' || buffer[3]!='A') continue;
        boolean valid = true; for(int i=0;i<16;i++) if(buffer[4+i]!=key[i]) valid=false; if(!valid) continue;
        int sequence = ((buffer[20]&255)<<24)|((buffer[21]&255)<<16)|((buffer[22]&255)<<8)|(buffer[23]&255);
        if(haveSequence && sequence-last<=0) continue;
        if(haveSequence && sequence-last>1) s.audioDrops += sequence-last-1;
        haveSequence=true; last=sequence;
        long sent=0; for(int i=24;i<32;i++) sent=(sent<<8)|(buffer[i]&255L);
        long delta=SystemClock.elapsedRealtime()-sent; clockOffset=Math.min(clockOffset,delta);
        if(delta-clockOffset>targetMs) {s.audioDrops++;continue;}
        offerAudio(s,Arrays.copyOfRange(buffer,32,length));
      } catch (SocketTimeoutException ignored) { }
      catch (IOException e) { break; }
    }
  }
  private void playAudio(Session s, Socket peer) {
    android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_AUDIO);
    AudioTrack track = null;
    try {
      int minimum = AudioTrack.getMinBufferSize(48000,AudioFormat.CHANNEL_OUT_STEREO,AudioFormat.ENCODING_PCM_16BIT);
      AudioTrack.Builder builder = new AudioTrack.Builder().setAudioAttributes(new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_GAME)
          .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build())
          .setAudioFormat(new AudioFormat.Builder().setEncoding(AudioFormat.ENCODING_PCM_16BIT).setSampleRate(48000).setChannelMask(AudioFormat.CHANNEL_OUT_STEREO).build())
          .setBufferSizeInBytes(Math.max(minimum, 192*targetMs)).setTransferMode(AudioTrack.MODE_STREAM);
      if(Build.VERSION.SDK_INT>=26) builder.setPerformanceMode(AudioTrack.PERFORMANCE_MODE_LOW_LATENCY);
      track=builder.build();
      if(Build.VERSION.SDK_INT>=24) track.setBufferSizeInFrames(48*targetMs);
      s.actualBufferMs=track.getBufferSizeInFrames()/48;
      track.setVolume(volume); track.play(); long written=0, lastWrite=0; boolean flushed=false;
      while(alive(s) && !peer.isClosed()) {
        Sound sound=s.sound.poll(10,TimeUnit.MILLISECONDS); long now=SystemClock.elapsedRealtime();
        if(muted || sound==null) {
          if(!flushed && (muted || now-lastWrite>100)) { track.pause(); track.flush(); written=0; track.play(); flushed=true; }
          s.queuedMs=(int)Math.max(0,written-(track.getPlaybackHeadPosition()&0xffffffffL))/48;
          continue;
        }
        if(now-sound.received>targetMs) { s.audioDrops++; continue; }
        long queued=Math.max(0,written-(track.getPlaybackHeadPosition()&0xffffffffL));
        if(queued>48L*Math.max(targetMs,s.actualBufferMs)) { track.pause(); track.flush(); written=0; track.play(); s.audioDrops++; }
        track.setVolume(volume); int offset=0;
        while(offset<sound.bytes.length && alive(s) && !peer.isClosed() && !muted && SystemClock.elapsedRealtime()-sound.received<=targetMs) {
          int count=track.write(sound.bytes,offset,sound.bytes.length-offset,AudioTrack.WRITE_NON_BLOCKING);
          if(count<0) throw new IOException("Phone audio output stopped");
          offset+=count; written+=count/4;
          if(count==0) Thread.sleep(1);
        }
        if(offset<sound.bytes.length) s.audioDrops++;
        lastWrite=SystemClock.elapsedRealtime(); flushed=false;
        s.queuedMs=(int)Math.max(0,written-(track.getPlaybackHeadPosition()&0xffffffffL))/48;
      }
    } catch(Exception e) { if(alive(s) && !peer.isClosed()) status(s,"Audio unavailable · video and controls remain connected"); }
    finally { s.queuedMs=0; if(track!=null) { try { track.pause(); track.flush(); } catch(IllegalStateException ignored) { } track.release(); } }
  }
}
