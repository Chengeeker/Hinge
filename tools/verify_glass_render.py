from ctypes import *
import os
from functools import lru_cache
from pathlib import Path

dlls = Path(os.environ["LOCALAPPDATA"]) / "Android/Sdk/emulator/lib64/gles_swiftshader"
os.add_dll_directory(str(dlls))
egl = WinDLL(str(dlls / "libEGL.dll"))
gl = WinDLL(str(dlls / "libGLESv2.dll"))
def function(lib, name, result, *arguments):
    call = getattr(lib, name)
    call.restype = result
    call.argtypes = arguments
    return call
P, I, U, F = c_void_p, c_int, c_uint, c_float
PI = POINTER(I)
def e(name, result, *args): return function(egl, "egl" + name, result, *args)
def g(name, result, *args): return function(gl, "gl" + name, result, *args)
def ints(*values): return (I * len(values))(*values)
density = int(os.environ.get("HINGE_GLASS_TEST_DPR", "1"))
none = 0x3038
display = e("GetDisplay", P, P)(None)
major, minor = I(), I()
assert e("Initialize", U, P, PI, PI)(display, byref(major), byref(minor)), hex(e("GetError", I)())
config, count = P(), I()
assert e("ChooseConfig", U, P, PI, POINTER(P), I, PI)(
    display, ints(0x3024,8,0x3023,8,0x3022,8,0x3021,8,0x3040,4,0x3033,1,none),
    byref(config), 1, byref(count)) and count.value
context = e("CreateContext", P, P, P, P, PI)(display, config, None, ints(0x3098,2,none))
surface = e("CreatePbufferSurface", P, P, P, PI)(display, config, ints(0x3057,328*density,0x3056,112*density,none))
assert context and surface
assert e("MakeCurrent", U, P, P, P, P)(display, surface, surface, context)
print("EGL", major.value, minor.value, g("GetString", c_char_p, U)(0x1f01))
import struct
import zlib

W, H = 328*density, 112*density
assets = Path("app/src/main/assets/glass")
def source(name): return (assets / (name + ".glsl")).read_text("utf-8-sig")
def shader(stage, code):
    shader_id = g("CreateShader", U, U)(stage)
    pointer = c_char_p(code.encode())
    g("ShaderSource", None, U, I, POINTER(c_char_p), PI)(shader_id, 1, byref(pointer), None)
    g("CompileShader", None, U)(shader_id)
    ok = I()
    g("GetShaderiv", None, U, U, PI)(shader_id, 0x8B81, byref(ok))
    log = create_string_buffer(8192)
    g("GetShaderInfoLog", None, U, I, PI, P)(shader_id, len(log), None, log)
    assert ok.value, log.value.decode()
    return shader_id

vertex = shader(0x8B31, "attribute vec2 position; void main(){gl_Position=vec4(position,0.0,1.0);}")
programs = {}
for name in ("gaussian_blur", "shell", "tab_scene", "tab_lens", "flutter_geometry", "flutter_render", "shell_backdrop", "shell_blur"):
    fragment = shader(0x8B30, (source("capsule") + "\n" if name in ("shell", "tab_scene", "tab_lens", "shell_backdrop", "shell_blur") else "") + source(name))
    program = g("CreateProgram", U)()
    for stage in vertex, fragment:
        g("AttachShader", None, U, U)(program, stage)
    g("LinkProgram", None, U)(program)
    ok = I()
    g("GetProgramiv", None, U, U, PI)(program, 0x8B82, byref(ok))
    log = create_string_buffer(8192)
    g("GetProgramInfoLog", None, U, I, PI, P)(program, len(log), None, log)
    assert ok.value, (name, log.value.decode())
    programs[name] = program
print("Linked all eight production programs")

textures = (U * 17)()
framebuffer = U()
g("GenTextures", None, I, POINTER(U))(17, textures)
g("GenFramebuffers", None, I, POINTER(U))(1, byref(framebuffer))
g("Viewport", None, I, I, I, I)(0, 0, W, H)
g("Disable", None, U)(0x0BD0)
for texture in textures:
    g("BindTexture", None, U, U)(0x0DE1, texture)
    for name, value in ((0x2801,0x2601),(0x2800,0x2601),(0x2802,0x812F),(0x2803,0x812F)):
        g("TexParameteri", None, U, U, I)(0x0DE1, name, value)
    g("TexImage2D", None,U,I,I,I,I,I,U,U,P)(0x0DE1,0,0x1908,W,H,0,0x1908,0x1401,None)
