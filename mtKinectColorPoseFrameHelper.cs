namespace KinectTestApp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using System.Threading.Tasks;
    using Windows.Media.Capture;
    using Windows.Media.Capture.Frames;
    using Windows.Media.Devices.Core;
    using Windows.Perception.Spatial;
    using WindowsPreview.Media.Capture.Frames;
    using Windows.Foundation;
    using Windows.Graphics.Imaging;
    using Windows.UI.Xaml.Media.Imaging;
    using System.IO;
    using System.Runtime.InteropServices.WindowsRuntime;
    using Windows.Storage.Streams;
    using Windows.System.Threading;
    using System.Collections.Concurrent;

    class mtSoftwareBitmapEventArgs : EventArgs
    {
        public mtSoftwareBitmapEventArgs()
        {
        }
        public SoftwareBitmap Bitmap { get; set; } // SoftwareBitmap is why C# and MS is so annyoing, just remove it
    }
    class mtPoseTrackingFrameEventArgs : EventArgs
    {
        public mtPoseTrackingDetails[] PoseEntries { get; set; }
    }
    class mtPoseTrackingDetails
    {
        public Guid EntityId { get; set; }
        public Point[] Points { get; set; }

        public static mtPoseTrackingDetails FromPoseTrackingEntity(
          PoseTrackingEntity poseTrackingEntity,
          CameraIntrinsics colorIntrinsics,
          Matrix4x4 depthColorTransform)
        {
            mtPoseTrackingDetails details = null;

            var poses = new TrackedPose[poseTrackingEntity.PosesCount];
            poseTrackingEntity.GetPoses(poses);

            var points = new Point[poses.Length];

            colorIntrinsics.ProjectManyOntoFrame(
              poses.Select(p => Multiply(depthColorTransform, p.Position)).ToArray(),
              points);

            details = new mtPoseTrackingDetails()
            {
                EntityId = poseTrackingEntity.EntityId,
                Points = points
            };
            return (details);
        }
        static Vector3 Multiply(Matrix4x4 matrix, Vector3 position)
        {
            return (new Vector3(
              position.X * matrix.M11 + position.Y * matrix.M21 + position.Z * matrix.M31 + matrix.M41,
              position.X * matrix.M12 + position.Y * matrix.M22 + position.Z * matrix.M32 + matrix.M42,
              position.X * matrix.M13 + position.Y * matrix.M23 + position.Z * matrix.M33 + matrix.M43));
        }
    }
    class MediaReader
    {
        Func<MediaFrameSource, bool> additionalSourceCriteria;
        Action<MediaFrameReader> onFrameArrived;
        MediaFrameReader frameReader;
        MediaFrameSource mediaSource;
        MediaCapture mediaCapture;
        MediaFrameSourceKind mediaSourceKind;

        public MediaReader(
              MediaCapture capture,
              MediaFrameSourceKind mediaSourceKind,
              Action<MediaFrameReader> onFrameArrived,
              Func<MediaFrameSource, bool> additionalSourceCriteria = null)
        {
            this.mediaCapture = capture;
            this.mediaSourceKind = mediaSourceKind;
            this.additionalSourceCriteria = additionalSourceCriteria;
            this.onFrameArrived = onFrameArrived;
        }

        public bool Initialise()
        {
            this.mediaSource = this.mediaCapture.FrameSources.FirstOrDefault(
              fs =>
                (fs.Value.Info.SourceKind == this.mediaSourceKind) &&
                ((this.additionalSourceCriteria != null) ?
                  this.additionalSourceCriteria(fs.Value) : true)).Value;

            return (this.mediaSource != null);
        }
        public async Task OpenReaderAsync()
        {
            this.frameReader =
              await this.mediaCapture.CreateFrameReaderAsync(this.mediaSource);

            this.frameReader.FrameArrived +=
              (s, e) =>
              {
                  this.onFrameArrived(s);
              };

            await this.frameReader.StartAsync();
        }

    }
    class MediaSourceReaders
    {
        MediaCapture mediaCapture;

        public event EventHandler<mtSoftwareBitmapEventArgs> ColorFrameArrived;
        public event EventHandler<mtPoseTrackingFrameEventArgs> PoseFrameArrived;
        SpatialCoordinateSystem colorCoordinateSystem;
        mtSoftwareBitmapEventArgs softwareBitmapEventArgs = new mtSoftwareBitmapEventArgs();
        MediaReader[] mediaSourceReaders;
        CameraIntrinsics camIntrinsics;
        const string PerceptionFormat = "Perception";
        private Matrix4x4? depthColorTransform;
        static string clientId;
        static uPLibrary.Networking.M2Mqtt.MqttClient client;
        WriteableBitmap newBitmap;

        static ConcurrentQueue<SoftwareBitmap> q = new ConcurrentQueue<SoftwareBitmap>();

        static public byte[] ConvertBitmapToByteArray(SoftwareBitmap bitmap)
        {
            // this code is so horrible I will never use C# or C++ from MS unless its the only choice
            WriteableBitmap newBitmap = new WriteableBitmap(bitmap.PixelWidth, bitmap.PixelHeight);
            bitmap.CopyToBuffer(newBitmap.PixelBuffer); 
            using (Stream stream = newBitmap.PixelBuffer.AsStream())
            using (MemoryStream memoryStream = new MemoryStream())
            {
                stream.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
        }

        static ThreadPoolTimer timer = ThreadPoolTimer.CreatePeriodicTimer((t) =>
        {
            //do some work \ dispatch to UI thread as needed
            // An action to consume the ConcurrentQueue.
            SoftwareBitmap bitmap;
            while (q.TryDequeue(out bitmap)) {
                byte[] data = ConvertBitmapToByteArray(bitmap);
            }

        }, TimeSpan.FromMilliseconds(1000));

        private static async Task SaveSoftwareBitmapAsync(SoftwareBitmap bitmap, Windows.Storage.StorageFile file)
        {
            using (var outputStream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
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
       
        internal async Task<bool> InitialiseAsync()
        {
            bool necessarySourcesAvailable = false;

            // Find all possible source groups.
            var sourceGroups = await MediaFrameSourceGroup.FindAllAsync();

            // We try to find the Kinect by asking for a group that can deliver
            // color, depth, custom and infrared. todo support all cams including intel motion?
            var allGroups = await GetGroupsSupportingSourceKindsAsync(
              MediaFrameSourceKind.Color,
              MediaFrameSourceKind.Depth,
              MediaFrameSourceKind.Custom,
              MediaFrameSourceKind.Infrared);

            // We assume the first group here is what we want which is not
            // necessarily going to be right on all systems so would need
            // more care.
            var firstSourceGroup = allGroups.FirstOrDefault();

            // Got one that supports all those types?
            if (firstSourceGroup != null)
            {
                this.mediaCapture = new MediaCapture();

                var captureSettings = new MediaCaptureInitializationSettings()
                {
                    SourceGroup = firstSourceGroup,
                    SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu
                };
                await this.mediaCapture.InitializeAsync(captureSettings);

                this.mediaSourceReaders = new MediaReader[]
                {
                      new MediaReader(this.mediaCapture, MediaFrameSourceKind.Color, this.OnFrameArrived),
                      new MediaReader(this.mediaCapture, MediaFrameSourceKind.Depth, this.OnFrameArrived),
                      new MediaReader(this.mediaCapture, MediaFrameSourceKind.Infrared, this.OnFrameArrived),
                      new MediaReader(this.mediaCapture, MediaFrameSourceKind.Custom, this.OnFrameArrived,
                        DoesCustomSourceSupportPerceptionFormat)
                };

                necessarySourcesAvailable =
                  this.mediaSourceReaders.All(reader => reader.Initialise());

                if (necessarySourcesAvailable)
                {
                    foreach (var reader in this.mediaSourceReaders)
                    {
                        await reader.OpenReaderAsync();
                    }
                }
                else
                {
                    this.mediaCapture.Dispose();
                }
            }
            return (necessarySourcesAvailable);
        }
        void OnFrameArrived(MediaFrameReader sender)
        {
            var frame = sender.TryAcquireLatestFrame();

            if (frame != null)
            {
                switch (frame.SourceKind)
                {
                    case MediaFrameSourceKind.Custom:
                        this.ProcessCustomFrame(frame);
                        break;
                    case MediaFrameSourceKind.Color:
                        this.ProcessColorFrame(frame);
                        break;
                    case MediaFrameSourceKind.Infrared:
                        this.ProcessIRFrame(frame);
                        break;
                    case MediaFrameSourceKind.Depth:
                        this.ProcessDepthFrame(frame);
                        break;
                    default:
                        break;
                }
                frame.Dispose();
            }
        }
        void ProcessIRFrame(MediaFrameReference frame)
        {
            if (this.colorCoordinateSystem != null)
            {
                this.depthColorTransform = frame.CoordinateSystem.TryGetTransformTo(this.colorCoordinateSystem);
            }
        }
        void ProcessDepthFrame(MediaFrameReference frame)
        {
            if (this.colorCoordinateSystem != null)
            {
                this.depthColorTransform = frame.CoordinateSystem.TryGetTransformTo(this.colorCoordinateSystem);
            }
            if (this.camIntrinsics == null)
            {
                this.camIntrinsics = frame.VideoMediaFrame.CameraIntrinsics;
            }

        }
        void ProcessColorFrame(MediaFrameReference frame)
        {
            if (this.colorCoordinateSystem == null)
            {
                this.colorCoordinateSystem = frame.CoordinateSystem;
                this.camIntrinsics = frame.VideoMediaFrame.CameraIntrinsics;
            }

            this.softwareBitmapEventArgs.Bitmap = frame.VideoMediaFrame.SoftwareBitmap;
            q.Enqueue(frame.VideoMediaFrame.SoftwareBitmap);
            this.ColorFrameArrived?.Invoke(this, this.softwareBitmapEventArgs);
        }
        void ProcessCustomFrame(MediaFrameReference frame)
        {
            if ((this.PoseFrameArrived != null) && (this.colorCoordinateSystem != null))
            {
                var trackingFrame = PoseTrackingFrame.Create(frame);
                if (trackingFrame.Status == PoseTrackingFrameCreationStatus.Success)
                {
                    var eventArgs = new mtPoseTrackingFrameEventArgs();
                    // Which of the entities here are actually tracked?
                    var trackedEntities = trackingFrame.Frame.Entities.Where(e => e.IsTracked).ToArray();

                    var trackedCount = trackedEntities.Count();

                    if (trackedCount > 0)
                    {
                        eventArgs.PoseEntries =
                           trackedEntities
                           .Select(entity =>
                           mtPoseTrackingDetails.FromPoseTrackingEntity(entity, this.camIntrinsics, this.depthColorTransform.Value))
                           .ToArray();
                    }
                    this.PoseFrameArrived(this, eventArgs);
                }
            }
        }
        async static Task<IEnumerable<MediaFrameSourceGroup>> GetGroupsSupportingSourceKindsAsync(
          params MediaFrameSourceKind[] kinds)
        {
            var sourceGroups = await MediaFrameSourceGroup.FindAllAsync();

            var groups =
              sourceGroups.Where(
                group => kinds.All(
                  kind => group.SourceInfos.Any(sourceInfo => sourceInfo.SourceKind == kind)));

            return (groups);
        }
        static bool DoesCustomSourceSupportPerceptionFormat(MediaFrameSource source)
        {
            return (
              (source.Info.SourceKind == MediaFrameSourceKind.Custom) &&
              (source.CurrentFormat.MajorType == PerceptionFormat) &&
              (Guid.Parse(source.CurrentFormat.Subtype) == PoseTrackingFrame.PoseTrackingSubtype));
        }
    }

}
