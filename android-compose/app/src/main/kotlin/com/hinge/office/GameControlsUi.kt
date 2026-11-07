package com.hinge.office

import android.graphics.*
import android.view.MotionEvent
import android.view.View
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlin.math.*

fun gameKeyName(key:Int):String=when(key){0->"未设置";in 48..90->key.toChar().toString();in 112..135->"F${key-111}";in 96..105->"数字小键盘 ${key-96}";else->mapOf(-1 to "鼠标左键",-2 to "鼠标右键",-3 to "鼠标中键",8 to "退格",9 to "Tab",13 to "Enter",16 to "Shift",17 to "Ctrl",18 to "Alt",19 to "Pause",20 to "Caps Lock",27 to "Esc",32 to "Space",33 to "Page Up",34 to "Page Down",35 to "End",36 to "Home",37 to "←",38 to "↑",39 to "→",40 to "↓",44 to "Print Screen",45 to "Insert",46 to "Delete",91 to "左 Win",92 to "右 Win",106 to "小键盘 *",107 to "小键盘 +",109 to "小键盘 -",110 to "小键盘 .",111 to "小键盘 /",144 to "Num Lock",160 to "左 Shift",161 to "右 Shift",162 to "左 Ctrl",163 to "右 Ctrl",164 to "左 Alt",165 to "右 Alt",186 to ";",187 to "=",188 to ",",189 to "-",190 to ".",191 to "/",192 to "`",219 to "[",220 to "\\",221 to "]",222 to "'")[key]?:"VK $key"}
fun joystickKeys(dx:Float,dy:Float,directions:List<Int>):Set<Int>{
    if(hypot(dx,dy)<.18f)return emptySet()
    val axis=max(abs(dx),abs(dy))*.42f
    return buildSet{if(dy< -axis)add(directions[0]);if(dx< -axis)add(directions[1]);if(dy>axis)add(directions[2]);if(dx>axis)add(directions[3])}.filter{it!=0}.toSet()
}
fun gameControlRadius(control:GameControl,width:Float,height:Float):Float=when{
    control.joystick||control.shape=="circle"->min(width,height)/2
    control.shape=="rect"->0f
    control.shape=="rounded"->min(width,height)*control.cornerRadius
    abs(control.aspect-1)<.02f->height/2
    else->18f
}
fun runtimeControlRect(control:GameControl,width:Float,height:Float,desktop:Boolean):ControlRect{
    val rect=controlRect(control,width,height)
    fun overlaps(left:Float,right:Float)=rect.y<56&&rect.y+rect.height>0&&rect.x<right&&rect.x+rect.width>left
    return if(if(desktop)overlaps(width/2-56,width/2+56) else overlaps(width/2-28,width/2+28)||overlaps(width-64,width))rect.copy(y=60f.coerceAtMost(max(0f,height-rect.height)))else rect
}

@Composable fun ControlImage(control:GameControl,modifier:Modifier=Modifier,scale:Float=1f){
    val bitmap by produceState<Bitmap?>(null,control.image){value=withContext(Dispatchers.IO){runCatching{val path=control.image?:return@runCatching null;val bounds=BitmapFactory.Options().apply{inJustDecodeBounds=true};BitmapFactory.decodeFile(path,bounds);BitmapFactory.decodeFile(path,BitmapFactory.Options().apply{inSampleSize=max(1,max(bounds.outWidth,bounds.outHeight)/256)})}.getOrNull()}}
    if(bitmap!=null)Image(bitmap!!.asImageBitmap(),control.label,modifier,contentScale=ContentScale.Fit)else Text(control.label.ifEmpty{gameKeyName(control.binding)},modifier,maxLines=2,textAlign=androidx.compose.ui.text.style.TextAlign.Center,overflow=androidx.compose.ui.text.style.TextOverflow.Ellipsis,style=MaterialTheme.typography.titleMedium.copy(fontSize=MaterialTheme.typography.titleMedium.fontSize*scale,lineHeight=MaterialTheme.typography.titleMedium.lineHeight*scale))
}

