package com.brian.cemugamepad;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.media.AudioAttributes;
import android.media.AudioFormat;
import android.media.AudioTrack;
import java.io.DataInputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

final class PhoneConnection {
  interface Listener { void frame(Bitmap bitmap); void status(String text); }
  private final Controls controls;
  private final Listener listener;
  private volatile boolean running;
  private volatile long generation;
  private volatile Socket socket;
  private Thread worker;
  private final AtomicReference<byte[]> nextVideo = new AtomicReference<>();
  private final ArrayBlockingQueue<byte[]> sound = new ArrayBlockingQueue<>(4);
  private Thread decoder, audio;
  private volatile boolean muted;
  PhoneConnection(Controls controls, Listener listener) { this.controls = controls; this.listener = listener; }
  void mute(boolean value) { muted = value; sound.clear(); }
  synchronized void start(String host, String code) {
    stop(); running = true; long ticket = ++generation;
    worker = new Thread(() -> run(host, code, ticket), "gamepad-network"); worker.start();
  }
  synchronized void stop() {
    running = false; generation++; controls.clear();
    try { if (socket != null) socket.close(); } catch (Exception ignored) { }
    if (worker != null) { try { worker.join(3000); } catch (InterruptedException ignored) { Thread.currentThread().interrupt(); } }
    worker = null; socket = null; nextVideo.set(null); sound.clear();
  }
  private void run(String host, String code, long ticket) {
    decoder = new Thread(() -> decode(ticket), "gamepad-video"); decoder.start();
    audio = new Thread(() -> playAudio(ticket), "gamepad-audio"); audio.start();
    Thread sender = null;
    while (running && ticket == generation) {
      try (Socket peer = new Socket()) {
        socket = peer;
        listener.status("Connecting to " + host);
        peer.connect(new InetSocketAddress(host, 26762), 4000);
        peer.setTcpNoDelay(true); peer.setSoTimeout(5000);
        OutputStream output = peer.getOutputStream();
        output.write(("PGP1" + code).getBytes(StandardCharsets.US_ASCII)); output.flush();
        sender = new Thread(() -> send(peer, output, ticket), "gamepad-input"); sender.start();
        DataInputStream input = new DataInputStream(peer.getInputStream());
        boolean announced = false;
        while (running && ticket == generation && !peer.isClosed()) {
          int type = input.readUnsignedByte();
          int length = input.readInt();
          if (length < 0 || length > 2000000) throw new java.io.IOException("Invalid host frame");
          byte[] payload = new byte[length]; input.readFully(payload);
          if (!announced) { listener.status("Connected — GamePad screen, touch and motion"); announced = true; }
          if (type == 1) nextVideo.set(payload);
          else if (type == 2 && !muted && length % 4 == 0) { if (!sound.offer(payload)) { sound.poll(); sound.offer(payload); } }
          else if (type == 3) listener.status(new String(payload, StandardCharsets.UTF_8));
        }
      } catch (Exception e) {
        if (running) listener.status("Reconnecting — check host, pairing code and Wi-Fi");
      } finally {
        controls.clear(); nextVideo.set(null); sound.clear();
        try { if (socket != null) socket.close(); } catch (Exception ignored) { }
        if (sender != null) { sender.interrupt(); try { sender.join(1000); } catch (InterruptedException ignored) { Thread.currentThread().interrupt(); } sender = null; }
      }
      if (running) try { Thread.sleep(1000); } catch (InterruptedException ignored) { }
    }
    if (decoder != null) decoder.interrupt(); if (audio != null) audio.interrupt();
    try { if (decoder != null) decoder.join(1000); if (audio != null) audio.join(1000); } catch (InterruptedException ignored) { Thread.currentThread().interrupt(); }
  }
  private void send(Socket peer, OutputStream output, long ticket) {
    int sequence = 0;
    try {
      while (running && ticket == generation && !peer.isClosed()) {
        output.write(controls.packet(sequence++)); output.flush(); Thread.sleep(16);
      }
    } catch (Exception ignored) { try { peer.close(); } catch (Exception ignoredClose) { } }
  }
  private void decode(long ticket) {
    while (running && ticket == generation) {
      byte[] data = nextVideo.getAndSet(null);
      if (data == null) { try { Thread.sleep(3); } catch (InterruptedException ignored) { } continue; }
      BitmapFactory.Options bounds = new BitmapFactory.Options(); bounds.inJustDecodeBounds = true;
      BitmapFactory.decodeByteArray(data, 0, data.length, bounds);
      if (bounds.outWidth < 1 || bounds.outHeight < 1 || bounds.outWidth > 1920 || bounds.outHeight > 1080) continue;
      Bitmap bitmap = BitmapFactory.decodeByteArray(data, 0, data.length);
      if (bitmap != null) listener.frame(bitmap);
    }
  }
  private void playAudio(long ticket) {
    AudioTrack track = null;
    try {
      int minimum = AudioTrack.getMinBufferSize(48000, AudioFormat.CHANNEL_OUT_STEREO, AudioFormat.ENCODING_PCM_16BIT);
      track = new AudioTrack.Builder().setAudioAttributes(new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_GAME)
          .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build())
          .setAudioFormat(new AudioFormat.Builder().setEncoding(AudioFormat.ENCODING_PCM_16BIT).setSampleRate(48000)
          .setChannelMask(AudioFormat.CHANNEL_OUT_STEREO).build()).setBufferSizeInBytes(Math.max(minimum, 7680))
          .setTransferMode(AudioTrack.MODE_STREAM).build();
      track.play();
      while (running && ticket == generation) {
        byte[] data = sound.poll(200, TimeUnit.MILLISECONDS);
        if (data != null && !muted) track.write(data, 0, data.length, AudioTrack.WRITE_BLOCKING);
      }
    } catch (Exception e) { if (running) listener.status("Audio unavailable; video and controls remain active"); }
    finally { if (track != null) { try { track.stop(); } catch (IllegalStateException ignored) { } track.release(); } }
  }
}
