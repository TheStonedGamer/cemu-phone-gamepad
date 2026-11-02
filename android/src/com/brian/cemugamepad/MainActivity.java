package com.brian.cemugamepad;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.res.Configuration;
import android.graphics.*;
import android.hardware.*;
import android.os.Bundle;
import android.os.Build;
import android.os.SystemClock;
import android.text.InputType;
import android.view.*;
import android.widget.*;
import android.content.pm.ActivityInfo;
import java.util.*;
import java.util.concurrent.atomic.AtomicReference;

public final class MainActivity extends Activity implements SensorEventListener {
  private final Controls state = new Controls();
  private PhoneConnection connection;
  private PadView pad;
  private SensorManager sensors;
  private String host, deviceId;
  private volatile String code = "";
  private boolean setupOpen;
  private volatile boolean connected;
  private boolean active, muted, disconnected, haptics, motion, touchMarker, screenOnly;
  private int volume, deadzone, audioBuffer;
  private boolean udpAudio;
  private final android.os.Handler ui = new android.os.Handler(android.os.Looper.getMainLooper());
  private final Runnable ticker = new Runnable() { public void run() { if(active) { pad.invalidate(); ui.postDelayed(this,500); } } };
  public void onCreate(Bundle saved) {
    super.onCreate(saved);
    setVolumeControlStream(android.media.AudioManager.STREAM_MUSIC);
    getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
    immersive();
    sensors = (SensorManager)getSystemService(SENSOR_SERVICE);
    pad = new PadView(); setContentView(pad);
    connection = new PhoneConnection(state, new PhoneConnection.Listener() {
      public void frame(Bitmap bitmap) { pad.frame(bitmap); }
      public void connected(boolean value) { connected = value; pad.postInvalidate(); }
      public void status(String value) { pad.message = value; pad.postInvalidate(); }
      public void paired(String address, String token) {
        boolean saved = getPreferences(0).edit().putString("token." + address, token).remove("code").commit();
        if (saved) code = "";
        else runOnUiThread(() -> Toast.makeText(MainActivity.this, "Pairing could not be saved. Check available phone storage.", Toast.LENGTH_LONG).show());
      }
    });
    host = getPreferences(0).getString("host", "");
    deviceId = getPreferences(0).getString("deviceId", "");
    if (!deviceId.matches("[a-f0-9]{32}")) {
      deviceId = UUID.randomUUID().toString().replace("-", "");
      getPreferences(0).edit().putString("deviceId", deviceId).commit();
    }
    loadSettings(); connection.mute(muted); applyAudio();
    pad.post(() -> { if (!canConnect()) setup(); });
  }
  private void immersive() { getWindow().getDecorView().setSystemUiVisibility(5894); }
  protected void onResume() {
    super.onResume(); active = true; immersive(); ui.removeCallbacks(ticker); ui.post(ticker);
    Sensor accel = sensors.getDefaultSensor(Sensor.TYPE_ACCELEROMETER), gyro = sensors.getDefaultSensor(Sensor.TYPE_GYROSCOPE);
    if (accel != null) sensors.registerListener(this, accel, SensorManager.SENSOR_DELAY_GAME);
    if (gyro != null) sensors.registerListener(this, gyro, SensorManager.SENSOR_DELAY_GAME);
    if (!setupOpen) reconnect();
  }
  protected void onPause() {
    active = false; ui.removeCallbacks(ticker); pad.clear(); connection.stop(); sensors.unregisterListener(this);
    synchronized (state) { state.accelTime = state.gyroTime = 0; }
    super.onPause();
  }
  public void onConfigurationChanged(Configuration config) { super.onConfigurationChanged(config); pad.clear(); synchronized(state) { state.accelTime = state.gyroTime = 0; } immersive(); }
  public void onWindowFocusChanged(boolean focused) { super.onWindowFocusChanged(focused); if (focused) immersive(); else if (pad != null) pad.clear(); }
  public void onBackPressed() { menu(); }
  protected void onDestroy() { ui.removeCallbacks(ticker); connection.stop(); pad.releaseFrames(); super.onDestroy(); }
  private int dp(int value) { return (int)(value*getResources().getDisplayMetrics().density+.5f); }
  private void loadSettings() {
    android.content.SharedPreferences p=getPreferences(0);
    muted=p.getBoolean("muted",false); haptics=p.getBoolean("haptics",true); motion=p.getBoolean("motion",true);
    touchMarker=p.getBoolean("touchMarker",true); screenOnly=p.getBoolean("screenOnly",false);
    volume=Math.max(0,Math.min(100,p.getInt("volume",80))); deadzone=Math.max(0,Math.min(30,p.getInt("deadzone",12)));
    audioBuffer=p.getInt("audioBuffer",30); if(audioBuffer!=20 && audioBuffer!=30 && audioBuffer!=60) audioBuffer=30;
    udpAudio=p.getBoolean("udpAudio",true);
  }
  private void saveSettings() {
    getPreferences(0).edit().putBoolean("muted",muted).putBoolean("haptics",haptics).putBoolean("motion",motion)
        .putBoolean("touchMarker",touchMarker).putBoolean("screenOnly",screenOnly).putInt("volume",volume).putInt("deadzone",deadzone)
        .putInt("audioBuffer",audioBuffer).putBoolean("udpAudio",udpAudio).apply();
    connection.mute(muted); applyAudio(); pad.invalidate();
  }
  private void applyAudio() { connection.configure(volume/100f,audioBuffer,udpAudio); }
  private void menu() {
    pad.clear();
    String[] items={"Connection & saved PCs", "Controller & audio settings", screenOnly?"Show controller buttons":"Screen only view", muted?"Unmute audio":"Mute audio", disconnected?"Reconnect":"Disconnect", "Help & about"};
    new AlertDialog.Builder(this).setTitle("GamePad menu").setItems(items,(d,which)->{
      if(which==0) setup();
      else if(which==1) preferences();
      else if(which==2) { screenOnly=!screenOnly; pad.clear(); saveSettings(); }
      else if(which==3) { muted=!muted; saveSettings(); }
      else if(which==4) { disconnected=!disconnected; if(disconnected) { connection.stop(); pad.message="Disconnected · open Menu to reconnect"; } else reconnect(); }
      else help();
    }).setNegativeButton("Back to game",null).show();
  }
  private void help() {
    String version=""; try {version=getPackageManager().getPackageInfo(getPackageName(),0).versionName;} catch(Exception ignored) { }
    new AlertDialog.Builder(this).setTitle("Cemu Phone GamePad " + version)
      .setMessage("CONNECT\nStart the Windows host, then enter its PC address and code in Connection once. Saved PCs reconnect automatically.\n\nCEMU CONTROLS\nInstall the supplied button mappings in the Windows host. In Cemu Input settings, select Controller 1 → CemuPhoneGamePad → Load.\n\nAUDIO DELAY\nUse Low delay (UDP) and phone speakers or wired headphones. Bluetooth adds output delay. Try the Fast buffer on a strong network; use Stable if audio crackles. Queue time is a phone buffer reading, not total latency.\n\nGAMEPAD SCREEN\nKeep Cemu’s separate GamePad View open and not minimized. Touch the image while holding buttons or sticks. Menu offers a larger screen only view.\n\nIndependent software · MIT license\nCamera, microphone, NFC and rumble are not forwarded. Use a trusted LAN; traffic is not encrypted.")
      .setPositiveButton("Done",null).setNeutralButton("Downloads",(d,w)->startActivity(new android.content.Intent(android.content.Intent.ACTION_VIEW,android.net.Uri.parse("https://github.com/TheStonedGamer/cemu-phone-gamepad/releases/latest")))).show();
  }
  private interface ValueChanged { void changed(int value); }
  private void slider(LinearLayout form,String title,int maximum,int value,ValueChanged change) {
    TextView label=new TextView(this); label.setText(title+": "+value+"%"); form.addView(label);
    SeekBar bar=new SeekBar(this); bar.setMax(maximum); bar.setProgress(value); form.addView(bar);
    bar.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
      public void onProgressChanged(SeekBar b,int n,boolean user) { label.setText(title+": "+n+"%"); if(user) change.changed(n); }
      public void onStartTrackingTouch(SeekBar b) { } public void onStopTrackingTouch(SeekBar b) { }
    });
  }
  private void preferences() {
    pad.clear();
    LinearLayout form=new LinearLayout(this); form.setOrientation(1); form.setPadding(dp(24),dp(12),dp(24),dp(12));
    TextView heading=new TextView(this); heading.setText("Settings are saved on this phone. Audio transport and buffer changes reconnect the session when you finish."); form.addView(heading);
    CheckBox feedback=new CheckBox(this); feedback.setText("Button haptics"); feedback.setChecked(haptics); form.addView(feedback);
    CheckBox sensor=new CheckBox(this); sensor.setText(sensors.getDefaultSensor(Sensor.TYPE_GYROSCOPE)==null?"Phone motion (no gyroscope available)":"Phone motion"); sensor.setChecked(motion); form.addView(sensor);
    CheckBox marker=new CheckBox(this); marker.setText("Show touchscreen contact"); marker.setChecked(touchMarker); form.addView(marker);
    slider(form,"Stick deadzone",30,deadzone,n->deadzone=n);
    slider(form,"Audio volume",100,volume,n->{volume=n; applyAudio();});
    TextView audioHeading=new TextView(this); audioHeading.setText("Audio transport"); form.addView(audioHeading);
    RadioGroup transport=new RadioGroup(this); RadioButton fast=new RadioButton(this); fast.setId(1001); fast.setText("Low delay · UDP (recommended)"); transport.addView(fast);
    RadioButton compat=new RadioButton(this); compat.setId(1002); compat.setText("Compatibility · TCP (if UDP is blocked)"); transport.addView(compat); transport.check(udpAudio?1001:1002); form.addView(transport);
    TextView bufferHeading=new TextView(this); bufferHeading.setText("Playback buffer"); form.addView(bufferHeading);
    RadioGroup buffers=new RadioGroup(this); String[] names={"Fast · 20 ms", "Balanced · 30 ms", "Stable · 60 ms"};
    for(int i=0;i<3;i++) { RadioButton b=new RadioButton(this); b.setId(1010+i); b.setText(names[i]); buffers.addView(b); }
    buffers.check(audioBuffer==20?1010:audioBuffer==60?1012:1011); form.addView(buffers);
    ScrollView scroll=new ScrollView(this); scroll.addView(form);
    AlertDialog dialog=new AlertDialog.Builder(this).setTitle("Controller & audio").setView(scroll).setPositiveButton("Done",null)
      .setNeutralButton("Reset defaults",(d,w)->{haptics=motion=touchMarker=udpAudio=true; deadzone=12; volume=80; audioBuffer=30; muted=false; saveSettings(); reconnect();}).create();
    dialog.setOnDismissListener(d->{
      haptics=feedback.isChecked(); motion=sensor.isChecked(); touchMarker=marker.isChecked();
      udpAudio=transport.getCheckedRadioButtonId()==1001;
      audioBuffer=buffers.getCheckedRadioButtonId()==1010?20:buffers.getCheckedRadioButtonId()==1012?60:30;
      if(!motion) synchronized(state) {state.accelTime=state.gyroTime=0;}
      saveSettings(); reconnect(); immersive();
    });
    dialog.show();
    // Reset is handled without dismissing first, so it cannot be overwritten by old widgets.
    dialog.getButton(-3).setOnClickListener(v->{feedback.setChecked(true);sensor.setChecked(true);marker.setChecked(true);transport.check(1001);buffers.check(1011);deadzone=12;volume=80;muted=false;haptics=motion=touchMarker=udpAudio=true;audioBuffer=30;saveSettings();dialog.dismiss();});
  }
  private String savedToken(String address) { return getPreferences(0).getString("token." + address, ""); }
  private boolean canConnect() { return host != null && !host.isEmpty() && (code.length() == 8 || !savedToken(host).isEmpty()); }
  private void reconnect() {
    if (active && !disconnected && canConnect()) connection.start(host, code, code.isEmpty() ? savedToken(host) : "", deviceId, Build.MANUFACTURER + " " + Build.MODEL);
  }
  private void setup() {
    if (setupOpen) return;
    setupOpen = true; pad.clear(); connection.stop();
    LinearLayout form = new LinearLayout(this); form.setOrientation(1); form.setPadding(dp(24), dp(12), dp(24), dp(12));
    TextView info = new TextView(this); info.setText("Pair once with the Windows host's code. Saved PCs reconnect after restarts without another code. Both devices must share a trusted network."); form.addView(info);
    ArrayList<String> savedHosts = new ArrayList<>();
    for (String key : getPreferences(0).getAll().keySet()) if (key.startsWith("token.")) savedHosts.add(key.substring(6));
    Collections.sort(savedHosts);
    AutoCompleteTextView address = new AutoCompleteTextView(this);
    address.setAdapter(new ArrayAdapter<String>(this, android.R.layout.simple_dropdown_item_1line, savedHosts));
    address.setThreshold(1); address.setHint("PC IPv4 address (saved PCs are suggested)");
    address.setSingleLine(); address.setText(host); address.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI); form.addView(address);
    EditText pairing = new EditText(this); pairing.setHint("Code — leave blank for a saved PC");
    pairing.setFilters(new android.text.InputFilter[] {new android.text.InputFilter.LengthFilter(8)});
    pairing.setSingleLine(); pairing.setInputType(InputType.TYPE_CLASS_NUMBER); form.addView(pairing);
    address.setOnItemClickListener((parent, view, position, id) -> pairing.setText(""));
    CheckBox mute = new CheckBox(this); mute.setText("Mute phone audio"); mute.setChecked(muted); form.addView(mute);
    ScrollView scroll = new ScrollView(this); scroll.addView(form);
    AlertDialog dialog = new AlertDialog.Builder(this).setTitle("Connect to your PC").setView(scroll)
        .setPositiveButton("Connect", null).setNegativeButton("Cancel", (d,w) -> reconnect())
        .setNeutralButton("Forget this PC", null).create();
    dialog.setOnCancelListener(d -> reconnect());
    dialog.setOnDismissListener(d -> { setupOpen = false; immersive(); });
    dialog.setOnShowListener(d -> {
      dialog.getButton(-3).setOnClickListener(v -> {
        String value = address.getText().toString().trim();
        getPreferences(0).edit().remove("token." + value).remove("code").apply();
        code = ""; pairing.setText(""); pairing.setHint("Enter this PC's current code to pair again");
        pad.message = "Pairing forgotten on phone; Windows Saved devices can revoke its entry";
      });
      dialog.getButton(-1).setOnClickListener(v -> {
        String value = address.getText().toString().trim(), secret = pairing.getText().toString().trim();
        if (!value.matches("[0-9]{1,3}(\\.[0-9]{1,3}){3}")) { address.setError("Enter a PC IPv4 address"); return; }
        String[] parts = value.split("\\.");
        for (String part : parts) if (Integer.parseInt(part)>255) { address.setError("Invalid IPv4 address"); return; }
        value = Integer.parseInt(parts[0])+"."+Integer.parseInt(parts[1])+"."+Integer.parseInt(parts[2])+"."+Integer.parseInt(parts[3]);
        if (!(secret.isEmpty() && !savedToken(value).isEmpty()) && !secret.matches("[0-9]{8}")) { pairing.setError("Enter an 8-digit code, or leave blank for a saved PC"); return; }
        disconnected = false; host = value; code = secret; muted = mute.isChecked(); connection.mute(muted);
        getPreferences(0).edit().putString("host", host).putBoolean("muted", muted).remove("code").apply();
        reconnect(); dialog.dismiss();
      });
    });
    dialog.show();
  }
  public void onAccuracyChanged(Sensor sensor, int accuracy) { }
  public void onSensorChanged(SensorEvent event) {
    if (!motion) return;
    float x = event.values[0], y = event.values[1], z = event.values[2];
    int rotation = getWindowManager().getDefaultDisplay().getRotation();
    float dx = rotation == Surface.ROTATION_90 ? -y : rotation == Surface.ROTATION_270 ? y : x;
    float dy = rotation == Surface.ROTATION_90 ? x : rotation == Surface.ROTATION_270 ? -x : y;
    long now = event.timestamp;
    synchronized(state) {
      if (event.sensor.getType() == Sensor.TYPE_ACCELEROMETER) {
        state.accel[0] = -dx / SensorManager.GRAVITY_EARTH;
        state.accel[1] = -z / SensorManager.GRAVITY_EARTH;
        state.accel[2] = dy / SensorManager.GRAVITY_EARTH; state.accelTime = now;
      } else {
        float degrees = (float)(180.0 / Math.PI);
        state.gyro[0] = dx * degrees; state.gyro[1] = -z * degrees; state.gyro[2] = dy * degrees; state.gyroTime = now;
      }
    }
  }
  private final class PadView extends View {
    private final Paint paint = new Paint(3);
    private final RectF screen = new RectF(300, 150, 980, 532);
    private final ArrayList<Control> controls = new ArrayList<>();
    private final LinkedHashMap<Integer, Contact> contacts = new LinkedHashMap<>();
    private final AtomicReference<Bitmap> pending = new AtomicReference<>();
    private Bitmap displayed;
    volatile String message = "Open Menu → Connection to connect";
    private float scale = 1, left, top;
    PadView() { super(MainActivity.this); setContentDescription("GamePad touchscreen, buttons and sticks. Press Back for the accessible settings menu."); setFocusable(true); layoutControls(); }
    void releaseFrames() { Bitmap old=pending.getAndSet(null); if(old!=null) old.recycle(); if(displayed!=null) {displayed.recycle();displayed=null;} }
    void frame(Bitmap bitmap) { Bitmap old = pending.getAndSet(bitmap); if (old != null) old.recycle(); postInvalidate(); }
    void clear() { contacts.clear(); state.clear(); postInvalidate(); }
    private void add(String label, float x, float y, float w, float h, int bit, int kind) { controls.add(new Control(label,new RectF(x,y,x+w,y+h),bit,kind)); }
    private void layoutControls() {
      add("ZL",25,75,110,55,8,0); add("L",150,75,110,55,10,0);
      add("R",1020,75,110,55,11,0); add("ZR",1145,75,110,55,9,0);
      add("−",500,75,80,50,0,0); add("+",700,75,80,50,3,0);
      add("HOME",585,585,110,50,16,0); add("L3",70,600,100,48,1,0); add("R3",1110,600,100,48,2,0);
      add("MENU",1080,12,170,46,-1,4);
      add("AUDIO",900,12,155,46,-1,5);
      add("VIEW",730,12,145,46,-1,6);
      add("LEFT",45,165,180,180,-1,1); add("RIGHT",1055,400,180,180,-1,2);
      add("D-PAD",45,390,180,180,-1,3);
      add("X",1110,165,70,70,12,0); add("Y",1035,240,70,70,15,0);
      add("A",1185,240,70,70,13,0); add("B",1110,315,70,70,14,0);
    }
    protected void onDraw(Canvas canvas) {
      Bitmap ready = pending.getAndSet(null);
      if (ready != null) { if (displayed != null) displayed.recycle(); displayed = ready; }
      scale = Math.min(getWidth()/1280f,getHeight()/720f); left=(getWidth()-1280*scale)/2; top=(getHeight()-720*scale)/2;
      canvas.drawColor(Color.rgb(8,15,25)); canvas.save(); canvas.translate(left,top); canvas.scale(scale,scale);
      paint.setColor(Color.rgb(17,28,42)); canvas.drawRoundRect(new RectF(8,5,1272,710),40,40,paint);
      text(canvas,"CEMU",30,43,28,Color.rgb(67,217,189));
      text(canvas,"PHONE GAMEPAD",125,43,21,Color.WHITE);
      paint.setColor(connected?Color.rgb(67,217,189):Color.rgb(242,180,85)); canvas.drawCircle(370,35,6,paint);
      text(canvas,connected?"CONNECTED":disconnected?"OFFLINE":canConnect()?"CONNECTING":"NOT PAIRED",388,42,17,Color.rgb(167,191,210));
      if(screenOnly) { float width=880; screen.set((1280-width)/2,85,(1280+width)/2,580); }
      else screen.set(300,150,980,532);
      paint.setColor(Color.BLACK); canvas.drawRoundRect(new RectF(screen.left-10,screen.top-10,screen.right+10,screen.bottom+10),18,18,paint);
      if (displayed != null) canvas.drawBitmap(displayed,null,screen,paint);
      else {
        text(canvas,"Your GamePad screen",screen.centerX(),screen.centerY()-20,28,Color.WHITE,true);
        text(canvas,"Menu → Connection to get started",screen.centerX(),screen.centerY()+22,21,Color.LTGRAY,true);
      }
      if(displayed!=null && !connected) { paint.setColor(0x99080f19); canvas.drawRect(screen,paint); text(canvas,"Waiting for connection",screen.centerX(),screen.centerY(),24,Color.WHITE,true); }
      synchronized(state) { for (Control control: controls) {
        if(screenOnly && control.kind<4) continue;
        boolean down = control.bit >= 0 && (state.buttons & (1 << control.bit)) != 0;
        paint.setColor(down ? Color.rgb(32,144,126) : Color.rgb(32,49,68));
        RectF r=control.rect;
        if (control.kind==1 || control.kind==2) {
          canvas.drawOval(r,paint); paint.setColor(Color.rgb(80,111,136));
          float ax=control.kind==1?state.lx:state.rx, ay=control.kind==1?state.ly:state.ry;
          canvas.drawCircle(r.centerX()+ax*52,r.centerY()-ay*52,42,paint);
        } else if (control.kind==3) {
          canvas.drawRoundRect(new RectF(r.left+60,r.top,r.right-60,r.bottom),10,10,paint);
          canvas.drawRoundRect(new RectF(r.left,r.top+60,r.right,r.bottom-60),10,10,paint);
        } else {
          if(control.label.equals("A") || control.label.equals("B") || control.label.equals("X") || control.label.equals("Y")) {
            canvas.drawOval(r,paint);
            int color=control.label.equals("A")?Color.rgb(111,231,184):control.label.equals("B")?Color.rgb(255,143,148):control.label.equals("X")?Color.rgb(135,190,255):Color.rgb(255,216,128);
            text(canvas,control.label,r.centerX(),r.centerY()+9,28,color,true);
          } else {
            canvas.drawRoundRect(r,12,12,paint);
            text(canvas,control.kind==5?(muted?"UNMUTE":"AUDIO ON"):control.kind==6?(screenOnly?"BUTTONS":"SCREEN"):control.label,r.centerX(),r.centerY()+7,20,Color.WHITE,true);
          }
        }
      } }
      if(touchMarker && state.touch) { paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(2); paint.setColor(Color.rgb(67,217,189)); canvas.drawCircle(screen.left+state.tx*screen.width(),screen.top+state.ty*screen.height(),11,paint); paint.setStyle(Paint.Style.FILL); }
      float statusY=screenOnly?615:570;
      paint.setTextSize(18); String notice=android.text.TextUtils.ellipsize(message, new android.text.TextPaint(paint),screenOnly?1150:680,android.text.TextUtils.TruncateAt.END).toString();
      text(canvas,notice,screenOnly?65:300,statusY,18,Color.rgb(167,191,210));
      String[] stats=connection.statistics().split("\n");
      text(canvas,stats[0],640,662,18,Color.rgb(119,148,172),true);
      if(stats.length>1) text(canvas,stats[1],640,690,17,Color.rgb(119,148,172),true);
      canvas.restore();
    }
    private void text(Canvas c,String value,float x,float y,float size,int color) { text(c,value,x,y,size,color,false); }
    private void text(Canvas c,String value,float x,float y,float size,int color,boolean centered) {
      paint.setColor(color); paint.setTextSize(size); paint.setTextAlign(centered?Paint.Align.CENTER:Paint.Align.LEFT); c.drawText(value,x,y,paint);
    }
    public boolean onTouchEvent(MotionEvent event) {
      int action=event.getActionMasked(), index=event.getActionIndex();
      if (action==MotionEvent.ACTION_CANCEL) { clear(); return true; }
      if (action==MotionEvent.ACTION_DOWN || action==MotionEvent.ACTION_POINTER_DOWN) {
        float x=(event.getX(index)-left)/scale,y=(event.getY(index)-top)/scale;
        Control found=null;
        for (Control c:controls) if((!screenOnly || c.kind>=4) && c.rect.contains(x,y)) { found=c; break; }
        if(found!=null && found.kind>=4) {
          clear(); if(found.kind==4) menu(); else if(found.kind==5) {muted=!muted;saveSettings();} else {screenOnly=!screenOnly;saveSettings();}
          return true;
        }
        if(found!=null && found.kind==0 && haptics) performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP);
        if(found!=null || screen.contains(x,y)) contacts.put(event.getPointerId(index),new Contact(found,x,y));
      }
      for(int i=0;i<event.getPointerCount();i++) {
        Contact contact=contacts.get(event.getPointerId(i));
        if(contact!=null) { contact.x=(event.getX(i)-left)/scale; contact.y=(event.getY(i)-top)/scale; }
      }
      if(action==MotionEvent.ACTION_UP || action==MotionEvent.ACTION_POINTER_UP) contacts.remove(event.getPointerId(index));
      synchronized(state) {
        state.clear();
        boolean hasTouch=false;
        for(Contact contact:contacts.values()) {
          Control c=contact.control;
          if(c==null) { if(hasTouch) continue; hasTouch=true; state.touch=true; state.tx=clamp((contact.x-screen.left)/screen.width(),0,1); state.ty=clamp((contact.y-screen.top)/screen.height(),0,1); }
          else if(c.kind==1 || c.kind==2) {
            float x=(contact.x-c.rect.centerX())/72, y=(c.rect.centerY()-contact.y)/72;
            float length=(float)Math.sqrt(x*x+y*y); float zone=deadzone/100f;
            if(length<=zone) x=y=0; else { float magnitude=(Math.min(length,1)-zone)/(1-zone); x=x/length*magnitude; y=y/length*magnitude; }
            if(c.kind==1) { state.lx=x; state.ly=y; } else { state.rx=x; state.ry=y; }
          } else if(c.kind==3) {
            float dx=contact.x-c.rect.centerX(),dy=contact.y-c.rect.centerY();
            if(dx < -25) state.buttons|=1<<7; if(dx>25) state.buttons|=1<<5;
            if(dy < -25) state.buttons|=1<<4; if(dy>25) state.buttons|=1<<6;
          } else if(c.bit>=0 && c.rect.contains(contact.x,contact.y)) state.buttons|=1<<c.bit;
        }
      }
      invalidate(); return true;
    }
  }
  private static float clamp(float v,float min,float max) { return Math.max(min,Math.min(max,v)); }
  private static final class Control { final String label; final RectF rect; final int bit,kind; Control(String label,RectF rect,int bit,int kind) { this.label=label;this.rect=rect;this.bit=bit;this.kind=kind; } }
  private static final class Contact { final Control control; float x,y; Contact(Control c,float x,float y) {control=c;this.x=x;this.y=y;} }
}
