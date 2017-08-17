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

    class mtSoftwareBitmapEventArgs : EventArgs
    {
        public WriteableBitmap writeablebitmap;

        public SoftwareBitmap Bitmap { get; set; } // SoftwareBitmap is why C# and MS is so annyoing, just remove it
        public void Set(SoftwareBitmap newBitmap)
        {
            Bitmap = newBitmap; // likely way too many copies here but once working Bitmap will just go away
        }
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
    class mtMediaSourceReader
    {
        public mtMediaSourceReader(
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
        Func<MediaFrameSource, bool> additionalSourceCriteria;
        Action<MediaFrameReader> onFrameArrived;
        MediaFrameReader frameReader;
        MediaFrameSource mediaSource;
        MediaCapture mediaCapture;
        MediaFrameSourceKind mediaSourceKind;
    }
    class mtKinectColorPoseFrameHelper
  {
    public event EventHandler<mtSoftwareBitmapEventArgs> ColorFrameArrived;
    public event EventHandler<mtPoseTrackingFrameEventArgs> PoseFrameArrived;
    SpatialCoordinateSystem colorCoordinateSystem;
    mtSoftwareBitmapEventArgs softwareBitmapEventArgs;
    mtMediaSourceReader[] mediaSourceReaders;
    MediaCapture mediaCapture;
    CameraIntrinsics colorIntrinsics;
    const string PerceptionFormat = "Perception";
    private Matrix4x4? depthColorTransform;
    public WriteableBitmap writeablebitmap2;
    public mtKinectColorPoseFrameHelper()
    {
      this.softwareBitmapEventArgs = new mtSoftwareBitmapEventArgs();
      
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

        this.mediaSourceReaders = new mtMediaSourceReader[]
        {
          new mtMediaSourceReader(this.mediaCapture, MediaFrameSourceKind.Color, this.OnFrameArrived),
          new mtMediaSourceReader(this.mediaCapture, MediaFrameSourceKind.Depth, this.OnFrameArrived),
          new mtMediaSourceReader(this.mediaCapture, MediaFrameSourceKind.Infrared, this.OnFrameArrived),
          new mtMediaSourceReader(this.mediaCapture, MediaFrameSourceKind.Custom, this.OnFrameArrived,
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
            this.depthColorTransform = frame.CoordinateSystem.TryGetTransformTo(
                this.colorCoordinateSystem);
        }
    }
    void ProcessDepthFrame(MediaFrameReference frame)
    {
      if (this.colorCoordinateSystem != null)
      {
        this.depthColorTransform = frame.CoordinateSystem.TryGetTransformTo(
          this.colorCoordinateSystem);
      }     
    }
    void ProcessColorFrame(MediaFrameReference frame)
    {
      if (this.colorCoordinateSystem == null)
      {
        this.colorCoordinateSystem = frame.CoordinateSystem;
        this.colorIntrinsics = frame.VideoMediaFrame.CameraIntrinsics;
      }
     this.softwareBitmapEventArgs.Set(frame.VideoMediaFrame.SoftwareBitmap);
    writeablebitmap2 = new WriteableBitmap(100, 100);
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
                mtPoseTrackingDetails.FromPoseTrackingEntity(entity, this.colorIntrinsics, this.depthColorTransform.Value))
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