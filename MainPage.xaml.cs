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
namespace KinectTestApp
{
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

    public sealed partial class MainPage : Page
  {

        public MainPage()
    {
      this.InitializeComponent();
      this.Loaded += this.OnLoaded;
    }
    void OnCanvasControlSizeChanged(object sender, SizeChangedEventArgs e)
    {
      this.canvasSize = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
    }
    async void OnLoaded(object sender, RoutedEventArgs e)
    {
      this.helper = new mtKinectColorPoseFrameHelper();

      this.helper.ColorFrameArrived += OnColorFrameArrived;
      this.helper.PoseFrameArrived += OnPoseFrameArrived;

      var suppported = await this.helper.InitialiseAsync();

      if (suppported)
      {
        this.canvasControl.Visibility = Visibility.Visible;
      }
            // create client instance
            uPLibrary.Networking.M2Mqtt.MqttClient client = new MqttClient("127.0.0.1");

            string clientId = Guid.NewGuid().ToString();
            client.Connect(clientId);

            string strValue = Convert.ToString(1);

            // publish a message on "/home/temperature" topic with QoS 2
            client.Publish("/home/temperature", Encoding.UTF8.GetBytes(strValue), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);

            // create client instance
            MqttClient client2 = new MqttClient("127.0.0.1");

            // register to message received
            client.MqttMsgPublishReceived += client_MqttMsgPublishReceived;

            clientId = Guid.NewGuid().ToString();
            client.Connect(clientId);

            // subscribe to $SYS for one test status
            client.Subscribe(new string[] { "$SYS/1/uptime" }, new byte[] { MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE });
 
        }

        static void client_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            // handle message received
            string result = System.Text.Encoding.UTF8.GetString(e.Message);
        }
        void OnColorFrameArrived(object sender, mtSoftwareBitmapEventArgs e)
    {
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