@Composable fun GameOverlay(model:HingeModel,remote:RemoteController,preset:GamePreset,desktop:Boolean,options:DesktopOptions){
    val enabled=remote.ready&&remote.supportsGameKeys&&!options.viewOnly&&!options.actions&&!options.presetPicker&&!options.gamePreferences&&!options.editing&&!options.systemPending
    val colors=MaterialTheme.colorScheme
    val labelWeight=MaterialTheme.typography.titleMedium.fontWeight?.weight?:600
    val directionWeight=MaterialTheme.typography.labelSmall.fontWeight?.weight?:600
    BoxWithConstraints(Modifier.fillMaxSize().then(if(!desktop)Modifier.background(colors.surface)else Modifier)){
        val width=maxWidth.value;val height=maxHeight.value
        if(!desktop||options.gameCamera)AndroidView(factory={GameCameraView(it,remote)},modifier=Modifier.fillMaxSize(),update={it.inputEnabled=enabled;it.sensitivity=options.sensitivity})
        if(!desktop)Text("单指滑动移动视角 · 双指按住并移动可拖拽（鼠标左键）",Modifier.align(Alignment.Center),style=MaterialTheme.typography.bodySmall,color=colors.onSurfaceVariant)
        preset.controls.forEach{control->key(control.id){
            val rect=runtimeControlRect(control,width,height,desktop)
            val bitmap by produceState<Bitmap?>(null,control.image){value=withContext(Dispatchers.IO){runCatching{val path=control.image?:return@runCatching null;val bounds=BitmapFactory.Options().apply{inJustDecodeBounds=true};BitmapFactory.decodeFile(path,bounds);BitmapFactory.decodeFile(path,BitmapFactory.Options().apply{inSampleSize=max(1,max(bounds.outWidth,bounds.outHeight)/256)})}.getOrNull()}}
            AndroidView(factory={GameButtonView(it,remote,model.activity::suppressTapFeedback)},modifier=Modifier.offset(rect.x.dp,rect.y.dp).size(rect.width.dp,rect.height.dp),update={
                it.update(control,enabled,bitmap,if(desktop)options.opacity else 1f,options.sensitivity,options.gameHaptics,labelWeight,directionWeight,intArrayOf(colors.surfaceContainerHighest.toArgb(),colors.primaryContainer.toArgb(),colors.primary.toArgb(),colors.outlineVariant.toArgb(),colors.onSurface.toArgb(),colors.onPrimaryContainer.toArgb(),colors.onPrimary.toArgb()))
            })
        }}
    }
}

private class GameCameraView(context:android.content.Context,private val remote:RemoteController):View(context){
    var inputEnabled=false;var sensitivity=3f
    private val points=mutableMapOf<Int,Pair<Float,Float>>()
    private var remainderX=0f;private var remainderY=0f
    init{isClickable=true;contentDescription="游戏视角触控区域"}
    override fun performClick():Boolean{super.performClick();return true}
    override fun onTouchEvent(event:MotionEvent):Boolean{
        if(!inputEnabled){points.clear();return true}
        when(event.actionMasked){
            MotionEvent.ACTION_DOWN,MotionEvent.ACTION_POINTER_DOWN->{val i=event.actionIndex;points[event.getPointerId(i)]=event.getX(i) to event.getY(i)}
            MotionEvent.ACTION_MOVE->{var dx=0f;var dy=0f;var count=0;for(i in 0 until event.pointerCount){val id=event.getPointerId(i);points[id]?.let{dx+=event.getX(i)-it.first;dy+=event.getY(i)-it.second;count++};points[id]=event.getX(i) to event.getY(i)};if(count>0){remainderX+=dx/count*sensitivity/resources.displayMetrics.density;remainderY+=dy/count*sensitivity/resources.displayMetrics.density;val x=remainderX.toInt();val y=remainderY.toInt();remainderX-=x;remainderY-=y;if(x!=0||y!=0)remote.input(Wire.input(1,flags=4,x=x,y=y))}}
            MotionEvent.ACTION_UP,MotionEvent.ACTION_POINTER_UP->{points.remove(event.getPointerId(event.actionIndex));if(event.actionMasked==MotionEvent.ACTION_UP)performClick()}
            MotionEvent.ACTION_CANCEL->points.clear()
        };return true
    }
}

