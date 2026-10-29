#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mftransform.h>
#include <mferror.h>
#include <codecapi.h>
#include <wmcodecdsp.h>
#include <wrl/client.h>
#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cstdint>
#include <memory>
#include <fstream>
#include <fcntl.h>
#include <io.h>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>
#include "GpuVideoConverter.h"
#include "DesktopGeometry.h"
#include "DesktopCursor.h"

using Microsoft::WRL::ComPtr;
using Clock = std::chrono::steady_clock;
class TickWait {
    HANDLE timer = CreateWaitableTimerExW(nullptr, nullptr, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
public:
    TickWait() { if (!timer) throw std::runtime_error("High resolution timer unavailable"); }
    void Wait() {
        LARGE_INTEGER due{}; due.QuadPart = -10000;
        if (!SetWaitableTimer(timer, &due, 0, nullptr, nullptr, FALSE) || WaitForSingleObject(timer, 1000) != WAIT_OBJECT_0)
            throw std::runtime_error("Desktop frame timer failed");
    }
    ~TickWait() { if (timer) CloseHandle(timer); }
};
static void Check(HRESULT hr, const char* operation) {
    if (FAILED(hr)) {
        char message[160]; sprintf_s(message, "%s: HRESULT 0x%08lX", operation, hr);
        throw std::runtime_error(message);
    }
}
static void SetCodec(ICodecAPI* codec, const GUID& key, ULONG value) {
    VARIANT v; VariantInit(&v); v.vt = VT_UI4; v.ulVal = value;
    codec->SetValue(&key, &v); // Vendor encoders may not implement optional tuning.
}
static void Put32(unsigned char* p, uint32_t n) { for (int i = 3; i >= 0; --i) { p[i] = (unsigned char)n; n >>= 8; } }
static void Put64(unsigned char* p, uint64_t n) { for (int i = 7; i >= 0; --i) { p[i] = (unsigned char)n; n >>= 8; } }
static void WritePacket(IMFSample* sample, UINT width, UINT height, uint32_t& sequence) {
    ComPtr<IMFMediaBuffer> buffer; Check(sample->ConvertToContiguousBuffer(&buffer), "output buffer");
    BYTE* bytes = nullptr; DWORD length = 0; Check(buffer->Lock(&bytes, nullptr, &length), "output lock");
    if (!length || length > 4 * 1024 * 1024) { buffer->Unlock(); throw std::runtime_error("Invalid encoded access unit size"); }
    LONGLONG pts = 0; sample->GetSampleTime(&pts);
    UINT32 key = 0; sample->GetUINT32(MFSampleExtension_CleanPoint, &key);
    unsigned char header[32] = {'H','D','V','1',1,0}; header[5] = key ? 2 : 0;
    Put32(header + 8, sequence++); Put64(header + 12, (uint64_t)std::max<LONGLONG>(0, pts / 10));
    Put32(header + 20, width); Put32(header + 24, height); Put32(header + 28, length);
    bool written = fwrite(header, 1, sizeof(header), stdout) == sizeof(header) && fwrite(bytes, 1, length, stdout) == length;
    buffer->Unlock();
    if (!written || fflush(stdout)) throw std::runtime_error("Desktop media pipe closed");
}

class Encoder {
    ComPtr<IMFTransform> transform;
    ComPtr<IMFMediaEventGenerator> events;
    UINT width, height, fps;
    DWORD inputId = 0, outputId = 0;
    unsigned credits = 0;
    uint32_t sequence = 0;
    unsigned inFlight = 0;
    Clock::time_point lastOutput = Clock::now();
    bool asynchronous = false;
    void Output() {
        MFT_OUTPUT_STREAM_INFO info{}; Check(transform->GetOutputStreamInfo(outputId, &info), "output info");
        MFT_OUTPUT_DATA_BUFFER output{}; output.dwStreamID = outputId;
        ComPtr<IMFSample> owned;
        if (!(info.dwFlags & MFT_OUTPUT_STREAM_PROVIDES_SAMPLES)) {
            Check(MFCreateSample(&owned), "output sample");
            ComPtr<IMFMediaBuffer> buffer;
            Check(MFCreateAlignedMemoryBuffer(std::max<DWORD>(info.cbSize, 4 * 1024 * 1024), info.cbAlignment ? info.cbAlignment - 1 : 0, &buffer), "output allocation");
            Check(owned->AddBuffer(buffer.Get()), "output attach"); output.pSample = owned.Get();
        }
        DWORD status = 0; HRESULT hr = transform->ProcessOutput(0, 1, &output, &status);
        if (output.pEvents) output.pEvents->Release();
        if (hr == MF_E_TRANSFORM_NEED_MORE_INPUT) return;
        ComPtr<IMFSample> supplied;
        if (output.pSample && output.pSample != owned.Get()) supplied.Attach(output.pSample);
        Check(hr, "encode output");
        if (output.pSample) {
            WritePacket(output.pSample, width, height, sequence);
            if (inFlight) --inFlight;
            lastOutput = Clock::now();
        }
    }
public:
    std::wstring name;
    bool hardware = false;
    Encoder(UINT w, UINT h, UINT rate, UINT bitrate, bool softwareOnly) : width(w), height(h), fps(rate) {
        MFT_REGISTER_TYPE_INFO in{MFMediaType_Video, MFVideoFormat_NV12};
        MFT_REGISTER_TYPE_INFO out{MFMediaType_Video, MFVideoFormat_H264};
        for (bool hw : {true, false}) {
            if (hw && softwareOnly) continue;
            IMFActivate** candidates = nullptr; UINT32 count = 0;
            Check(MFTEnumEx(MFT_CATEGORY_VIDEO_ENCODER, (hw ? MFT_ENUM_FLAG_HARDWARE : MFT_ENUM_FLAG_SYNCMFT) | MFT_ENUM_FLAG_SORTANDFILTER, &in, &out, &candidates, &count), "enumerate encoder");
            for (UINT32 i = 0; i < count; ++i) {
                if (!transform) {
                    ComPtr<IMFTransform> candidate;
                    HRESULT hr = candidates[i]->ActivateObject(IID_PPV_ARGS(&candidate));
                    if (SUCCEEDED(hr)) {
                        try {
                            ComPtr<IMFAttributes> attrs; Check(candidate->GetAttributes(&attrs), "encoder attributes");
                            attrs->SetUINT32(MF_LOW_LATENCY, TRUE);
                            UINT32 asyncFlag = 0; attrs->GetUINT32(MF_TRANSFORM_ASYNC, &asyncFlag);
                            if (asyncFlag) Check(attrs->SetUINT32(MF_TRANSFORM_ASYNC_UNLOCK, TRUE), "unlock encoder");
                            ComPtr<ICodecAPI> tuning; if (SUCCEEDED(candidate.As(&tuning))) {
                                SetCodec(tuning.Get(), CODECAPI_AVEncMPVDefaultBPictureCount, 0);
                                SetCodec(tuning.Get(), CODECAPI_AVEncCommonRateControlMode, eAVEncCommonRateControlMode_CBR);
                                SetCodec(tuning.Get(), CODECAPI_AVEncCommonMeanBitRate, bitrate);
                                VARIANT low; VariantInit(&low); low.vt = VT_BOOL; low.boolVal = VARIANT_TRUE;
                                tuning->SetValue(&CODECAPI_AVLowLatencyMode, &low);
                            }
                            DWORD inStream = 0, outStream = 0;
                            HRESULT ids = candidate->GetStreamIDs(1, &inStream, 1, &outStream);
                            if (FAILED(ids) && ids != E_NOTIMPL) Check(ids, "encoder stream ids");
                            ComPtr<IMFMediaType> outputType; Check(MFCreateMediaType(&outputType), "output type");
                            outputType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video); outputType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264);
                            outputType->SetUINT32(MF_MT_AVG_BITRATE, bitrate); outputType->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
                            outputType->SetUINT32(MF_MT_MPEG2_PROFILE, eAVEncH264VProfile_Base);
                            outputType->SetUINT32(MF_MT_YUV_MATRIX, MFVideoTransferMatrix_BT601);
                            outputType->SetUINT32(MF_MT_VIDEO_NOMINAL_RANGE, MFNominalRange_16_235);
                            MFSetAttributeSize(outputType.Get(), MF_MT_FRAME_SIZE, w, h); MFSetAttributeRatio(outputType.Get(), MF_MT_FRAME_RATE, rate, 1);
                            MFSetAttributeRatio(outputType.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
                            Check(candidate->SetOutputType(outStream, outputType.Get(), 0), "set H264 output");
                            ComPtr<IMFMediaType> inputType; Check(MFCreateMediaType(&inputType), "input type");
                            inputType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video); inputType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12);
                            inputType->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
                            inputType->SetUINT32(MF_MT_YUV_MATRIX, MFVideoTransferMatrix_BT601);
                            inputType->SetUINT32(MF_MT_VIDEO_NOMINAL_RANGE, MFNominalRange_16_235);
                            MFSetAttributeSize(inputType.Get(), MF_MT_FRAME_SIZE, w, h); MFSetAttributeRatio(inputType.Get(), MF_MT_FRAME_RATE, rate, 1);
                            MFSetAttributeRatio(inputType.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
                            Check(candidate->SetInputType(inStream, inputType.Get(), 0), "set NV12 input");
                            transform = candidate; inputId = inStream; outputId = outStream; asynchronous = asyncFlag != 0; hardware = hw;
                            WCHAR* friendly = nullptr; UINT32 chars = 0;
                            if (SUCCEEDED(candidates[i]->GetAllocatedString(MFT_FRIENDLY_NAME_Attribute, &friendly, &chars))) { name = friendly; CoTaskMemFree(friendly); }
                        } catch (const std::exception& error) { fprintf(stderr, "encoder rejected: %s\n", error.what()); candidates[i]->ShutdownObject(); }
                    }
                }
                candidates[i]->Release();
            }
            CoTaskMemFree(candidates);
            if (transform) break;
        }
        if (!transform) throw std::runtime_error("No compatible H264 encoder");
        if (asynchronous) Check(transform.As(&events), "encoder event interface");
        Check(transform->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0), "begin encoder");
        Check(transform->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0), "start encoder");
        fwprintf(stderr, L"encoder=%s hardware=%d dimensions=%ux%u fps=%u\n", name.c_str(), hardware ? 1 : 0, w, h, rate);
    }
    void Poll() {
        if (!asynchronous) return;
        while (true) {
            ComPtr<IMFMediaEvent> event; HRESULT hr = events->GetEvent(MF_EVENT_FLAG_NO_WAIT, &event);
            if (hr == MF_E_NO_EVENTS_AVAILABLE) break;
            Check(hr, "encoder event"); HRESULT status; Check(event->GetStatus(&status), "event status"); Check(status, "encoder async status");
            MediaEventType type; Check(event->GetType(&type), "event type");
            if (type == METransformNeedInput) ++credits;
            else if (type == METransformHaveOutput) Output();
        }
    }
    uint32_t OutputCount() const { return sequence; }
    bool Ready() {
        Poll();
        if (inFlight && Clock::now()-lastOutput > std::chrono::seconds(3))
            throw std::runtime_error("Encoder produced no output for three seconds");
        return (!asynchronous || credits > 0) && inFlight < 3;
    }
    void Submit(const std::vector<BYTE>& nv12, LONGLONG time) {
        ComPtr<IMFSample> sample; Check(MFCreateSample(&sample), "input sample");
        ComPtr<IMFMediaBuffer> buffer; Check(MFCreateMemoryBuffer((DWORD)nv12.size(), &buffer), "input allocation");
        BYTE* data; Check(buffer->Lock(&data, nullptr, nullptr), "input lock"); memcpy(data, nv12.data(), nv12.size()); buffer->Unlock();
        Check(buffer->SetCurrentLength((DWORD)nv12.size()), "input size"); sample->AddBuffer(buffer.Get()); sample->SetSampleTime(time); sample->SetSampleDuration(10000000 / fps);
        Check(transform->ProcessInput(inputId, sample.Get(), 0), "encode input");
        if (!inFlight++) lastOutput = Clock::now();
        if (asynchronous) --credits; else Output();
    }
    ~Encoder() {
        if (transform) { transform->ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM, 0); transform->ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0); }
        ComPtr<IMFShutdown> shutdown; if (transform && SUCCEEDED(transform.As(&shutdown))) shutdown->Shutdown();
    }
};

