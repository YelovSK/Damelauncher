using System.Drawing;
using Microsoft.Win32.SafeHandles;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct2D1.D2D1;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DirectWrite.DWrite;

namespace Damelauncher.Rendering;

/// <summary>Owns the graphics device and the frame lifecycle.</summary>
internal sealed class D2DRenderer : IDisposable
{
    private const uint BufferCount = 2;

    private readonly ID2D1Factory1 _d2dFactory;
    private readonly IDWriteFactory1 _directWriteFactory;
    private readonly DeviceResources _device;
    private ID2D1Bitmap1? _targetBitmap;
    private ID3D11RenderTargetView? _targetView;
    private int _width;
    private int _height;
    private float _dpi;

    internal D2DRenderer(nint window, int width, int height, float dpi)
    {
        _width = width;
        _height = height;
        _dpi = dpi;

        _d2dFactory = D2D1CreateFactory<ID2D1Factory1>();
        _directWriteFactory = DWriteCreateFactory<IDWriteFactory1>();
        _device = new DeviceResources(CreateDevice(), _d2dFactory, window, width, height, dpi);
        CreateTargetBitmap();
    }

    internal nint FrameLatencyWaitHandle => _device.FrameLatencyWaitHandle.DangerousGetHandle();
    internal ID2D1DeviceContext DeviceContext => _device.DeviceContext;
    internal IDWriteFactory DirectWriteFactory => _directWriteFactory;

    internal void Render(Action<SizeF> draw, Color4 background)
    {
        if (_width <= 0 || _height <= 0)
        {
            return;
        }

        // Match the premultiplied Direct2D target without splitting its draw batch.
        _device.D3DContext.ClearRenderTargetView(_targetView!, new Color4(
            background.R * background.A,
            background.G * background.A,
            background.B * background.A,
            background.A));
        DeviceContext.BeginDraw();
        draw(new SizeF(_width, _height));
        DeviceContext.EndDraw().CheckError();
        _device.SwapChain.Present(1, PresentFlags.None).CheckError();
    }

    internal void Resize(int width, int height)
    {
        if (width == _width && height == _height)
        {
            return;
        }

        _width = width;
        _height = height;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        ReleaseTargetBitmap();
        _device.SwapChain.ResizeBuffers(
            BufferCount,
            (uint)width,
            (uint)height,
            Format.B8G8R8A8_UNorm,
            SwapChainFlags.FrameLatencyWaitableObject).CheckError();
        CreateTargetBitmap();
    }

    internal void SetDpi(float dpi)
    {
        _dpi = dpi;
        DeviceContext.SetDpi(dpi, dpi);

        if (_width > 0 && _height > 0)
        {
            ReleaseTargetBitmap();
            CreateTargetBitmap();
        }
    }

    public void Dispose()
    {
        ReleaseTargetBitmap();
        _device.Dispose();
        _directWriteFactory.Dispose();
        _d2dFactory.Dispose();
    }

    private static ID3D11Device CreateDevice()
    {
        return D3D11CreateDevice(
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            Vortice.Direct3D.FeatureLevel.Level_11_1,
            Vortice.Direct3D.FeatureLevel.Level_11_0,
            Vortice.Direct3D.FeatureLevel.Level_10_1,
            Vortice.Direct3D.FeatureLevel.Level_10_0);
    }

    private void CreateTargetBitmap()
    {
        using IDXGISurface surface = _device.SwapChain.GetBuffer<IDXGISurface>(0);
        using ID3D11Texture2D texture = surface.QueryInterface<ID3D11Texture2D>();
        _targetView = _device.D3DDevice.CreateRenderTargetView(texture);
        BitmapProperties1 properties = new(
            new PixelFormat(
                Format.B8G8R8A8_UNorm,
                Vortice.DCommon.AlphaMode.Premultiplied),
            _dpi,
            _dpi,
            BitmapOptions.Target | BitmapOptions.CannotDraw);

        _targetBitmap = DeviceContext.CreateBitmapFromDxgiSurface(surface, properties);
        DeviceContext.Target = _targetBitmap;
    }

    private void ReleaseTargetBitmap()
    {
        DeviceContext.Target = null;
        _targetBitmap?.Dispose();
        _targetBitmap = null;
        _targetView?.Dispose();
        _targetView = null;
    }

    /// <summary>
    /// Everything built from one Direct3D device, which it takes ownership of. It is released
    /// as a unit, also when its creation fails partway.
    /// </summary>
    private sealed class DeviceResources : IDisposable
    {
        internal DeviceResources(
            ID3D11Device device,
            ID2D1Factory1 d2dFactory,
            nint window,
            int width,
            int height,
            float dpi)
        {
            D3DDevice = device;
            try
            {
                DxgiDevice = D3DDevice.QueryInterface<IDXGIDevice>();
                D3DContext = D3DDevice.ImmediateContext;
                using IDXGIAdapter adapter = DxgiDevice.GetAdapter();
                DxgiFactory = adapter.GetParent<IDXGIFactory2>();

                D2DDevice = d2dFactory.CreateDevice(DxgiDevice);
                DeviceContext = D2DDevice.CreateDeviceContext();
                DeviceContext.SetDpi(dpi, dpi);

                SwapChainDescription1 description = new(
                    (uint)Math.Max(width, 1),
                    (uint)Math.Max(height, 1),
                    Format.B8G8R8A8_UNorm,
                    false,
                    Usage.RenderTargetOutput,
                    BufferCount,
                    Scaling.Stretch,
                    SwapEffect.FlipSequential,
                    Vortice.DXGI.AlphaMode.Ignore,
                    SwapChainFlags.FrameLatencyWaitableObject);

                using IDXGISwapChain1 swapChain = DxgiFactory.CreateSwapChainForHwnd(
                    D3DDevice,
                    window,
                    description);
                SwapChain = swapChain.QueryInterface<IDXGISwapChain2>();
                SwapChain.MaximumFrameLatency = 1;
                FrameLatencyWaitHandle = new SafeWaitHandle(
                    SwapChain.FrameLatencyWaitableObject,
                    ownsHandle: true);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal ID3D11Device D3DDevice { get; }
        internal ID3D11DeviceContext D3DContext { get; }
        internal IDXGIDevice DxgiDevice { get; }
        internal IDXGIFactory2 DxgiFactory { get; }
        internal ID2D1Device D2DDevice { get; }
        internal ID2D1DeviceContext DeviceContext { get; }
        internal IDXGISwapChain2 SwapChain { get; }
        internal SafeWaitHandle FrameLatencyWaitHandle { get; }

        // Null-tolerant, because a failed construction leaves only some of these assigned.
        public void Dispose()
        {
            // A swap chain only leaves its window once nothing references its buffers any more,
            // so the pipeline has to be cleared and flushed before the chain is dropped.
            D3DContext?.ClearState();
            D3DContext?.Flush();

            DeviceContext?.Dispose();
            D2DDevice?.Dispose();
            FrameLatencyWaitHandle?.Dispose();
            SwapChain?.Dispose();
            DxgiFactory?.Dispose();
            DxgiDevice?.Dispose();
            D3DContext?.Dispose();
            D3DDevice.Dispose();
        }
    }
}
