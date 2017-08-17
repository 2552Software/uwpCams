/* MIT License

started with Mike's code

Copyright(c) 2016 Mike Taulty

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System.Numerics;
using System.Threading;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Windows.Storage;
using Xunit;
using Xunit.Abstractions;
using System.Reflection;
using System.Net.Security;
using System.Net;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using System.Text;
using Windows.Media.MediaProperties;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Devices.Enumeration;
using Windows.Graphics.Display;
using Windows.Media;
using Windows.Media.Capture;
using Windows.System.Display;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using System.IO;

// lots of maybe good camera stuff down the road https://github.com/Microsoft/Windows-universal-samples/blob/master/Samples/CameraGetPreviewFrame/cs/MainPage.xaml.cs
namespace KinectTestApp
{
    [ComImport]
    [Guid("5b0d3235-4dba-4d44-865e-8f1d0e4fd04d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    public sealed partial class MainPage : Page
    {
        private static async Task SaveSoftwareBitmapAsync(SoftwareBitmap bitmap, StorageFile file)
        {
            using (var outputStream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outputStream);

                // Grab the data from the SoftwareBitmap
                encoder.SetSoftwareBitmap(bitmap);
                await encoder.FlushAsync();
            }
        }
        private unsafe void EditPixels(SoftwareBitmap bitmap)
        {
            // Effect is hard-coded to operate on BGRA8 format only
            if (bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8)
            {
                // In BGRA8 format, each pixel is defined by 4 bytes
                const int BYTES_PER_PIXEL = 4;

                using (var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.ReadWrite))
                using (var reference = buffer.CreateReference())
                {
                    // Get a pointer to the pixel buffer
                    byte* data;
                    uint capacity;
                    ((IMemoryBufferByteAccess)reference).GetBuffer(out data, out capacity);

                    // Get information about the BitmapBuffer
                    var desc = buffer.GetPlaneDescription(0);

                    // Iterate over all pixels
                    for (uint row = 0; row < desc.Height; row++)
                    {
                        for (uint col = 0; col < desc.Width; col++)
                        {
                            // Index of the current pixel in the buffer (defined by the next 4 bytes, BGRA8)
                            var currPixel = desc.StartIndex + desc.Stride * row + BYTES_PER_PIXEL * col;

                            // Read the current pixel information into b,g,r channels (leave out alpha channel)
                            var b = data[currPixel + 0]; // Blue
                            var g = data[currPixel + 1]; // Green
                            var r = data[currPixel + 2]; // Red

                            // Boost the green channel, leave the other two untouched
                            data[currPixel + 0] = b;
                            data[currPixel + 1] = (byte)Math.Min(g + 80, 255);
                            data[currPixel + 2] = r;
                        }
                    }
                }
            }
        }

    static string clientId;
    static uPLibrary.Networking.M2Mqtt.MqttClient client;
    public MainPage()
    {
        this.InitializeComponent();
      this.Loaded += this.OnLoaded;
            newBitmap = BitmapFactory.New(512, 512);
        }

        WriteableBitmap newBitmap;
        public byte[] ConvertBitmapToByteArray(SoftwareBitmap bitmap)
        {
            // this code is so horrible I will never use C# or C++ from MS unless its the only choice
            newBitmap.Resize(bitmap.PixelWidth, bitmap.PixelHeight, WriteableBitmapExtensions.Interpolation.Bilinear);
            bitmap.CopyToBuffer(newBitmap.PixelBuffer);
            using (Stream stream = newBitmap.PixelBuffer.AsStream())
            using (MemoryStream memoryStream = new MemoryStream())
            {
                stream.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
        }
        void OnCanvasControlSizeChanged(object sender, SizeChangedEventArgs e)
    {
      this.canvasSize = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
    }
    async void OnLoaded(object sender, RoutedEventArgs e)
    {
            DispatcherTimerSetup();

            this.helper = new mtKinectColorPoseFrameHelper();

            this.helper.ColorFrameArrived += OnColorFrameArrived;
      this.helper.PoseFrameArrived += OnPoseFrameArrived;

      var suppported = await this.helper.InitialiseAsync();

      if (suppported)
      {
        this.canvasControl.Visibility = Visibility.Visible;
      }
        // create client instance
        client = new MqttClient("127.0.0.1");

        clientId = Guid.NewGuid().ToString();
        client.Connect(clientId);

        // register to message received
        client.MqttMsgPublishReceived += client_MqttMsgPublishReceived;

        // subscribe to $SYS for one test status
        client.Subscribe(new string[] { "$SYS/1/uptime" }, new byte[] { MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE });
 
    }
    static void send(byte[] data)
    {
        client.Publish("kinect.1.color", data, MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
    }
    static void client_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
    {
        // handle message received
        string result = System.Text.Encoding.UTF8.GetString(e.Message);
    }
        private async Task<byte[]> EncodeJpeg(WriteableBitmap bmp)
        {
            SoftwareBitmap soft = SoftwareBitmap.CreateCopyFromBuffer(bmp.PixelBuffer, BitmapPixelFormat.Bgra8, bmp.PixelWidth, bmp.PixelHeight);
            byte[] array = null;

            using (var ms = new InMemoryRandomAccessStream())
            {
                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, ms);
                encoder.SetSoftwareBitmap(soft);

                try
                {
                    await encoder.FlushAsync();
                }
                catch { }

                array = new byte[ms.Size];
                await ms.ReadAsync(array.AsBuffer(), (uint)ms.Size, InputStreamOptions.None);
            }

            return array;
        }
        DispatcherTimer dispatcherTimer;
        DateTimeOffset startTime;
        DateTimeOffset lastTime;
        DateTimeOffset stopTime;
        int timesTicked = 1;
        int timesToTick = 10;
        public void DispatcherTimerSetup()
        {
            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Tick += dispatcherTimer_Tick;
            dispatcherTimer.Interval = new TimeSpan(0, 0, 1);
            //IsEnabled defaults to false
            startTime = DateTimeOffset.Now;
            lastTime = startTime;
            dispatcherTimer.Start();
            //IsEnabled should now be true after calling start
        }
        //https://docs.microsoft.com/en-us/uwp/api/windows.ui.xaml.dispatchertimer needed to run in the UI thread
        void dispatcherTimer_Tick(object sender, object e)
        {
            // doto bugbug let tick run for ever, just convert and send when there is new data
            SoftwareBitmap bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 10, 10);
            byte[] data = ConvertBitmapToByteArray(bitmap);

            DateTimeOffset time = DateTimeOffset.Now;
            TimeSpan span = time - lastTime;
            lastTime = time;
            //Time since last tick should be very very close to Interval
            timesTicked++;
            if (timesTicked > timesToTick)
            {
                stopTime = time;
                dispatcherTimer.Stop();
                //IsEnabled should now be false after calling stop
                span = stopTime - startTime;
            }
        }
        void OnColorFrameArrived(object sender, mtSoftwareBitmapEventArgs e)
    {
            //Task < byte[] > tsk = EncodeJpeg(e.writeablebitmap);
            //bugbug do this for depth etc using cool examples tsk.Wait();
            //send(e.data);
            // Note that when this function returns to the caller, we have
            // finished with the incoming software bitmap.
            if (this.bitmapSize == null)
      {
        this.bitmapSize = new Rect(0, 0, e.Bitmap.PixelWidth, e.Bitmap.PixelHeight);
      }

      if (Interlocked.CompareExchange(ref this.isBetweenRenderingPass, 1, 0) == 0)
      {
        this.lastConvertedColorBitmap?.Dispose();

        // Sadly, the format that comes in here, isn't supported by Win2D when
        // it comes to drawing so we have to convert. The upside is that 
        // we know we can keep this bitmap around until we are done with it.
        this.lastConvertedColorBitmap = SoftwareBitmap.Convert(
          e.Bitmap,
          BitmapPixelFormat.Bgra8,
          BitmapAlphaMode.Ignore);

        // Cause the canvas control to redraw itself.
        this.InvalidateCanvasControl();
      }
    }
    void InvalidateCanvasControl()
    {
      // Fire and forget.
      this.Dispatcher.RunAsync(CoreDispatcherPriority.High, this.canvasControl.Invalidate);
    }
    void OnPoseFrameArrived(object sender, mtPoseTrackingFrameEventArgs e)
    {
      // NB: we do not invalidate the control here but, instead, just keep
      // this frame around (maybe) until the colour frame redraws which will 
      // (depending on race conditions) pick up this frame and draw it
      // too.
      this.lastPoseEventArgs = e;
    }
    void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
      // Capture this here (in a race) in case it gets over-written
      // while this function is still running.
      var poseEventArgs = this.lastPoseEventArgs;

      args.DrawingSession.Clear(Colors.Black);

      // Do we have a colour frame to draw?
      if (this.lastConvertedColorBitmap != null)
      {
        using (var canvasBitmap = CanvasBitmap.CreateFromSoftwareBitmap(
          this.canvasControl,
          this.lastConvertedColorBitmap))
        {
          // Draw the colour frame
          args.DrawingSession.DrawImage(
            canvasBitmap,
            this.canvasSize,
            this.bitmapSize.Value);

          // Have we got a skeletal frame hanging around?
          if (poseEventArgs?.PoseEntries?.Length > 0)
          {
            foreach (var entry in poseEventArgs.PoseEntries)
            {
              foreach (var pose in entry.Points)
              {
                var centrePoint = ScalePosePointToDrawCanvasVector2(pose);

                args.DrawingSession.FillCircle(
                  centrePoint, circleRadius, Colors.Red);
              }
            }
          }
        }
      }
      Interlocked.Exchange(ref this.isBetweenRenderingPass, 0);
    }
    Vector2 ScalePosePointToDrawCanvasVector2(Point posePoint)
    {
      return (new Vector2(
        (float)((posePoint.X / this.bitmapSize.Value.Width) * this.canvasSize.Width),
        (float)((posePoint.Y / this.bitmapSize.Value.Height) * this.canvasSize.Height)));
    }
    Rect? bitmapSize;
    Rect canvasSize;
    int isBetweenRenderingPass;
    SoftwareBitmap lastConvertedColorBitmap;
    mtPoseTrackingFrameEventArgs lastPoseEventArgs;
    mtKinectColorPoseFrameHelper helper;
    static readonly float circleRadius = 10.0f;
  }
}