struct DesktopOutput {
    ComPtr<IDXGIAdapter1> adapter;
    ComPtr<IDXGIOutput> output;
    DXGI_OUTPUT_DESC description{};
};
static std::vector<DesktopOutput> DesktopOutputs() {
    ComPtr<IDXGIFactory1> factory;Check(CreateDXGIFactory1(IID_PPV_ARGS(&factory)),"DXGI factory");
    std::vector<DesktopOutput> outputs;
    for(UINT a=0;outputs.size()<16;++a) {
        ComPtr<IDXGIAdapter1> adapter;HRESULT hr=factory->EnumAdapters1(a,&adapter);
        if(hr==DXGI_ERROR_NOT_FOUND)break;Check(hr,"desktop adapter enumerate");
        for(UINT o=0;outputs.size()<16;++o) {
            DesktopOutput item;item.adapter=adapter;hr=adapter->EnumOutputs(o,&item.output);
            if(hr==DXGI_ERROR_NOT_FOUND)break;Check(hr,"desktop output enumerate");
            Check(item.output->GetDesc(&item.description),"monitor descriptor");
            if(item.description.AttachedToDesktop)outputs.push_back(std::move(item));
        }
    }
    return outputs;
}

class Desktop {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<IDXGIOutputDuplication> duplication;
    ComPtr<ID3D11Texture2D> staging;
    std::unique_ptr<GpuVideoConverter> gpu;
    DesktopCursor cursorImage;
    UINT sourceWidth = 0, sourceHeight = 0;
    UINT desktopWidth = 0, desktopHeight = 0;
    DXGI_MODE_ROTATION rotation = DXGI_MODE_ROTATION_IDENTITY;
    LONG originX = 0, originY = 0;
    bool hasFrame = false;
    bool pointerMetadataKnown = false, separatePointer = true;
    Clock::time_point created = Clock::now();
    static BYTE Clamp(int n) { return (BYTE)std::clamp(n, 0, 255); }
    void ConvertCpu(const BYTE* pixels, UINT pitch, std::vector<BYTE>& nv12, bool upright = false) {
        nv12.resize((size_t)width * height * 3 / 2);
        for (UINT y = 0; y < height; y += 2) for (UINT x = 0; x < width; x += 2) {
            int sumU = 0, sumV = 0;
            for (UINT dy = 0; dy < 2; ++dy) for (UINT dx = 0; dx < 2; ++dx) {
                UINT px = (x + dx) * desktopWidth / width, py = (y + dy) * desktopHeight / height;
                POINT point = upright ? POINT{(LONG)px, (LONG)py} : DesktopSourcePoint(px, py, sourceWidth, sourceHeight, rotation);
                const BYTE* p = pixels + (size_t)point.y * pitch + (size_t)point.x * 4;
                int b = p[0], g = p[1], r = p[2];
                nv12[(size_t)(y + dy) * width + x + dx] = Clamp(((66*r + 129*g + 25*b + 128) >> 8) + 16);
                sumU += ((-38*r - 74*g + 112*b + 128) >> 8) + 128;
                sumV += ((112*r - 94*g - 18*b + 128) >> 8) + 128;
            }
            size_t uv = (size_t)width * height + (size_t)(y/2)*width + x;
            nv12[uv] = Clamp(sumU / 4); nv12[uv+1] = Clamp(sumV / 4);
        }
    }
    static void RequireUnlockedDesktop() {
        HDESK desktop=OpenInputDesktop(0,FALSE,DESKTOP_READOBJECTS);
        if(!desktop) throw std::runtime_error("Desktop is locked or unavailable");
        WCHAR name[64]{};DWORD needed=0;
        BOOL available=GetUserObjectInformationW(desktop,UOI_NAME,name,sizeof(name),&needed);
        CloseDesktop(desktop);
        if(!available || _wcsicmp(name,L"Default")!=0) throw std::runtime_error("Desktop is locked or secure");
    }
    void DrawCursor(std::vector<BYTE>& nv12, bool forceFallback = false) {
        CURSORINFO cursor{}; cursor.cbSize = sizeof(cursor);
        bool systemCursorVisible = !forceFallback && GetCursorInfo(&cursor) && (cursor.flags & CURSOR_SHOWING);
        POINT position{};
        if (systemCursorVisible) position = cursor.ptScreenPos;
        else if (!GetCursorPos(&position)) return;
        int64_t x=(int64_t)position.x-originX, y=(int64_t)position.y-originY;
        if(x<0 || y<0 || x>=desktopWidth || y>=desktopHeight) return;
        int px=(int)(x*width/desktopWidth),py=(int)(y*height/desktopHeight);
        if(!systemCursorVisible || !cursorImage.Draw(nv12,width,height,px,py,desktopWidth,desktopHeight,cursor.hCursor))
            DrawDesktopPointer(nv12,width,height,px,py);
    }
    void InitialSnapshot(std::vector<BYTE>& nv12) {
        RequireUnlockedDesktop();
        struct Gdi {
            HDC screen=nullptr, memory=nullptr;HBITMAP bitmap=nullptr;HGDIOBJ old=nullptr;
            ~Gdi(){if(old)SelectObject(memory,old);if(bitmap)DeleteObject(bitmap);if(memory)DeleteDC(memory);if(screen)ReleaseDC(nullptr,screen);}
        } image;
        image.screen=GetDC(nullptr);if(!image.screen)throw std::runtime_error("Initial desktop DC unavailable");
        image.memory=CreateCompatibleDC(image.screen);if(!image.memory)throw std::runtime_error("Initial desktop memory DC unavailable");
        BITMAPINFO info{};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);info.bmiHeader.biWidth=(LONG)desktopWidth;
        info.bmiHeader.biHeight=-(LONG)desktopHeight;info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;info.bmiHeader.biCompression=BI_RGB;
        void* bits=nullptr;image.bitmap=CreateDIBSection(image.screen,&info,DIB_RGB_COLORS,&bits,nullptr,0);
        if(!image.bitmap || !bits)throw std::runtime_error("Initial desktop bitmap unavailable");
        image.old=SelectObject(image.memory,image.bitmap);
        if(!image.old || image.old==HGDI_ERROR) { image.old=nullptr;throw std::runtime_error("Initial desktop bitmap selection failed"); }
        if(!BitBlt(image.memory,0,0,(int)desktopWidth,(int)desktopHeight,image.screen,originX,originY,SRCCOPY|CAPTUREBLT))
            throw std::runtime_error("Initial desktop snapshot failed");
        for(size_t i=3;i<(size_t)desktopWidth*desktopHeight*4;i+=4)((BYTE*)bits)[i]=255;
        // GDI snapshots are already upright, unlike duplication textures.
        if(gpu && rotation <= DXGI_MODE_ROTATION_IDENTITY) {
            D3D11_TEXTURE2D_DESC desc{};desc.Width=desktopWidth;desc.Height=desktopHeight;desc.MipLevels=desc.ArraySize=1;
            desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.Usage=D3D11_USAGE_DEFAULT;desc.BindFlags=D3D11_BIND_RENDER_TARGET;
            D3D11_SUBRESOURCE_DATA data{};data.pSysMem=bits;data.SysMemPitch=desktopWidth*4;
            ComPtr<ID3D11Texture2D> texture;Check(device->CreateTexture2D(&desc,&data,&texture),"initial snapshot texture");
            gpu->Convert(texture.Get(),nv12);
        } else ConvertCpu((BYTE*)bits,desktopWidth*4,nv12,true);
        DrawCursor(nv12);
        RequireUnlockedDesktop();
        fprintf(stderr,"initial-frame=gdi-static-snapshot\n");
    }
