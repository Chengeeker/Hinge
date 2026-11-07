@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
package com.hinge.office

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.draw.drawBehind
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.background
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsDraggedAsState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.compositeOver

// app.dart's application-wide FilledButton/OutlinedButton/InputDecoration themes.
@Composable fun HingeFilledButton(onClick: () -> Unit, modifier: Modifier = Modifier,
    enabled: Boolean = true, shape: Shape = RoundedCornerShape(50),
    colors: ButtonColors = ButtonDefaults.buttonColors(),
    contentPadding: PaddingValues = if(LocalHingeCompact.current)PaddingValues(horizontal=12.dp,vertical=8.dp)else ButtonDefaults.ContentPadding,
    content: @Composable RowScope.() -> Unit) = Button(onClick,
    modifier.heightIn(min = if(LocalHingeCompact.current)48.dp else 52.dp), enabled, shape, colors, contentPadding = contentPadding, content = content)

@Composable fun HingeTonalButton(onClick: () -> Unit, modifier: Modifier = Modifier,
    enabled: Boolean = true, shape: Shape = RoundedCornerShape(50),
    colors: ButtonColors = ButtonDefaults.filledTonalButtonColors(),
    contentPadding: PaddingValues = if(LocalHingeCompact.current)PaddingValues(horizontal=12.dp,vertical=8.dp)else ButtonDefaults.ContentPadding,
    content: @Composable RowScope.() -> Unit) = FilledTonalButton(onClick,
    modifier.heightIn(min = if(LocalHingeCompact.current)48.dp else 52.dp), enabled, shape, colors, contentPadding = contentPadding, content = content)

@Composable fun HingeOutlinedButton(onClick: () -> Unit, modifier: Modifier = Modifier,
    enabled: Boolean = true, shape: Shape = RoundedCornerShape(50),
    colors: ButtonColors = ButtonDefaults.outlinedButtonColors(),
    border: BorderStroke? = BorderStroke(1.dp,if(enabled)MaterialTheme.colorScheme.outline else MaterialTheme.colorScheme.onSurface.copy(alpha=.12f)),
    contentPadding: PaddingValues = if(LocalHingeCompact.current)PaddingValues(horizontal=12.dp,vertical=8.dp)else ButtonDefaults.ContentPadding,
    content: @Composable RowScope.() -> Unit) = OutlinedButton(onClick,
    modifier.heightIn(min = if(LocalHingeCompact.current)48.dp else 52.dp), enabled, shape, colors, border=border, contentPadding = contentPadding, content = content)

@Composable fun HingeTextField(value: String, onValueChange: (String) -> Unit,
    modifier: Modifier = Modifier, enabled: Boolean = true,
    label: @Composable (() -> Unit)? = null, placeholder: @Composable (() -> Unit)? = null,
    leadingIcon: @Composable (() -> Unit)? = null, supportingText: @Composable (() -> Unit)? = null,
    isError: Boolean = false, minLines: Int = 1, maxLines: Int = Int.MAX_VALUE,
    autofocus: Boolean = false, keyboardOptions: KeyboardOptions = KeyboardOptions.Default,
    visualTransformation: VisualTransformation = VisualTransformation.None) {
    val scheme = MaterialTheme.colorScheme
    val focus=remember{FocusRequester()}
    LaunchedEffect(autofocus){if(autofocus)focus.requestFocus()}
    val colors = OutlinedTextFieldDefaults.colors(
        focusedContainerColor=scheme.surfaceContainerHighest,unfocusedContainerColor=scheme.surfaceContainerHighest,
        disabledContainerColor=scheme.surfaceContainerHighest,errorContainerColor=scheme.surfaceContainerHighest,
        focusedBorderColor=scheme.primary,unfocusedBorderColor=Color.Transparent,disabledBorderColor=Color.Transparent)
    if(LocalHingeCompact.current){
        val interactions=remember{MutableInteractionSource()}
        BasicTextField(value,onValueChange,modifier.focusRequester(focus),enabled=enabled,
            textStyle=MaterialTheme.typography.bodyLarge.copy(color=if(enabled)scheme.onSurface else scheme.onSurface.copy(alpha=.38f)),
            keyboardOptions=keyboardOptions,minLines=minLines,maxLines=maxLines,visualTransformation=visualTransformation,
            interactionSource=interactions,cursorBrush=SolidColor(if(isError)scheme.error else scheme.primary),
            decorationBox={inner->OutlinedTextFieldDefaults.DecorationBox(value=value,innerTextField=inner,enabled=enabled,
                singleLine=maxLines==1,visualTransformation=visualTransformation,interactionSource=interactions,
                isError=isError,label=label,placeholder=placeholder,leadingIcon=leadingIcon,supportingText=supportingText,
                colors=colors,contentPadding=PaddingValues(horizontal=12.dp,vertical=10.dp),
                container={OutlinedTextFieldDefaults.Container(enabled,isError,interactions,colors=colors,shape=RoundedCornerShape(16.dp))})})
    }else OutlinedTextField(value,onValueChange,modifier.focusRequester(focus),enabled=enabled,keyboardOptions=keyboardOptions,
        label=label,placeholder=placeholder,leadingIcon=leadingIcon,supportingText=supportingText,isError=isError,
        minLines=minLines,maxLines=maxLines,visualTransformation=visualTransformation,shape=RoundedCornerShape(16.dp),colors=colors)
}

