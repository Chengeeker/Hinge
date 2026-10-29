#pragma once
#include <dxgi.h>
#include <stdexcept>
#include <vector>
#include <cstdint>

inline bool SwapsAxes(DXGI_MODE_ROTATION rotation) {
    return rotation == DXGI_MODE_ROTATION_ROTATE90 || rotation == DXGI_MODE_ROTATION_ROTATE270;
}

inline void DrawDesktopPointer(std::vector<unsigned char>& nv12, UINT width, UINT height, int px, int py) {
    if (width < 2 || height < 2 || width > 8192 || height > 8192 || (width & 1) || (height & 1) ||
        nv12.size() != (size_t)width * height * 3 / 2)
        throw std::runtime_error("Invalid pointer canvas");
    if (px < -11 || py < -20 || px >= (int)width || py >= (int)height) return;
    for (int y=0; y<20; ++y) for (int x=0; x<11; ++x) {
        bool arrow = y<16 && x<=y/2;
        bool stem = y>=12 && x>=3 && x<=6;
        int dx=px+x, dy=py+y;
        if (!(arrow || stem) || dx<0 || dy<0 || dx>=(int)width || dy>=(int)height) continue;
        nv12[(size_t)dy*width+dx] = (x==0 || (y<16 && x==y/2)) ? 16 : 235;
        size_t uv=(size_t)width*height+(size_t)(dy/2)*width+(dx&~1);
        nv12[uv]=nv12[uv+1]=128;
    }
}

// Map an upright desktop pixel back into the unrotated duplication texture.
inline POINT DesktopSourcePoint(UINT x, UINT y, UINT width, UINT height, DXGI_MODE_ROTATION rotation) {
    switch (rotation) {
        case DXGI_MODE_ROTATION_UNSPECIFIED:
        case DXGI_MODE_ROTATION_IDENTITY: return {(LONG)x, (LONG)y};
        case DXGI_MODE_ROTATION_ROTATE90: return {(LONG)y, (LONG)(height - 1 - x)};
        case DXGI_MODE_ROTATION_ROTATE180: return {(LONG)(width - 1 - x), (LONG)(height - 1 - y)};
        case DXGI_MODE_ROTATION_ROTATE270: return {(LONG)(width - 1 - y), (LONG)x};
        default: throw std::runtime_error("Unsupported desktop rotation");
    }
}