public:
    UINT width, height;
    Desktop(UINT outputIndex, UINT longest, const std::wstring& outputName) {
        auto outputs=DesktopOutputs();
        auto selected=outputs.end();
        if(outputName.empty()) { if(outputIndex<outputs.size())selected=outputs.begin()+outputIndex; }
        else selected=std::find_if(outputs.begin(),outputs.end(),[&](const DesktopOutput& item){return outputName==item.description.DeviceName;});
        if(selected==outputs.end())throw std::runtime_error("Selected desktop monitor unavailable; choose a connected display");
        DXGI_OUTPUT_DESC monitorDesc=selected->description;
        originX = monitorDesc.DesktopCoordinates.left; originY = monitorDesc.DesktopCoordinates.top;
        Check(D3D11CreateDevice(selected->adapter.Get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &device, nullptr, &context), "desktop device");
        ComPtr<IDXGIOutput1> output1; Check(selected->output.As(&output1), "duplication output");
        Check(output1->DuplicateOutput(device.Get(), &duplication), "duplicate desktop");
        DXGI_OUTDUPL_DESC desc{}; duplication->GetDesc(&desc);
        sourceWidth = desc.ModeDesc.Width; sourceHeight = desc.ModeDesc.Height;
        rotation = desc.Rotation;
        if (rotation < DXGI_MODE_ROTATION_UNSPECIFIED || rotation > DXGI_MODE_ROTATION_ROTATE270)
            throw std::runtime_error("Unsupported desktop rotation");
        desktopWidth = SwapsAxes(rotation) ? sourceHeight : sourceWidth;
        desktopHeight = SwapsAxes(rotation) ? sourceWidth : sourceHeight;
        if (desktopWidth != (UINT)(monitorDesc.DesktopCoordinates.right-originX) ||
            desktopHeight != (UINT)(monitorDesc.DesktopCoordinates.bottom-originY))
            throw std::runtime_error("Desktop dimensions changed; reconnect to refresh capture");
        double scale = std::min(1.0, (double)longest / std::max(desktopWidth, desktopHeight));
        width = std::max(2u, ((UINT)(desktopWidth * scale)) & ~1u); height = std::max(2u, ((UINT)(desktopHeight * scale)) & ~1u);
        try {
            auto gpuRotation = (D3D11_VIDEO_PROCESSOR_ROTATION)(rotation <= DXGI_MODE_ROTATION_IDENTITY ? 0 : rotation-1);
            gpu = std::make_unique<GpuVideoConverter>(device.Get(), context.Get(), sourceWidth, sourceHeight, width, height, gpuRotation);
            fprintf(stderr,"conversion=gpu-nv12-readback\n");
        } catch(const std::exception& error) { fprintf(stderr,"conversion=cpu fallback=%s\n",error.what()); }
    }
    bool Read(std::vector<BYTE>& nv12) {
        DXGI_OUTDUPL_FRAME_INFO info{}; ComPtr<IDXGIResource> resource;
        HRESULT hr = duplication->AcquireNextFrame(0, &info, &resource);
        if (hr == DXGI_ERROR_WAIT_TIMEOUT) {
            if(!hasFrame && Clock::now()-created > std::chrono::milliseconds(250)) {
                InitialSnapshot(nv12);hasFrame=true;return true;
            }
            return false;
        }
        Check(hr, "desktop acquire");
        if(!pointerMetadataKnown || info.LastMouseUpdateTime.QuadPart!=0) {
            separatePointer=info.PointerPosition.Visible!=FALSE;
            pointerMetadataKnown=true;
        }
        struct ReleaseFrame { IDXGIOutputDuplication* duplication; ~ReleaseFrame(){duplication->ReleaseFrame();} } frame{duplication.Get()};
        ComPtr<ID3D11Texture2D> captured; Check(resource.As(&captured), "desktop texture");
        bool converted = false;
        if(gpu) {
            try { gpu->Convert(captured.Get(),nv12); converted = true; }
            catch(const std::exception& error) { fprintf(stderr,"conversion=cpu fallback=%s\n",error.what()); gpu.reset(); }
        }
        if(!converted) {
        if(!staging) {
            D3D11_TEXTURE2D_DESC texture{}; texture.Width = sourceWidth; texture.Height = sourceHeight; texture.MipLevels = texture.ArraySize = 1;
            texture.Format = DXGI_FORMAT_B8G8R8A8_UNORM; texture.SampleDesc.Count = 1; texture.Usage = D3D11_USAGE_STAGING; texture.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
            Check(device->CreateTexture2D(&texture, nullptr, &staging), "desktop staging");
        }
        context->CopyResource(staging.Get(), captured.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{}; Check(context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped), "desktop readback");
        // Compatibility conversion when GPU video processing is unavailable.
        ConvertCpu((BYTE*)mapped.pData,mapped.RowPitch,nv12);
        context->Unmap(staging.Get(), 0);
        }
        CURSORINFO cursor{}; cursor.cbSize = sizeof(cursor);
        bool systemCursorVisible = GetCursorInfo(&cursor) && (cursor.flags & CURSOR_SHOWING);
        // DXGI's embedded-pointer state does not distinguish a hidden system cursor.
        if(ShouldCompositeDesktopCursor(separatePointer,systemCursorVisible))
            DrawCursor(nv12,!separatePointer && !systemCursorVisible);
        hasFrame=true;return true;
    }
};