@Composable fun HingeSlider(value:Float,onValueChange:(Float)->Unit,modifier:Modifier=Modifier,
    enabled:Boolean=true,valueRange:ClosedFloatingPointRange<Float> = 0f..1f,steps:Int=0){
    val scheme=MaterialTheme.colorScheme
    val interactions=remember{MutableInteractionSource()}
    val dragged by interactions.collectIsDraggedAsState()
    val pressed by interactions.collectIsPressedAsState()
    val focused by interactions.collectIsFocusedAsState()
    val compact=LocalHingeCompact.current
    val thumbSize=if(compact)14.dp else 20.dp
    val trackHeight=if(compact)3.dp else 4.dp
    val active=if(enabled)scheme.primary else scheme.onSurface.copy(alpha=.38f)
    val inactive=if(enabled)scheme.surfaceContainerHighest else scheme.onSurface.copy(alpha=.12f)
    val rtl=androidx.compose.ui.platform.LocalLayoutDirection.current==androidx.compose.ui.unit.LayoutDirection.Rtl
    Slider(value,onValueChange,modifier.padding(horizontal=if(compact)11.dp else 14.dp).heightIn(min=48.dp),enabled=enabled,steps=steps,valueRange=valueRange,interactionSource=interactions,
        thumb={Box(Modifier.size(thumbSize).drawBehind{if(enabled&&(dragged||pressed||focused))drawCircle(scheme.primary.copy(alpha=if(focused&&!dragged&&!pressed).08f else .1f),(if(compact)18.dp else 24.dp).toPx())}.shadow(if(enabled)if(dragged)6.dp else 1.dp else 0.dp,CircleShape).background(if(enabled)active else active.compositeOver(scheme.surface),CircleShape))},
        track={Canvas(Modifier.fillMaxWidth().height(trackHeight)){
            val fraction=((value-valueRange.start)/(valueRange.endInclusive-valueRange.start)).coerceIn(0f,1f)
            val start=if(rtl)size.width else 0f;val end=if(rtl)0f else size.width;val thumb=start+(end-start)*fraction
            drawLine(inactive,Offset(start,center.y),Offset(end,center.y),trackHeight.toPx(),StrokeCap.Round)
            drawLine(active,Offset(start,center.y),Offset(thumb,center.y),(trackHeight+2.dp).toPx(),StrokeCap.Round)
            if(steps>0&&size.width/(steps+1)>6.dp.toPx())for(i in 0..steps+1){val f=i.toFloat()/(steps+1);drawCircle((if(!enabled)scheme.onSurface else if(f<=fraction)scheme.onPrimary else scheme.onSurfaceVariant).copy(alpha=.38f),1.dp.toPx(),Offset(start+(end-start)*f,center.y))}
        }})
}

@Composable fun HingeAlertDialog(onDismissRequest:()->Unit,confirmButton:@Composable ()->Unit,
    dismissButton:(@Composable ()->Unit)?=null,dismissOnClickOutside:Boolean=true,title:(@Composable ()->Unit)?=null,text:(@Composable ()->Unit)?=null){
    AlertDialog(onDismissRequest=onDismissRequest,confirmButton=confirmButton,dismissButton=dismissButton,title=title,text=text,
        modifier=Modifier.padding(horizontal=40.dp,vertical=24.dp).hingeWindowTapFeedback(),
        properties=androidx.compose.ui.window.DialogProperties(usePlatformDefaultWidth=false,dismissOnClickOutside=dismissOnClickOutside))
}
