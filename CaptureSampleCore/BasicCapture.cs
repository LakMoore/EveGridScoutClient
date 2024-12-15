using Composition.WindowsRuntimeHelpers;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.UI.Composition;
using SharpDX.Direct3D11;

namespace CaptureSampleCore
{
    public class BasicCapture : IDisposable
    {
        private GraphicsCaptureItem item;
        private Direct3D11CaptureFramePool framePool;
        private GraphicsCaptureSession session;
        private SizeInt32 lastSize;

        private IDirect3DDevice device;
        private SharpDX.Direct3D11.Device d3dDevice;
        private SharpDX.DXGI.SwapChain1 swapChain;

        public event EventHandler<Bitmap> FrameCaptured;

        public BasicCapture(IDirect3DDevice d, GraphicsCaptureItem i)
        {
            item = i;
            device = d;
            d3dDevice = Direct3D11Helper.CreateSharpDXDevice(device);

            var dxgiFactory = new SharpDX.DXGI.Factory2();
            var description = new SharpDX.DXGI.SwapChainDescription1()
            {
                Width = item.Size.Width,
                Height = item.Size.Height,
                Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                Stereo = false,
                SampleDescription = new SharpDX.DXGI.SampleDescription()
                {
                    Count = 1,
                    Quality = 0
                },
                Usage = SharpDX.DXGI.Usage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = SharpDX.DXGI.Scaling.Stretch,
                SwapEffect = SharpDX.DXGI.SwapEffect.FlipSequential,
                AlphaMode = SharpDX.DXGI.AlphaMode.Premultiplied,
                Flags = SharpDX.DXGI.SwapChainFlags.None
            };
            swapChain = new SharpDX.DXGI.SwapChain1(dxgiFactory, d3dDevice, ref description);

            framePool = Direct3D11CaptureFramePool.Create(
                device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                i.Size);
            session = framePool.CreateCaptureSession(i);
            session.IsCursorCaptureEnabled = false;
            lastSize = i.Size;

            framePool.FrameArrived += OnFrameArrived;
        }

        public void Dispose()
        {
            session?.Dispose();
            framePool?.Dispose();
            swapChain?.Dispose();
            d3dDevice?.Dispose();
        }

        public void StartCapture()
        {
            session.StartCapture();
        }

        public void StopCapture()
        {
            session?.Dispose();
            session = null;
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            var newSize = false;

            using (var frame = sender.TryGetNextFrame())
            {
                if (frame == null) return;

                if (frame.ContentSize.Width != lastSize.Width ||
                    frame.ContentSize.Height != lastSize.Height)
                {
                    newSize = true;
                    lastSize = frame.ContentSize;
                    swapChain.ResizeBuffers(
                        2, 
                        lastSize.Width, 
                        lastSize.Height, 
                        SharpDX.DXGI.Format.B8G8R8A8_UNorm, 
                        SharpDX.DXGI.SwapChainFlags.None);
                }

                using (var backBuffer = swapChain.GetBackBuffer<SharpDX.Direct3D11.Texture2D>(0))
                using (var bitmap = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface))
                {
                    d3dDevice.ImmediateContext.CopyResource(bitmap, backBuffer);

                    if (FrameCaptured != null)
                    {
                        try
                        {
                            using (var tempTexture = new Texture2D(d3dDevice, new Texture2DDescription
                            {
                                Width = lastSize.Width,
                                Height = lastSize.Height,
                                ArraySize = 1,
                                BindFlags = BindFlags.None,
                                Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                                MipLevels = 1,
                                OptionFlags = ResourceOptionFlags.None,
                                SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                                Usage = ResourceUsage.Staging,
                                CpuAccessFlags = CpuAccessFlags.Read
                            }))
                            {
                                d3dDevice.ImmediateContext.CopyResource(backBuffer, tempTexture);
                                var dataBox = d3dDevice.ImmediateContext.MapSubresource(
                                    tempTexture,
                                    0,
                                    MapMode.Read,
                                    MapFlags.None);

                                var bmp = new Bitmap(lastSize.Width, lastSize.Height, PixelFormat.Format32bppArgb);
                                var bmpData = bmp.LockBits(
                                    new Rectangle(0, 0, bmp.Width, bmp.Height),
                                    ImageLockMode.WriteOnly,
                                    PixelFormat.Format32bppArgb);

                                try
                                {
                                    for (int y = 0; y < lastSize.Height; y++)
                                    {
                                        IntPtr sourcePtr = IntPtr.Add(dataBox.DataPointer, y * dataBox.RowPitch);
                                        IntPtr destPtr = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
                                        var buffer = new byte[Math.Min(dataBox.RowPitch, bmpData.Stride)];
                                        System.Runtime.InteropServices.Marshal.Copy(sourcePtr, buffer, 0, buffer.Length);
                                        System.Runtime.InteropServices.Marshal.Copy(buffer, 0, destPtr, buffer.Length);
                                    }

                                    bmp.UnlockBits(bmpData);
                                    d3dDevice.ImmediateContext.UnmapSubresource(tempTexture, 0);

                                    FrameCaptured?.Invoke(this, bmp);
                                }
                                catch
                                {
                                    bmp.Dispose();
                                    throw;
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // Log or handle the error as needed
                        }
                    }
                }
            }

            if (newSize)
            {
                framePool.Recreate(
                    device,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    lastSize);
            }
        }
    }
}