static uint32_t Get32(const unsigned char* p) { return ((uint32_t)p[0]<<24) | ((uint32_t)p[1]<<16) | ((uint32_t)p[2]<<8) | p[3]; }
static void VerifyPointer() {
    if(!ShouldCompositeDesktopCursor(true,true) || ShouldCompositeDesktopCursor(false,true) ||
        !ShouldCompositeDesktopCursor(false,false) || !ShouldCompositeDesktopCursor(true,false))
        throw std::runtime_error("Desktop cursor visibility selection failed");
    for(UINT size:{2u,12u,128u}) {
        for(POINT p:{POINT{0,0},POINT{(LONG)size-1,(LONG)size-1},POINT{-100,-100},POINT{INT_MAX,INT_MAX}}) {
            std::vector<BYTE> canvas((size_t)size*size*3/2,64);
            DrawDesktopPointer(canvas,size,size,p.x,p.y);
            if(p.x>=0 && p.y>=0 && p.x<(LONG)size && p.y<(LONG)size) {
                if(canvas[(size_t)p.y*size+p.x]!=16)throw std::runtime_error("Pointer tip missing");
            } else if(std::any_of(canvas.begin(),canvas.end(),[](BYTE b){return b!=64;}))
                throw std::runtime_error("Off-screen pointer changed canvas");
        }
    }
    std::vector<BYTE> invalid(1);
    bool refused=false;try{DrawDesktopPointer(invalid,2,2,0,0);}catch(const std::runtime_error&){refused=true;}
    if(!refused)throw std::runtime_error("Invalid pointer canvas accepted");
    printf("pointer tip, edge clipping, off-screen and canvas bounds verified\n");
}
static void VerifySystemCursor() {
    DesktopCursor image;
    const wchar_t* shapes[]={IDC_ARROW,IDC_IBEAM,IDC_HAND,IDC_WAIT,IDC_SIZEWE,IDC_CROSS};
    for(const auto shape:shapes) {
        HCURSOR cursor=LoadCursor(nullptr,shape);
        for(POINT point:{POINT{64,64},POINT{0,0},POINT{127,127}}) {
            std::vector<BYTE> canvas(128*128*3/2,128);std::fill(canvas.begin(),canvas.begin()+128*128,(BYTE)96);
            if(!image.Draw(canvas,128,128,point.x,point.y,128,128,cursor))throw std::runtime_error("System cursor draw failed");
            if(point.x==64 && std::none_of(canvas.begin(),canvas.begin()+128*128,[](BYTE b){return b!=96;}))
                throw std::runtime_error("System cursor shape invisible");
        }
    }
    std::vector<BYTE> andMask(32*32/8,255),xorMask(32*32/8,0);
    HCURSOR transparent=CreateCursor(GetModuleHandle(nullptr),5,7,32,32,andMask.data(),xorMask.data());
    if(!transparent)throw std::runtime_error("Transparent test cursor creation failed");
    std::vector<BYTE> canvas(64*64*3/2,128);for(size_t i=0;i<64*64;++i)canvas[i]=(BYTE)(16+i%220);
    auto original=canvas;
    if(!image.Draw(canvas,64,64,INT_MIN,INT_MAX,64,64,transparent) || canvas!=original)
        throw std::runtime_error("Off-screen system cursor changed background");
    bool drawn=image.Draw(canvas,64,64,32,32,64,64,transparent);DestroyCursor(transparent);
    if(!drawn || canvas!=original)throw std::runtime_error("Transparent cursor changed background");
    andMask[7*4]&=(BYTE)~(0x80>>5);xorMask[7*4]|=(BYTE)(0x80>>5);
    HCURSOR spot=CreateCursor(GetModuleHandle(nullptr),5,7,32,32,andMask.data(),xorMask.data());
    if(!spot)throw std::runtime_error("Hotspot test cursor creation failed");
    drawn=image.Draw(canvas,64,64,32,32,64,64,spot);DestroyCursor(spot);
    if(!drawn || canvas[32*64+32]!=235 || canvas[32*64+31]!=original[32*64+31])
        throw std::runtime_error("Cursor hotspot validation failed");
    std::vector<BYTE> large(1920*1080*3/2,128);std::fill(large.begin(),large.begin()+1920*1080,(BYTE)96);
    HCURSOR arrow=LoadCursor(nullptr,IDC_ARROW);
    if(!image.Draw(large,1920,1080,800,500,2560,1440,arrow))throw std::runtime_error("Cursor benchmark warmup failed");
    DWORD resources=GetGuiResources(GetCurrentProcess(),GR_GDIOBJECTS);
    auto start=Clock::now();
    for(int i=0;i<150;++i)if(!image.Draw(large,1920,1080,800,500,2560,1440,arrow))throw std::runtime_error("Cursor benchmark failed");
    double elapsed=std::chrono::duration<double,std::milli>(Clock::now()-start).count();
    if(GetGuiResources(GetCurrentProcess(),GR_GDIOBJECTS)>resources)throw std::runtime_error("Cursor GDI resources leaked");
    printf("system cursor shapes=6 edges, transparency, hotspot and resource reuse verified; mean-ms=%.3f\n",elapsed/150);
}
static void VerifyGpuConversion() {
    ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> context;
    Check(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context),"GPU test device");
    GpuVideoConverter converter(device.Get(),context.Get(),64,64,32,32);
    D3D11_TEXTURE2D_DESC desc{}; desc.Width=desc.Height=64;desc.MipLevels=desc.ArraySize=1;
    desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.Usage=D3D11_USAGE_DEFAULT;desc.BindFlags=D3D11_BIND_RENDER_TARGET;
    ComPtr<ID3D11Texture2D> texture;Check(device->CreateTexture2D(&desc,nullptr,&texture),"GPU test texture");
    const BYTE colors[3][3]={{0,0,255},{0,255,0},{255,0,0}};
    for(const auto& color:colors) {
        std::vector<BYTE> bgra(64*64*4), nv12;
        for(size_t i=0;i<bgra.size();i+=4){bgra[i]=color[0];bgra[i+1]=color[1];bgra[i+2]=color[2];bgra[i+3]=255;}
        context->UpdateSubresource(texture.Get(),0,nullptr,bgra.data(),64*4,0);
        converter.Convert(texture.Get(),nv12);
        int b=color[0],g=color[1],r=color[2];
        int expectedY=((66*r+129*g+25*b+128)>>8)+16;
        int expectedU=((-38*r-74*g+112*b+128)>>8)+128;
        int expectedV=((112*r-94*g-18*b+128)>>8)+128;
        int y=nv12[16*32+16],u=nv12[32*32+8*32+16],v=nv12[32*32+8*32+17];
        if(abs(y-expectedY)>8 || abs(u-expectedU)>8 || abs(v-expectedV)>8) throw std::runtime_error("GPU NV12 color validation failed");
        printf("gpu-color verified Y=%d U=%d V=%d\n",y,u,v);
    }
    desc.Width=64;desc.Height=32;
    ComPtr<ID3D11Texture2D> quadrants;
    Check(device->CreateTexture2D(&desc,nullptr,&quadrants),"rotation test texture");
    std::vector<BYTE> gray(64*32*4,255);
    const BYTE levels[4]={16,64,128,235};
    for(UINT y=0;y<32;++y)for(UINT x=0;x<64;++x) {
        BYTE level=levels[(y>=16?2:0)+(x>=32?1:0)];
        size_t offset=((size_t)y*64+x)*4;gray[offset]=gray[offset+1]=gray[offset+2]=level;
    }
    context->UpdateSubresource(quadrants.Get(),0,nullptr,gray.data(),64*4,0);
    const POINT topLeft[4]={{0,0},{0,31},{63,31},{63,0}};
    for(int i=0;i<4;++i) {
        auto rotation=(DXGI_MODE_ROTATION)(i+1);
        POINT first=DesktopSourcePoint(0,0,64,32,rotation);
        if(first.x!=topLeft[i].x || first.y!=topLeft[i].y)throw std::runtime_error("CPU rotation corner validation failed");
        UINT w=SwapsAxes(rotation)?32:64,h=SwapsAxes(rotation)?64:32;
        std::vector<bool> visited(64*32);
        for(UINT y=0;y<h;++y)for(UINT x=0;x<w;++x) {
            POINT p=DesktopSourcePoint(x,y,64,32,rotation);
            if(p.x<0 || p.y<0 || p.x>=64 || p.y>=32 || visited[(size_t)p.y*64+p.x])
                throw std::runtime_error("CPU rotation is not a bounded one-to-one mapping");
            visited[(size_t)p.y*64+p.x]=true;
        }
        GpuVideoConverter rotated(device.Get(),context.Get(),64,32,w,h,(D3D11_VIDEO_PROCESSOR_ROTATION)i);
        std::vector<BYTE> nv12;rotated.Convert(quadrants.Get(),nv12);
        for(UINT y=h/4;y<h;y+=h/2)for(UINT x=w/4;x<w;x+=w/2) {
            POINT p=DesktopSourcePoint(x,y,64,32,rotation);
            if(p.x<0 || p.y<0 || p.x>=64 || p.y>=32)throw std::runtime_error("Rotation coordinates out of bounds");
            int level=levels[(p.y>=16?2:0)+(p.x>=32?1:0)];
            int expected=((220*level+128)>>8)+16;
            if(abs(nv12[(size_t)y*w+x]-expected)>5)throw std::runtime_error("GPU rotation quadrant validation failed");
        }
        printf("gpu-rotation verified degrees=%d output=%ux%u\n",i*90,w,h);
    }
    GpuVideoConverter scaled(device.Get(),context.Get(),1920,1080,1280,720);
    desc.Width=1920;desc.Height=1080;ComPtr<ID3D11Texture2D> large;
    Check(device->CreateTexture2D(&desc,nullptr,&large),"GPU benchmark texture");
    ComPtr<ID3D11RenderTargetView> target;Check(device->CreateRenderTargetView(large.Get(),nullptr,&target),"GPU benchmark target");
    std::vector<BYTE> nv12; const auto start=Clock::now();
    for(int i=0;i<150;++i) { float color[4]={(i%100)/100.0f,0,0,1};context->ClearRenderTargetView(target.Get(),color);scaled.Convert(large.Get(),nv12); }
    double elapsed=std::chrono::duration<double,std::milli>(Clock::now()-start).count();
    printf("gpu-scale-readback frames=150 input=1920x1080 output=1280x720 mean-ms=%.3f\n",elapsed/150);
}
static void VerifyFile(const wchar_t* path) {
    std::ifstream file(path, std::ios::binary);
    if (!file) throw std::runtime_error("Cannot open synthetic media file");
    ComPtr<IMFTransform> decoder;
    Check(CoCreateInstance(CLSID_CMSH264DecoderMFT, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&decoder)), "create verification decoder");
    bool initialized = false; UINT width = 0, height = 0; unsigned decoded = 0, submitted = 0;
    auto setOutput = [&]() {
        for (DWORD i = 0; ; ++i) {
            ComPtr<IMFMediaType> type; Check(decoder->GetOutputAvailableType(0, i, &type), "decoder output type");
            GUID subtype{}; Check(type->GetGUID(MF_MT_SUBTYPE, &subtype), "decoder subtype");
            if (subtype == MFVideoFormat_NV12) { Check(decoder->SetOutputType(0, type.Get(), 0), "decoder NV12 output"); return; }
        }
    };
    auto drain = [&]() {
        while (true) {
            MFT_OUTPUT_STREAM_INFO info{}; Check(decoder->GetOutputStreamInfo(0, &info), "decoder output info");
            ComPtr<IMFSample> owned; MFT_OUTPUT_DATA_BUFFER output{};
            if (!(info.dwFlags & MFT_OUTPUT_STREAM_PROVIDES_SAMPLES)) {
                Check(MFCreateSample(&owned), "decoder output sample"); ComPtr<IMFMediaBuffer> buffer;
                Check(MFCreateMemoryBuffer(std::max<DWORD>(info.cbSize, width * height * 2), &buffer), "decoder output allocation");
                Check(owned->AddBuffer(buffer.Get()), "decoder output attach"); output.pSample = owned.Get();
            }
            DWORD status = 0; HRESULT hr = decoder->ProcessOutput(0, 1, &output, &status);
            if (output.pEvents) output.pEvents->Release();
            ComPtr<IMFSample> supplied; if (output.pSample && output.pSample != owned.Get()) supplied.Attach(output.pSample);
            if (hr == MF_E_TRANSFORM_NEED_MORE_INPUT) return;
            if (hr == MF_E_TRANSFORM_STREAM_CHANGE) { setOutput(); continue; }
            Check(hr, "decode verification output");
            if (output.pSample) { DWORD size = 0; Check(output.pSample->GetTotalLength(&size), "decoded buffer length"); if (size > 0) ++decoded; }
        }
    };
    while (true) {
        unsigned char header[32]; file.read((char*)header, 32);
        if (!file.gcount()) break;
        if (file.gcount() != 32 || memcmp(header, "HDV1", 4) || header[4] != 1) throw std::runtime_error("Invalid verification file header");
        UINT w = Get32(header+20), h = Get32(header+24), length = Get32(header+28);
        if (w<2 || h<2 || w>8192 || h>8192 || (w&1) || (h&1) || !length || length>4*1024*1024 || submitted>100000)
            throw std::runtime_error("Invalid verification packet dimensions or size");
        if (!initialized) {
            width=w; height=h; ComPtr<IMFMediaType> input; Check(MFCreateMediaType(&input), "decoder input type");
            input->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video); input->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264);
            MFSetAttributeSize(input.Get(), MF_MT_FRAME_SIZE, width, height); MFSetAttributeRatio(input.Get(), MF_MT_FRAME_RATE, 60, 1);
            Check(decoder->SetInputType(0,input.Get(),0), "decoder H264 input"); setOutput();
            Check(decoder->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING,0), "decoder begin");
            Check(decoder->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM,0), "decoder start"); initialized=true;
        } else if (w!=width || h!=height) throw std::runtime_error("Verification dimensions changed");
        ComPtr<IMFMediaBuffer> buffer; Check(MFCreateMemoryBuffer(length,&buffer), "verification input allocation"); BYTE* data;
        Check(buffer->Lock(&data,nullptr,nullptr), "verification input lock"); file.read((char*)data,length); buffer->Unlock();
        if ((UINT)file.gcount()!=length) throw std::runtime_error("Truncated verification access unit");
        buffer->SetCurrentLength(length); ComPtr<IMFSample> sample; MFCreateSample(&sample); sample->AddBuffer(buffer.Get());
        uint64_t pts=((uint64_t)Get32(header+12)<<32)|Get32(header+16); sample->SetSampleTime((LONGLONG)pts*10); sample->SetSampleDuration(10000000/60);
        Check(decoder->ProcessInput(0,sample.Get(),0), "verification decode input"); ++submitted; drain();
    }
    if (initialized) { decoder->ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM,0); decoder->ProcessMessage(MFT_MESSAGE_COMMAND_DRAIN,0); drain(); }
    if (!submitted || decoded+2 < submitted) throw std::runtime_error("Encoded stream failed decode verification");
    printf("verified submitted=%u decoded=%u dimensions=%ux%u\n",submitted,decoded,width,height);
}

