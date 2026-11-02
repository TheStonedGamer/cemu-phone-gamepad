package com.brian.cemugamepad;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.res.Configuration;
import android.graphics.*;
import android.hardware.*;
import android.os.Bundle;
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
  private String host, code;
  private boolean active, muted;
  public void onCreate(Bundle saved) {
    super.onCreate(saved);
    getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
    immersive();
    sensors = (SensorManager)getSystemService(SENSOR_SERVICE);
    pad = new PadView(); setContentView(pad);
    connection = new PhoneConnection(state, new PhoneConnection.Listener() {
      public void frame(Bitmap bitmap) { pad.frame(bitmap); }
      public void status(String value) { pad.message = value; pad.postInvalidate(); }
    });
    host = getPreferences(0).getString("host", "");
    code = getPreferences(0).getString("code", "");
    pad.post(this::setup);
  }
  private void immersive() { getWindow().getDecorView().setSystemUiVisibility(5894); }
  protected void onResume() {
    super.onResume(); active = true; immersive();
    Sensor accel = sensors.getDefaultSensor(Sensor.TYPE_ACCELEROMETER), gyro = sensors.getDefaultSensor(Sensor.TYPE_GYROSCOPE);
    if (accel != null) sensors.registerListener(this, accel, SensorManager.SENSOR_DELAY_GAME);
    if (gyro != null) sensors.registerListener(this, gyro, SensorManager.SENSOR_DELAY_GAME);
    if (!host.isEmpty() && code.length() == 8) connection.start(host, code);
  }
  protected void onPause() {
    active = false; pad.clear(); connection.stop(); sensors.unregisterListener(this);
    synchronized (state) { state.accelTime = state.gyroTime = 0; }
    super.onPause();
  }
  public void onConfigurationChanged(Configuration config) { super.onConfigurationChanged(config); pad.clear(); synchronized(state) { state.accelTime = state.gyroTime = 0; } immersive(); }
  public void onWindowFocusChanged(boolean focused) { super.onWindowFocusChanged(focused); if (focused) immersive(); else if (pad != null) pad.clear(); }
  public void onBackPressed() { setup(); }
  private void setup() {
    pad.clear(); connection.stop();
    LinearLayout form = new LinearLayout(this); form.setOrientation(1); form.setPadding(32, 10, 32, 10);
    TextView info = new TextView(this); info.setText("Start the Windows host, select Cemu's GamePad View, then enter its address and 8-digit pairing code. Both devices must share a trusted network."); form.addView(info);
    EditText address = new EditText(this); address.setHint("PC IPv4 address"); address.setSingleLine(); address.setText(host); address.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI); form.addView(address);
    EditText pairing = new EditText(this); pairing.setHint("8-digit pairing code"); pairing.setText(code); pairing.setSingleLine(); pairing.setInputType(InputType.TYPE_CLASS_NUMBER); form.addView(pairing);
    CheckBox mute = new CheckBox(this); mute.setText("Mute phone audio"); mute.setChecked(muted); form.addView(mute);
    AlertDialog dialog = new AlertDialog.Builder(this).setTitle("Cemu Phone GamePad").setView(form)
        .setPositiveButton("Connect", null).setNegativeButton("Cancel", (d,w) -> { if (active && !host.isEmpty() && code.length()==8) connection.start(host, code); }).create();
    dialog.setOnCancelListener(d -> { if (active && !host.isEmpty() && code.length()==8) connection.start(host, code); });
    dialog.setOnShowListener(d -> dialog.getButton(-1).setOnClickListener(v -> {
      String value = address.getText().toString().trim(), secret = pairing.getText().toString().trim();
      if (!value.matches("[0-9]{1,3}(\\.[0-9]{1,3}){3}") || !secret.matches("[0-9]{8}")) { pairing.setError("Enter a PC IPv4 address and 8-digit code"); return; }
      for (String part : value.split("\\.")) if (Integer.parseInt(part)>255) { address.setError("Invalid IPv4 address"); return; }
      host = value; code = secret; muted = mute.isChecked(); connection.mute(muted);
      getPreferences(0).edit().putString("host", host).putString("code", code).apply();
      if (active) connection.start(host, code); dialog.dismiss(); immersive();
    })); dialog.show();
  }
  public void onAccuracyChanged(Sensor sensor, int accuracy) { }
  public void onSensorChanged(SensorEvent event) {
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
    private final HashMap<Integer, Contact> contacts = new HashMap<>();
    private final AtomicReference<Bitmap> pending = new AtomicReference<>();
    private Bitmap displayed;
    volatile String message = "Open SETUP to connect";
    private float scale = 1, left, top;
    PadView() { super(MainActivity.this); layoutControls(); }
    void frame(Bitmap bitmap) { Bitmap old = pending.getAndSet(bitmap); if (old != null) old.recycle(); postInvalidate(); }
    void clear() { contacts.clear(); state.clear(); postInvalidate(); }
    private void add(String label, float x, float y, float w, float h, int bit, int kind) { controls.add(new Control(label,new RectF(x,y,x+w,y+h),bit,kind)); }
    private void layoutControls() {
      add("ZL",25,75,110,55,8,0); add("L",150,75,110,55,10,0);
      add("R",1020,75,110,55,11,0); add("ZR",1145,75,110,55,9,0);
      add("−",500,75,80,50,0,0); add("+",700,75,80,50,3,0);
      add("HOME",585,585,110,50,16,0); add("L3",70,600,100,48,1,0); add("R3",1110,600,100,48,2,0);
      add("SETUP",1080,10,170,44,-1,4);
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
      paint.setColor(Color.rgb(15,27,43)); canvas.drawRoundRect(new RectF(8,5,1272,710),40,40,paint);
      text(canvas,"CEMU PHONE GAMEPAD",30,42,25,Color.rgb(67,217,189));
      paint.setColor(Color.BLACK); canvas.drawRoundRect(new RectF(290,140,990,542),18,18,paint);
      if (displayed != null) canvas.drawBitmap(displayed,null,screen,paint);
      else text(canvas,"Cemu's GamePad screen will appear here",345,345,24,Color.LTGRAY);
      synchronized(state) { for (Control control: controls) {
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
          canvas.drawRoundRect(r,20,20,paint); text(canvas,control.label,r.centerX(),r.centerY()+8,24,Color.WHITE,true);
        }
      } }
      text(canvas,message,300,570,19,Color.rgb(167,191,210));
      text(canvas,"Touch screen • Multi-touch controls • Phone motion",385,676,20,Color.rgb(119,148,172));
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
        for (Control c:controls) if(c.rect.contains(x,y)) { found=c; break; }
        if(found!=null && found.kind==4) { setup(); return true; }
        if(found!=null || screen.contains(x,y)) contacts.put(event.getPointerId(index),new Contact(found,x,y));
      }
      for(int i=0;i<event.getPointerCount();i++) {
        Contact contact=contacts.get(event.getPointerId(i));
        if(contact!=null) { contact.x=(event.getX(i)-left)/scale; contact.y=(event.getY(i)-top)/scale; }
      }
      if(action==MotionEvent.ACTION_UP || action==MotionEvent.ACTION_POINTER_UP) contacts.remove(event.getPointerId(index));
      synchronized(state) {
        state.clear();
        for(Contact contact:contacts.values()) {
          Control c=contact.control;
          if(c==null) { state.touch=true; state.tx=clamp((contact.x-screen.left)/screen.width(),0,1); state.ty=clamp((contact.y-screen.top)/screen.height(),0,1); }
          else if(c.kind==1 || c.kind==2) {
            float x=(contact.x-c.rect.centerX())/72, y=(c.rect.centerY()-contact.y)/72;
            float length=(float)Math.sqrt(x*x+y*y); if(length<.12f) x=y=0; else if(length>1) { x/=length; y/=length; }
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
