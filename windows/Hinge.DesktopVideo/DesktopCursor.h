#pragma once
#include <windows.h>
#include <algorithm>
#include <vector>
#include <cstring>
#include <cstdint>

class DesktopCursor {
    HCURSOR cached = nullptr;
    UINT nativeWidth = 0, nativeHeight = 0, hotspotX = 0, hotspotY = 0;
    HDC dc = nullptr;
    HBITMAP bitmap = nullptr;
    HGDIOBJ previous = nullptr;
    BYTE* pixels = nullptr;
    UINT canvasWidth = 0, canvasHeight = 0;
    std::vector<BYTE> before;
    static BYTE Clamp(int value) { return (BYTE)std::clamp(value, 0, 255); }
    void ReleaseCanvas() {
        if(previous)SelectObject(dc,previous);
        if(bitmap)DeleteObject(bitmap);
        if(dc)DeleteDC(dc);
        dc=nullptr;bitmap=nullptr;previous=nullptr;pixels=nullptr;canvasWidth=canvasHeight=0;
    }
    bool LoadShape(HCURSOR cursor) {
        if(cached==cursor && nativeWidth)return true;
        ICONINFO info{};
        if(!GetIconInfo(cursor,&info))return false;
        struct Bitmaps { HBITMAP color,mask;~Bitmaps(){if(color)DeleteObject(color);if(mask)DeleteObject(mask);} } owned{info.hbmColor,info.hbmMask};
        BITMAP shape{};
        if(!GetObject(info.hbmColor?info.hbmColor:info.hbmMask,sizeof(shape),&shape))return false;
        LONG height=info.hbmColor?shape.bmHeight:shape.bmHeight/2;
        if(shape.bmWidth<1 || shape.bmWidth>512 || height<1 || height>512 ||
            info.xHotspot>=(UINT)shape.bmWidth || info.yHotspot>=(UINT)height)return false;
        cached=cursor;nativeWidth=(UINT)shape.bmWidth;nativeHeight=(UINT)height;
        hotspotX=info.xHotspot;hotspotY=info.yHotspot;
        return true;
    }
    bool PrepareCanvas(UINT w,UINT h) {
        if(dc && canvasWidth==w && canvasHeight==h)return true;
        ReleaseCanvas();dc=CreateCompatibleDC(nullptr);
        if(!dc)return false;
        BITMAPINFO info{};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth=(LONG)w;info.bmiHeader.biHeight=-(LONG)h;
        info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;info.bmiHeader.biCompression=BI_RGB;
        void* data=nullptr;bitmap=CreateDIBSection(dc,&info,DIB_RGB_COLORS,&data,nullptr,0);
        if(!bitmap || !data){ReleaseCanvas();return false;}
        previous=SelectObject(dc,bitmap);
        if(!previous || previous==HGDI_ERROR){previous=nullptr;ReleaseCanvas();return false;}
        pixels=(BYTE*)data;canvasWidth=w;canvasHeight=h;before.resize((size_t)w*h*4);
        return true;
    }
public:
    ~DesktopCursor(){ReleaseCanvas();}
    DesktopCursor()=default;
    DesktopCursor(const DesktopCursor&)=delete;
    DesktopCursor& operator=(const DesktopCursor&)=delete;

    // Only the cursor-sized region leaves NV12; transparent pixels preserve the original frame exactly.
    bool Draw(std::vector<BYTE>& nv12,UINT width,UINT height,int px,int py,UINT desktopWidth,UINT desktopHeight,HCURSOR cursor) {
        if(!desktopWidth || !desktopHeight || desktopWidth>32768 || desktopHeight>32768 || width<2 || height<2 || (width&1) || (height&1) ||
            width>8192 || height>8192 || nv12.size()!=(size_t)width*height*3/2 || !cursor)return false;
        if(px < -512 || py < -512 || px > (int)width+512 || py > (int)height+512)return true;
        if(!LoadShape(cursor))return false;
        UINT w=std::max(1,MulDiv((int)nativeWidth,(int)width,(int)desktopWidth));
        UINT h=std::max(1,MulDiv((int)nativeHeight,(int)height,(int)desktopHeight));
        if(w>512 || h>512)return false;
        int left=px-MulDiv((int)hotspotX,(int)width,(int)desktopWidth);
        int top=py-MulDiv((int)hotspotY,(int)height,(int)desktopHeight);
        if(left>=(int)width || top>=(int)height || (int64_t)left+w<=0 || (int64_t)top+h<=0)return true;
        int originX=left&~1,originY=top&~1;
        if(!PrepareCanvas((w+3)&~1u,(h+3)&~1u))return false;
        for(UINT y=0;y<canvasHeight;++y)for(UINT x=0;x<canvasWidth;++x) {
            int sx=originX+(int)x,sy=originY+(int)y;
            BYTE* p=pixels+((size_t)y*canvasWidth+x)*4;
            p[0]=p[1]=p[2]=0;p[3]=255;
            if(sx<0 || sy<0 || sx>=(int)width || sy>=(int)height)continue;
            int c=nv12[(size_t)sy*width+sx]-16;
            size_t uv=(size_t)width*height+(size_t)(sy/2)*width+(sx&~1);
            int d=nv12[uv]-128,e=nv12[uv+1]-128;
            p[2]=Clamp((298*c+409*e+128)>>8);p[1]=Clamp((298*c-100*d-208*e+128)>>8);p[0]=Clamp((298*c+516*d+128)>>8);
        }
        memcpy(before.data(),pixels,before.size());
        // DrawIconEx handles alpha and monochrome AND/XOR shapes on the real cursor background.
        // shortcut: animated cursors use step zero; add animation timing if users need animated pointer playback.
        if(!DrawIconEx(dc,left-originX,top-originY,cursor,(int)w,(int)h,0,nullptr,DI_NORMAL))return false;
        GdiFlush();
        for(UINT y=0;y<canvasHeight;y+=2)for(UINT x=0;x<canvasWidth;x+=2) {
            int sx=originX+(int)x,sy=originY+(int)y;
            if(sx<0 || sy<0 || sx+1>=(int)width || sy+1>=(int)height)continue;
            size_t uv=(size_t)width*height+(size_t)(sy/2)*width+sx;
            int u=nv12[uv]*4,v=nv12[uv+1]*4;bool changed=false;
            for(UINT dy=0;dy<2;++dy)for(UINT dx=0;dx<2;++dx) {
                size_t offset=((size_t)(y+dy)*canvasWidth+x+dx)*4;
                BYTE* p=pixels+offset;
                if(memcmp(p,before.data()+offset,3)==0)continue;
                changed=true;int b=p[0],g=p[1],r=p[2];
                nv12[(size_t)(sy+dy)*width+sx+dx]=Clamp(((66*r+129*g+25*b+128)>>8)+16);
                u+=(((-38*r-74*g+112*b+128)>>8)+128)-nv12[uv];
                v+=(((112*r-94*g-18*b+128)>>8)+128)-nv12[uv+1];
            }
            if(changed){nv12[uv]=Clamp(u/4);nv12[uv+1]=Clamp(v/4);}
        }
        return true;
    }
};
