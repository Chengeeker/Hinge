package com.hinge.office

import kotlin.math.hypot
import kotlin.math.roundToInt

// Coordinates are logical pixels, matching the preserved touchpad calibration.
class TouchpadGesture(private val move:(Int,Int)->Unit,private val wheel:(Int)->Unit,private val click:(Int)->Unit,private val hold:(Boolean)->Unit){
    var sensitivity=3f
    private val points=linkedMapOf<Int,Pair<Float,Float>>()
    private val starts=mutableMapOf<Int,Pair<Float,Float>>()
    private var multi=false;private var tapCandidate=false;private var moved=false;private var dragging=false
    private var button=0;private var lastTap:Long?=null;private var lastPosition:Pair<Float,Float>?=null
    private var centroid:Pair<Float,Float>?=null
    private fun center():Pair<Float,Float>?=points.values.take(2).takeIf{it.size==2}?.let{(it[0].first+it[1].first)/2 to (it[0].second+it[1].second)/2}
    fun down(id:Int,x:Float,y:Float,time:Long,rightHalf:Boolean){
        points[id]=x to y;starts[id]=x to y
        if(points.size==1){button=if(rightHalf)1 else 0;moved=false
            if(button==0&&lastTap?.let{time-it<330}==true&&lastPosition?.let{hypot(x-it.first,y-it.second)<32}==true){hold(true);dragging=true;lastTap=null;lastPosition=null}
        }else if(points.size==2){release();multi=true;tapCandidate=!moved;centroid=center();lastTap=null;lastPosition=null}else tapCandidate=false
    }
    fun move(id:Int,x:Float,y:Float){
        val before=points[id]?:return;points[id]=x to y
        if(points.size==1&&!multi){starts[id]?.let{if(hypot(x-it.first,y-it.second)>8)moved=true};val dx=((x-before.first)*sensitivity).roundToInt();val dy=((y-before.second)*sensitivity).roundToInt();if(dx!=0||dy!=0)move(dx,dy)}
        else if(multi&&points.size>=2){starts[id]?.let{if(hypot(x-it.first,y-it.second)>12)tapCandidate=false};val now=center();val previous=centroid;centroid=now
            if(now!=null&&previous!=null&&hypot(now.first-previous.first,now.second-previous.second)>1){tapCandidate=false;val delta=(-(now.second-previous.second)*2*sensitivity).roundToInt();if(delta!=0)wheel(delta)}
        }
    }
    fun up(id:Int,x:Float,y:Float,time:Long){
        if(points.remove(id)==null)return;starts.remove(id);if(points.isNotEmpty())return
        if(multi){if(tapCandidate)click(1)}else if(dragging)release()else if(!moved){click(button);lastTap=if(button==0)time else null;lastPosition=if(button==0)x to y else null}else{lastTap=null;lastPosition=null}
        multi=false;tapCandidate=false;centroid=null;moved=false
    }
    private fun release(){if(dragging){hold(false);dragging=false}}
    fun cancel(){points.clear();starts.clear();multi=false;tapCandidate=false;centroid=null;moved=false;lastTap=null;lastPosition=null;release()}
}