private class GameButtonView(
    context: android.content.Context,
    private val remote: RemoteController,
    private val suppressTapFeedback: () -> Unit,
) : View(context) {
    private var control=GameControl();private var enabledInput=false;private var bitmap:Bitmap?=null;private var opacity=1f;private var sensitivity=3f;private var haptic=true
    private var colors=IntArray(7);private val paint=Paint(Paint.ANTI_ALIAS_FLAG);private val active=mutableSetOf<Int>();private var knobX=0f;private var knobY=0f;private var pressed=false;private var pointer=-1;private var lastX=0f;private var lastY=0f
    private var remainderX=0f;private var remainderY=0f
    private var labelWeight=600;private var directionWeight=600;private var pressProgress=0f
    private var pressAnimator:android.animation.ValueAnimator?=null
    private var textLayout:android.text.StaticLayout?=null;private var layoutKey:List<Any>?=null
    init{isClickable=true}
    fun update(value:GameControl,enabled:Boolean,image:Bitmap?,alpha:Float,speed:Float,feedback:Boolean,textWeight:Int,smallWeight:Int,palette:IntArray){
        if((!enabled&&enabledInput)||value!=control)release()
        control=value;enabledInput=enabled;bitmap=image;opacity=alpha;sensitivity=speed;haptic=feedback;labelWeight=textWeight;directionWeight=smallWeight;colors=palette;this.alpha=if(enabled)1f else .45f;contentDescription=value.label.ifEmpty{gameKeyName(value.binding)};invalidate()
    }
    private fun setPressedVisual(value:Boolean){
        if(pressed==value)return
        pressed=value;pressAnimator?.cancel()
        pressAnimator=android.animation.ValueAnimator.ofFloat(pressProgress,if(value)1f else 0f).apply{duration=65;interpolator=android.view.animation.LinearInterpolator();addUpdateListener{pressProgress=it.animatedValue as Float;invalidate()};start()}
    }
    private fun release(){active.toList().forEach{remote.key(it,false)};active.clear();setPressedVisual(false);knobX=0f;knobY=0f;pointer=-1;invalidate()}
    private fun applyKeys(keys:Set<Int>){(active-keys).forEach{remote.key(it,false)};(keys-active).forEach{remote.key(it,true)};active.clear();active.addAll(keys)}
    private fun label(canvas:Canvas,label:String,x:Float,y:Float,width:Float,height:Float,color:Int,size:Float){
        bitmap?.let{val scale=min(width/it.width,height/it.height);val w=it.width*scale;val h=it.height*scale;paint.color=android.graphics.Color.WHITE;paint.style=Paint.Style.FILL;canvas.drawBitmap(it,null,RectF(x-w/2,y-h/2,x+w/2,y+h/2),paint);return}
        val key=listOf(label,width.toInt(),size,resources.displayMetrics.scaledDensity,labelWeight,color)
        if(key!=layoutKey){
            val textPaint=android.text.TextPaint(Paint.ANTI_ALIAS_FLAG).apply{this.color=color;textSize=size*resources.displayMetrics.scaledDensity;typeface=font(labelWeight)}
            textLayout=android.text.StaticLayout.Builder.obtain(label,0,label.length,textPaint,max(1,width.toInt())).setAlignment(android.text.Layout.Alignment.ALIGN_CENTER).setIncludePad(false).setMaxLines(2).setEllipsize(android.text.TextUtils.TruncateAt.END).setLineSpacing(textPaint.textSize*1.5f-(textPaint.fontMetrics.descent-textPaint.fontMetrics.ascent),1f).build()
            layoutKey=key
        }
        textLayout?.let{canvas.save();canvas.translate(x-it.width/2f,y-it.height/2f);it.draw(canvas);canvas.restore()}
    }
    private fun font(weight:Int)=if(android.os.Build.VERSION.SDK_INT>=28)Typeface.create(Typeface.SANS_SERIF,weight,false)else Typeface.create(if(weight in 450..600)"sans-serif-medium" else "sans-serif",if(weight>600)Typeface.BOLD else Typeface.NORMAL)
    override fun onDraw(canvas:Canvas){
        val density=resources.displayMetrics.density;val radius=gameControlRadius(control,width/density,height/density)*density
        paint.style=Paint.Style.FILL;paint.color=androidx.core.graphics.ColorUtils.blendARGB(colors[0],colors[1],pressProgress);paint.alpha=(opacity*255).toInt();canvas.drawRoundRect(0f,0f,width.toFloat(),height.toFloat(),radius,radius,paint)
        paint.alpha=255;paint.style=Paint.Style.STROKE;paint.strokeWidth=density;paint.color=androidx.core.graphics.ColorUtils.blendARGB(colors[3],colors[2],pressProgress);canvas.drawRoundRect(.5f*density,.5f*density,width-.5f*density,height-.5f*density,max(0f,radius-.5f*density),max(0f,radius-.5f*density),paint);paint.style=Paint.Style.FILL
        if(control.joystick){
            paint.textSize=11*resources.displayMetrics.scaledDensity;paint.typeface=font(directionWeight);paint.letterSpacing=.5f/11f;paint.textAlign=Paint.Align.CENTER;paint.color=colors[4]
            control.directions.forEachIndexed{i,key->val text=gameKeyName(key);val dx=(width-paint.measureText(text))*.4f;val dy=(height-paint.textSize*1.45f)*.4f;val x=width/2f+if(i==1)-dx else if(i==3)dx else 0f;val y=height/2f+if(i==0)-dy else if(i==2)dy else 0f;canvas.drawText(text,x,y-(paint.ascent()+paint.descent())/2,paint)}
            paint.letterSpacing=0f;paint.color=colors[2];paint.alpha=(115+89*pressProgress).toInt();val x=width/2f+knobX*width*.28f;val y=height/2f+knobY*width*.28f;canvas.drawCircle(x,y,min(width,height)*.185f,paint);paint.alpha=255
            label(canvas,control.label,x,y,width*.37f,height*.37f,colors[6],16f)
        }else label(canvas,control.label.ifEmpty{gameKeyName(control.binding)},width/2f,height/2f,width-16*density,height-16*density,colors[if(pressed)5 else 4],16f)
    }
    override fun performClick():Boolean{super.performClick();return true}
    override fun onTouchEvent(event:MotionEvent):Boolean{
        if(!enabledInput){release();return true}
        when(event.actionMasked){
            MotionEvent.ACTION_DOWN->{suppressTapFeedback();pointer=event.getPointerId(0);setPressedVisual(true);lastX=event.x;lastY=event.y;if(haptic)performHapticFeedback(android.view.HapticFeedbackConstants.KEYBOARD_TAP);if(!control.joystick)applyKeys(setOf(control.binding).filter{it!=0}.toSet())}
            MotionEvent.ACTION_UP,MotionEvent.ACTION_CANCEL->{release();if(event.actionMasked==MotionEvent.ACTION_UP)performClick();return true}
            MotionEvent.ACTION_POINTER_UP->{if(event.getPointerId(event.actionIndex)==pointer)release();return true}
        }
        val index=event.findPointerIndex(pointer)
        if(index>=0){val x=event.getX(index);val y=event.getY(index)
            if(control.joystick){val dx=(x-width/2f)/max(1f,width/2f);val dy=(y-height/2f)/max(1f,height/2f);val length=max(1f,hypot(dx,dy));knobX=dx/length;knobY=dy/length;applyKeys(joystickKeys(dx,dy,control.directions))}
            else if(event.actionMasked==MotionEvent.ACTION_MOVE&&control.binding<0){remainderX+=(x-lastX)*sensitivity/resources.displayMetrics.density;remainderY+=(y-lastY)*sensitivity/resources.displayMetrics.density;val dx=remainderX.toInt();val dy=remainderY.toInt();remainderX-=dx;remainderY-=dy;if(dx!=0||dy!=0)remote.input(Wire.input(1,flags=4,x=dx,y=dy))}
            lastX=x;lastY=y;invalidate()
        };return true
    }
    override fun onDetachedFromWindow(){release();pressAnimator?.cancel();super.onDetachedFromWindow()}
}
