#pragma once
#include <d3d11.h>
#include <wrl/client.h>
#include <cstdio>
#include <cstring>
#include <stdexcept>
#include <vector>

// shortcut: NV12 readback feeds memory-input MFTs; use surface input after driver validation.
class GpuVideoConverter {
    Microsoft::WRL::ComPtr<ID3D11Device> device;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> context;
    Microsoft::WRL::ComPtr<ID3D11VideoDevice> videoDevice;
    Microsoft::WRL::ComPtr<ID3D11VideoContext> videoContext;
    Microsoft::WRL::ComPtr<ID3D11VideoProcessorEnumerator> enumerator;
    Microsoft::WRL::ComPtr<ID3D11VideoProcessor> processor;
    Microsoft::WRL::ComPtr<ID3D11Texture2D> converted, readback;
    Microsoft::WRL::ComPtr<ID3D11Texture2D> inputCopy;
    Microsoft::WRL::ComPtr<ID3D11VideoProcessorInputView> inputView;
    Microsoft::WRL::ComPtr<ID3D11VideoProcessorOutputView> outputView;
    UINT width, height;
    static void Verify(HRESULT hr, const char* label) {
        if (FAILED(hr)) { char error[160]; sprintf_s(error, "%s: HRESULT 0x%08lX", label, hr); throw std::runtime_error(error); }
    }
public:
    GpuVideoConverter(ID3D11Device* d, ID3D11DeviceContext* c, UINT sourceWidth, UINT sourceHeight, UINT w, UINT h,
        D3D11_VIDEO_PROCESSOR_ROTATION rotation = D3D11_VIDEO_PROCESSOR_ROTATION_IDENTITY)
        : device(d), context(c), width(w), height(h) {
        Verify(device.As(&videoDevice), "D3D video device"); Verify(context.As(&videoContext), "D3D video context");
        D3D11_VIDEO_PROCESSOR_CONTENT_DESC desc{};
        desc.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
        desc.InputFrameRate = {60, 1}; desc.OutputFrameRate = {60, 1};
        desc.InputWidth = sourceWidth; desc.InputHeight = sourceHeight;
        desc.OutputWidth = w; desc.OutputHeight = h; desc.Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
        Verify(videoDevice->CreateVideoProcessorEnumerator(&desc, &enumerator), "video processor enumerate");
        UINT support = 0;
        Verify(enumerator->CheckVideoProcessorFormat(DXGI_FORMAT_B8G8R8A8_UNORM, &support), "video BGRA support");
        if (!(support & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_INPUT)) throw std::runtime_error("GPU BGRA input unsupported");
        Verify(enumerator->CheckVideoProcessorFormat(DXGI_FORMAT_NV12, &support), "video NV12 support");
        if (!(support & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_OUTPUT)) throw std::runtime_error("GPU NV12 output unsupported");
        Verify(videoDevice->CreateVideoProcessor(enumerator.Get(), 0, &processor), "video processor create");
        if (rotation != D3D11_VIDEO_PROCESSOR_ROTATION_IDENTITY) {
            D3D11_VIDEO_PROCESSOR_CAPS caps{};
            Verify(enumerator->GetVideoProcessorCaps(&caps), "video rotation capabilities");
            if (!(caps.FeatureCaps & D3D11_VIDEO_PROCESSOR_FEATURE_CAPS_ROTATION))
                throw std::runtime_error("GPU rotation unsupported");
            videoContext->VideoProcessorSetStreamRotation(processor.Get(), 0, TRUE, rotation);
        }
        D3D11_TEXTURE2D_DESC inputTexture{};inputTexture.Width=sourceWidth;inputTexture.Height=sourceHeight;
        inputTexture.MipLevels=inputTexture.ArraySize=1;inputTexture.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
        inputTexture.SampleDesc.Count=1;inputTexture.Usage=D3D11_USAGE_DEFAULT;inputTexture.BindFlags=D3D11_BIND_RENDER_TARGET;
        Verify(device->CreateTexture2D(&inputTexture,nullptr,&inputCopy),"GPU input copy texture");
        D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC inputDescription{};inputDescription.ViewDimension=D3D11_VPIV_DIMENSION_TEXTURE2D;
        Verify(videoDevice->CreateVideoProcessorInputView(inputCopy.Get(),enumerator.Get(),&inputDescription,&inputView),"GPU persistent input view");
        D3D11_TEXTURE2D_DESC texture{}; texture.Width=w; texture.Height=h;
        texture.MipLevels=texture.ArraySize=1; texture.Format=DXGI_FORMAT_NV12; texture.SampleDesc.Count=1;
        texture.Usage=D3D11_USAGE_DEFAULT; texture.BindFlags=D3D11_BIND_RENDER_TARGET;
        Verify(device->CreateTexture2D(&texture, nullptr, &converted), "GPU NV12 texture");
        texture.Usage=D3D11_USAGE_STAGING; texture.BindFlags=0; texture.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        Verify(device->CreateTexture2D(&texture, nullptr, &readback), "NV12 readback texture");
        D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC output{}; output.ViewDimension=D3D11_VPOV_DIMENSION_TEXTURE2D;
        Verify(videoDevice->CreateVideoProcessorOutputView(converted.Get(), enumerator.Get(), &output, &outputView), "NV12 output view");
        D3D11_VIDEO_PROCESSOR_COLOR_SPACE rgb{}; rgb.Nominal_Range=D3D11_VIDEO_PROCESSOR_NOMINAL_RANGE_0_255;
        D3D11_VIDEO_PROCESSOR_COLOR_SPACE yuv{}; yuv.Nominal_Range=D3D11_VIDEO_PROCESSOR_NOMINAL_RANGE_16_235;
        videoContext->VideoProcessorSetStreamColorSpace(processor.Get(), 0, &rgb);
        videoContext->VideoProcessorSetOutputColorSpace(processor.Get(), &yuv);
        videoContext->VideoProcessorSetStreamFrameFormat(processor.Get(), 0, D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
        videoContext->VideoProcessorSetStreamAutoProcessingMode(processor.Get(), 0, FALSE);
        RECT source{0,0,(LONG)sourceWidth,(LONG)sourceHeight}, destination{0,0,(LONG)w,(LONG)h};
        videoContext->VideoProcessorSetStreamSourceRect(processor.Get(),0,TRUE,&source);
        videoContext->VideoProcessorSetStreamDestRect(processor.Get(),0,TRUE,&destination);
        videoContext->VideoProcessorSetOutputTargetRect(processor.Get(),TRUE,&destination);
    }

    void Convert(ID3D11Texture2D* input, std::vector<BYTE>& nv12) {
        context->CopyResource(inputCopy.Get(),input);
        D3D11_VIDEO_PROCESSOR_STREAM stream{}; stream.Enable=TRUE; stream.pInputSurface=inputView.Get();
        Verify(videoContext->VideoProcessorBlt(processor.Get(),outputView.Get(),0,1,&stream), "GPU color conversion");
        context->CopyResource(readback.Get(),converted.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};
        Verify(context->Map(readback.Get(),0,D3D11_MAP_READ,0,&mapped), "NV12 map");
        struct Unmap { ID3D11DeviceContext* context; ID3D11Resource* texture; ~Unmap(){context->Unmap(texture,0);} } guard{context.Get(),readback.Get()};
        nv12.resize((size_t)width*height*3/2);
        for(UINT y=0;y<height;++y) memcpy(nv12.data()+(size_t)y*width,(BYTE*)mapped.pData+(size_t)y*mapped.RowPitch,width);
        const BYTE* uv=(BYTE*)mapped.pData+(size_t)mapped.RowPitch*height;
        for(UINT y=0;y<height/2;++y) memcpy(nv12.data()+(size_t)width*height+(size_t)y*width,uv+(size_t)y*mapped.RowPitch,width);
    }
};