int wmain(int argc, wchar_t** argv) {
    bool pattern = false, capture = false, software = false; UINT fps = 60, longest = 1920, bitrate = 8000000, monitor = 0, duration = 0, parentPid = 0;
    std::wstring monitorName;
    try {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        if(argc==2 && std::wstring(argv[1])==L"--verify-pointer") { VerifyPointer();return 0; }
        if(argc==2 && std::wstring(argv[1])==L"--verify-system-cursor") { VerifySystemCursor();return 0; }
        if(argc==2 && std::wstring(argv[1])==L"--list-monitors") {
            const auto outputs=DesktopOutputs();
            for(size_t i=0;i<outputs.size();++i) {
                const auto& d=outputs[i].description;
                printf("%zu\t%ls\t%ld\t%ld\t%u\n",i,d.DeviceName,
                    d.DesktopCoordinates.right-d.DesktopCoordinates.left,d.DesktopCoordinates.bottom-d.DesktopCoordinates.top,
                    d.Rotation<=DXGI_MODE_ROTATION_IDENTITY?0:(UINT)d.Rotation-1);
            }
            return 0;
        }
        if(argc==2 && std::wstring(argv[1])==L"--verify-gpu-conversion") {
            Check(CoInitializeEx(nullptr,COINIT_MULTITHREADED),"COM init");VerifyGpuConversion();CoUninitialize();return 0;
        }
        if (argc==3 && std::wstring(argv[1])==L"--verify-file") {
            Check(CoInitializeEx(nullptr,COINIT_MULTITHREADED),"COM init"); Check(MFStartup(MF_VERSION),"MF init");
            VerifyFile(argv[2]); MFShutdown(); CoUninitialize(); return 0;
        }
        for (int i = 1; i < argc; ++i) {
            std::wstring arg = argv[i];
            if (arg == L"--test-pattern") pattern = true;
            else if (arg == L"--capture") capture = true;
            else if (arg == L"--software") software = true;
            else if (arg == L"--fps" && i+1 < argc) fps = std::stoul(argv[++i]);
            else if (arg == L"--longest" && i+1 < argc) longest = std::stoul(argv[++i]);
            else if (arg == L"--bitrate" && i+1 < argc) bitrate = std::stoul(argv[++i]);
            else if (arg == L"--monitor" && i+1 < argc) monitor = std::stoul(argv[++i]);
            else if (arg == L"--monitor-name" && i+1 < argc) monitorName=argv[++i];
            else if (arg == L"--duration-ms" && i+1 < argc) duration = std::stoul(argv[++i]);
            else if (arg == L"--parent-pid" && i+1 < argc) parentPid = std::stoul(argv[++i]);
            else throw std::runtime_error("Unknown or missing desktop video argument");
        }
        if (pattern == capture || fps < 15 || fps > 120 || longest < 640 || longest > 3840 || bitrate < 1000000 || bitrate > 50000000 || monitor > 15 || duration > 3600000 || monitorName.size()>32)
            throw std::runtime_error("Specify --capture or --test-pattern and valid bounded settings");
        Check(CoInitializeEx(nullptr, COINIT_MULTITHREADED), "COM init"); Check(MFStartup(MF_VERSION), "Media Foundation init");
        _setmode(_fileno(stdout), _O_BINARY); setvbuf(stdout, nullptr, _IONBF, 0);
        {
            std::unique_ptr<Desktop> desktop;
            if (capture) desktop = std::make_unique<Desktop>(monitor, longest, monitorName);
            UINT width = desktop ? desktop->width : 1280, height = desktop ? desktop->height : 720;
            Encoder encoder(width, height, fps, bitrate, software);
            HANDLE parent = parentPid ? OpenProcess(SYNCHRONIZE, FALSE, parentPid) : nullptr;
            if (parentPid && !parent) throw std::runtime_error("Desktop owner process unavailable");
            std::vector<BYTE> pixels((size_t)width * height * 3 / 2, 128);
            TickWait tick;
            const auto start = Clock::now(); auto next = start; uint64_t submitted = 0;
            while (true) {
                auto now = Clock::now(); auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(now-start).count();
                if (parent && WaitForSingleObject(parent, 0) == WAIT_OBJECT_0) break;
                if (duration && ms >= duration) break;
                if (!encoder.Ready() || now < next) { tick.Wait(); continue; }
                next += std::chrono::microseconds(1000000 / fps);
                if (next < now) next = now + std::chrono::microseconds(1000000 / fps);
                if (desktop && !desktop->Read(pixels)) continue;
                if (pattern) {
                    std::fill(pixels.begin(), pixels.begin() + (size_t)width * height, (BYTE)(16 + submitted % 180));
                    UINT bar = (UINT)(submitted * 12 % width);
                    for (UINT y=0; y<height; ++y) for (UINT x=bar; x<std::min(width, bar+40); ++x) pixels[(size_t)y*width+x] = 235;
                }
                LONGLONG time = std::chrono::duration_cast<std::chrono::nanoseconds>(now-start).count() / 100;
                encoder.Submit(pixels, time); ++submitted;
            }
            auto end = Clock::now() + std::chrono::milliseconds(150);
            while (Clock::now() < end) { encoder.Poll(); tick.Wait(); }
            fprintf(stderr, "submitted=%llu encoded=%u\n", submitted, encoder.OutputCount());
            if (parent) CloseHandle(parent);
        }
        MFShutdown(); CoUninitialize(); return 0;
    } catch (const std::exception& error) { fprintf(stderr, "desktop-video error: %s\n", error.what()); return 1; }
}