vertices = (F * 8)(-1,-1,1,-1,-1,1,1,1)

def upload(slot, data):
    g("ActiveTexture", None, U)(0x84C0)
    g("BindTexture", None,U,U)(0x0DE1,textures[slot])
    buf = create_string_buffer(data)
    g("TexSubImage2D", None,U,I,I,I,I,I,U,U,P)(0x0DE1,0,0,0,W,H,0x1908,0x1401,buf)

def draw(name, target, inputs, **values):
    program = programs[name]
    g("BindFramebuffer", None,U,U)(0x8D40,framebuffer if target is not None else 0)
    if target is not None:
        g("FramebufferTexture2D", None,U,U,U,U,I)(0x8D40,0x8CE0,0x0DE1,textures[target],0)
        assert g("CheckFramebufferStatus", U,U)(0x8D40) == 0x8CD5
    g("UseProgram", None,U)(program)
    def loc(key): return g("GetUniformLocation", I,U,c_char_p)(program,key.encode())
    for unit, (key, texture) in enumerate(inputs.items()):
        g("ActiveTexture", None,U)(0x84C0 + unit)
        g("BindTexture", None,U,U)(0x0DE1,textures[texture])
        g("Uniform1i", None,I,I)(loc(key),unit)
    logical_size = values.pop("uSize_VALUE", (W/density,H/density) if "uSize_OVERRIDE" not in values else (W,H))
    values.pop("uSize_OVERRIDE", None)
    for key, data in dict(uSize=logical_size,uDensity=density,**values).items():
        if isinstance(data, list):
            g("Uniform1fv", None,I,I,POINTER(F))(loc(key),len(data),(F*len(data))(*data))
            continue
        args = data if isinstance(data,tuple) else (data,)
        g("Uniform" + str(len(args)) + "f", None,I,*[F]*len(args))(loc(key),*args)
    position = g("GetAttribLocation", I,U,c_char_p)(program,b"position")
    g("EnableVertexAttribArray",None,U)(position)
    g("VertexAttribPointer",None,U,I,U,c_ubyte,I,P)(position,2,0x1406,0,0,vertices)
    g("DrawArrays",None,U,I,I)(5,0,4)
    g("DisableVertexAttribArray",None,U)(position)
    assert g("GetError",U)() == 0

def read():
    data = (c_ubyte * (W*H*4))()
    g("ReadPixels",None,I,I,I,I,U,U,P)(0,0,W,H,0x1908,0x1401,data)
    assert g("GetError",U)() == 0
    return bytes(data)

def blur(start, end, stride):
    draw("gaussian_blur",1,{"uSource":start},uSize_OVERRIDE=None,uSigma=max(.001,2*stride*density),uStep=(1/W,0))
    draw("gaussian_blur",end,{"uSource":1},uSize_OVERRIDE=None,uSigma=max(.001,2*stride*density),uStep=(0,1/H))

def pixel(buf,x,y): return tuple(buf[(y*W+x)*4:(y*W+x)*4+4])

