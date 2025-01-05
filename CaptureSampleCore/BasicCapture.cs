using Composition.WindowsRuntimeHelpers;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using SharpDX.Direct3D11;

namespace CaptureCore
{
    public class BasicCapture : IDisposable
    {
        private GraphicsCaptureItem item;
        private readonly Direct3D11CaptureFramePool framePool;
        private GraphicsCaptureSession session;
        private SizeInt32 lastSize;

        private readonly IDirect3DDevice device;
        private readonly Device d3dDevice;

        private bool _paused;
        private bool _awaitingFrame;

        public event EventHandler<Bitmap> FrameCaptured;

        public string Wormhole { get; set; }

        public BasicCapture(IDirect3DDevice d, GraphicsCaptureItem i)
        {
            item = i;
            device = d;
            d3dDevice = Direct3D11Helper.CreateSharpDXDevice(device);
            framePool = Direct3D11CaptureFramePool.Create(
                device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                i.Size);
            session = framePool.CreateCaptureSession(i);
            session.IsCursorCaptureEnabled = false;
            lastSize = i.Size;
            item.Closed += ItemClosed;
        }

        private void ItemClosed(GraphicsCaptureItem sender, object args)
        {
            Console.WriteLine("Graphics Item Closed!");
            item = null;
        }

        public void Dispose()
        {
            session?.Dispose();
            framePool?.Dispose();
            d3dDevice?.Dispose();
        }

        public GraphicsCaptureItem GetItem()
        {
            return item;
        }

        public bool IsAwaitingFrame()
        {
            return _awaitingFrame && !_paused;
        }

        public void StartCapture()
        {
            _paused = false;
            _awaitingFrame = true;
            framePool.FrameArrived += OnFrameArrived;
            session.StartCapture();
        }

        public void PauseCapture()
        {
            _paused = true;
            _awaitingFrame = false;
        }

        public void ResumeCapture()
        {
            _paused = false;
            _awaitingFrame = true;
        }

        public void StopCapture()
        {
            _paused = true;
            _awaitingFrame = false;
            framePool.FrameArrived -= OnFrameArrived;
            session?.Dispose();
            session = null;
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {

            var newSize = false;

            using (var frame = sender.TryGetNextFrame())
            {
                if (frame == null)
                {
                    Console.WriteLine("Null Frame!");
                    return;
                }

                _awaitingFrame = false;

                if (_paused)
                {
                    return;
                }

                if (frame.ContentSize.Width != lastSize.Width ||
                    frame.ContentSize.Height != lastSize.Height)
                {
                    newSize = true;
                    lastSize = frame.ContentSize;
                }

                using (var backBuffer = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface))
                {

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