@lru_cache(maxsize=4)
def fixture_pixels(dark, art):
    page, inactive, active = bytearray(),bytearray(),bytearray()
    for yy in range(H):
        for xx in range(W):
            x, y = xx//density, yy//density
            tone = (27 if dark else 230) + ((x//22 + y//11) % 2) * 13
            page.extend((tone, tone, min(255,tone+7), 255))
            tab = round((x-164)/74.6667)
            dx = abs(x - (164 + 74.6667*tab))
            icon = (dx<9 and 40<y<58 and (dx>5 or y<45 or y>54))
            label = dx<14 and 65<y<70 and (x%6)<4
            inactive.extend((240,244,249,255) if dark and (icon or label) and art else
                            (45,50,64,255) if (icon or label) and art else (0,0,0,0))
            active.extend(((197,183,255,255) if dark else (88,44,163,255))
                          if (icon or label) and art else (0,0,0,0))
    return bytes(page), bytes(inactive), bytes(active)

def fixture(dark, art):
    page, inactive, active = fixture_pixels(dark, art)
    upload(0,page);upload(5,inactive);upload(6,active)
    return page

def optical(source_slot, dark, rect, indicator, pressure):
    cx,cy,w,h=rect
    fade=max(.001,pressure) if indicator else 1
    native=indicator and not dark
    depth=(20 if indicator else 10)*fade*density/3
    index=1+.18*fade if indicator else 1.16
    dispersion=.025*fade if indicator else .008
    geometry_values=dict(uSize_OVERRIDE=True,
         uTransform=(cx*density,cy*density,1,1),uOpticalProps=(index,dispersion,depth,0),
         uShapeSettings=(1,density),uNativeEdge=int(native),
         uShapeData=[3,cx*density,cy*density,w*density,h*density,9999*density,9999*density])
    draw("flutter_geometry",8,{},**geometry_values)
    if "reference_geometry" in programs:
        actual_geometry=read()
        draw("reference_geometry",16,{},**geometry_values)
        assert actual_geometry == read(), "Single-capsule adapter changed the original Flutter geometry"
    gw,gh=w*density,h*density
    tint=int(indicator == dark)
    rim_light=(0 if indicator else .12) if dark else (.35 if indicator else 1)
    values=dict(uSize_OVERRIDE=True,uBackdropSize=(W,H),uCropOrigin=(0,0),
        uGeometryOffset=(cx*density-gw/2,cy*density-gh/2),uGeometrySize=(gw,gh),
        uGlassColor=(tint,tint,tint,((38/255 if dark else 24/255) if indicator else (.4 if dark else 240/255))*fade),
        uOpticalProps=(index,dispersion,depth,1),
        uLightConfig=((2 if indicator else .25)*fade,0,1.5 if indicator else 1),
        uLightDirection=(-.7071068,.7071068),uEdgeConfig=(0,fade,density/3,0),
        uRimConfig=(.12*fade if native else 0,rim_light*fade,.2),
        uBodyMode=1,uWhitenGated=1,
        uTouchPosition=(cx*density,cy*density),uTouchIntensity=pressure*.125,
        uPinchStrength=.7*(1-(1-fade)**2) if indicator else 0)
    draw("flutter_render",9,{"uBackgroundTexture":source_slot,"uGeometryTexture":8},**values)
    if "reference" in programs:
        actual=read()
        draw("reference",16,{"uBackgroundTexture":source_slot,"uGeometryTexture":8},**values)
        errors=[abs(a-b) for a,b in zip(actual,read())]
        assert max(errors) <= 1, (dark,indicator,pressure,max(errors))

bar = (164,56,232,56)
def compose_lens(dark, pressure):
    draw("tab_lens",None,{"uPage":0,"uScene":7,"uOptical":9,"uShell":3},
         uBar=bar,uDark=int(dark),uPressure=pressure)
    return read()

def render(dark, position, pressure, velocity=0, art=True):
    page = fixture(dark,art)
    expansion = 1 + .06*pressure
    distortion = min(abs(velocity)/10,1)*.8*.7*pressure
    slot = (232-8)/3
    lens = (164+(position-1)*slot,56,
            (slot*expansion+24*pressure)*(1-.5*distortion),
            (48*expansion+16*pressure)*(1+.3*distortion))
    optical(0,dark,bar,False,0)
    blur(8,10,3.04034/2)
    blur(8,11,1.135085/2)
    draw("shell_backdrop",12,{"uPage":0,"uGeometry":8,"uElevation":10,"uContact":11},uDark=int(dark))
    blur(12,2,.75)
    draw("shell_blur",13,{"uBackdrop":12,"uBlurred":2},uBar=bar)
    optical(13,dark,bar,False,0)
    draw("shell",3,{"uPrepared":13,"uOptical":9,"uBackdrop":12})
    blur(3,4,3)
    draw("tab_scene",7,{"uShell":3,"uRestBlur":4,"uUnselected":5,"uSelected":6},
         uBar=bar,uDark=int(dark),uLens=lens,uRestOpacity=1-pressure)
    scene = read()
    if pressure > .001: optical(7,dark,lens,True,pressure)
    return page, scene, compose_lens(dark,pressure)

def png(path, rows):
    def chunk(kind, data): return struct.pack(">I",len(data))+kind+data+struct.pack(">I",zlib.crc32(kind+data))
    image = b"\x89PNG\r\n\x1a\n"
    image += chunk(b"IHDR",struct.pack(">IIBBBBB",W,len(rows),8,2,0,0,0))
    image += chunk(b"IDAT",zlib.compress(b"".join(b"\x00"+row for row in rows)))
    Path(path).write_bytes(image+chunk(b"IEND",b""))


# The vendor optical pass in local coordinates, with only its matte sampler
# redirected to the full-size EGL geometry texture used by Hinge.
import sync_glass_shaders as upstream
reference = upstream.read("liquid_glass_render.frag")
for include in ("render.glsl", "displacement_encoding.glsl"):
    reference = reference.replace('#include "'+include+'"', upstream.read(include))
reference = reference.replace("texture(uGeometryTexture, geometryUV)", "texture(uGeometryTexture, gl_FragCoord.xy / uSize)")
reference = reference.replace("FlutterFragCoord().xy", "gl_FragCoord.xy")
fragment = shader(0x8B30, upstream.gles(reference))
program = g("CreateProgram", U)()
for stage in (vertex, fragment): g("AttachShader",None,U,U)(program,stage)
g("LinkProgram",None,U)(program)
ok=I();g("GetProgramiv",None,U,U,PI)(program,0x8B82,byref(ok))
assert ok.value
programs["reference"] = program

reference_geometry=upstream.read("liquid_glass_geometry_blended.frag")
for include in ("sdf.glsl", "displacement_encoding.glsl"):
    reference_geometry=reference_geometry.replace('#include "'+include+'"',upstream.read(include))
reference_geometry=reference_geometry.replace("uniform vec2 uSize;", "uniform vec2 uSize;\nuniform vec4 uTransform;")
reference_geometry=reference_geometry.replace("FlutterFragCoord().xy", "(gl_FragCoord.xy - uTransform.xy) / uTransform.zw + uTransform.xy")
fragment=shader(0x8B30,upstream.gles(reference_geometry))
program=g("CreateProgram",U)()
for stage in (vertex,fragment):g("AttachShader",None,U,U)(program,stage)
g("LinkProgram",None,U)(program)
ok=I();g("GetProgramiv",None,U,U,PI)(program,0x8B82,byref(ok))
assert ok.value
programs["reference_geometry"]=program

# At the same backdrop pixel, a passing resting plate must brighten dark glass
# and darken light glass, even with no glyphs or water rendered.
for dark in (False, True):
    page, _, selected_frame = render(dark, 1, 0, art=False)
    _, _, other_frame = render(dark, 0, 0, art=False)
    x, y = 164 * density, 56 * density
    background = pixel(page, x, y)[0]
    def displayed_red(frame):
        r, _, _, a = pixel(frame, x, y)
        return r + round(background * (255 - a) / 255)
    delta = displayed_red(selected_frame) - displayed_red(other_frame)
    assert (delta > 20 if dark else delta < -20), (dark, delta)
    print("Resting plate red-channel contrast:", "dark" if dark else "light", delta)

rows=[]
for dark in (False,True):
    _, _, idle = render(dark,1,0)
    page, scene, press = render(dark,1,1)
    assert pixel(idle,164*density,23*density)[3] < 48 and pixel(press,164*density,23*density)[3] > 230
    page, scene, press = render(dark,.44,1,9)
    _, blank_scene, blank = render(dark,.44,1,9,art=False)
    assert pixel(press,0,0)[3] == 0
    def contrast(a,b,offset): return max(abs(a[offset+j]-b[offset+j]) for j in range(3))
    pulled = sum(contrast(scene,blank_scene,i)<3 and contrast(press,blank,i)>16
                 for i in range(0,len(press),4))
    assert pulled > 40, pulled
    print("dark =", dark, "refracted glyph pixels outside original strokes =", pulled)
    for pos,bloom,velocity in ((1,0,0),(1,.10,0),(1,.5,0),(1,1,0),(.44,1,9),(0,1,-6)):
        page,_,output=render(dark,pos,bloom,velocity)
        for y in range(H):
            row=bytearray()
            for x in range(W):
                p=(y*W+x)*4
                row.extend(min(255,output[p+i] + round(page[p+i]*(255-output[p+3])/255)) for i in range(3))
            rows.append(bytes(row))
png(f"tmp/compose-glass-172-{density}x.png",rows)
print("Production geometry and local refraction matched the original vendor passes")

# Hold the optical texture fixed to isolate the actual premultiplied fade.
render(True,1,1,art=False)
alpha = [pixel(compose_lens(True,pressure),164*density,24*density)[3]
         for pressure in (1,.5,0)]
assert alpha[0] >= 230 and abs(alpha[1] - alpha[0]/2) <= 1 and alpha[2] == 0, alpha
print("Water alpha at full, half and zero pressure:", alpha)

