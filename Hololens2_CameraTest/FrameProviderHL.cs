using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Graphics.Imaging;
using Windows.Perception.Spatial;
using Windows.Foundation;
using Windows.System.Profile;

namespace Hololens2_CameraTest
{
    /// <summary>
    /// Event arguments for the <see cref="FrameProviderHL.CameraInitialized"/> event.
    /// </summary>
    public class CameraInitializedEventArgs
    {
        public int FrameWidth;
        public int FrameHeight;

        public CameraInitializedEventArgs(int width, int height)
        {
            FrameWidth = width;
            FrameHeight = height;
        }
    }
    
    /// <summary>
    /// Event arguments for the <see cref="FrameProviderHL.FrameArrived"/> event.
    /// </summary>
    public class FrameArrivedEventArgs
    {
        public byte[] Frame;
        public int FrameWidth;
        public int FrameHeight;

        /// <summary>
        /// The pixel data of the frame are stored in <see cref="Frame"/>. The width and height of the frame are stored in <see cref="FrameWidth"/> and <see cref="FrameHeight"/>.
        /// The pixel data in <see cref="Frame"/> are gray scale values from the luminance plane of the NV12 frame.
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="frameWidth"></param>
        /// <param name="frameHeight"></param>
        public FrameArrivedEventArgs(byte[] frame, int frameWidth, int frameHeight)
        {
            Frame = frame;
            FrameWidth = frameWidth;
            FrameHeight = frameHeight;
        }
    }

 
    public class CameraParameters
    {
        /// <summary>
        /// A valid height resolution for use with the camera.
        /// </summary>
        public int CameraResolutionHeight;

        /// <summary>
        /// A valid width resolution for use with the camera.
        /// </summary>
        public int CameraResolutionWidth;

        /// <summary>
        /// The frame rate at which to capture video.
        /// </summary>
        public float FrameRate;

        public CameraParameters(int cameraResolutionWidth, int cameraResolutionHeight, float frameRate)
        {
            CameraResolutionHeight = cameraResolutionHeight;
            CameraResolutionWidth = cameraResolutionWidth;
            FrameRate = frameRate;
        }

        public override string ToString()
        {
            return $"Camera Resolution: {CameraResolutionWidth}x{CameraResolutionHeight}, FrameRate: {FrameRate}";
        }
    }
    
    public class FrameProviderHL
    {
        #region member variables
        /// <summary>
        /// the default display name of the front facing camera of the Hololens 2
        /// </summary>
        private const string LocatableCameraDisplayName = "QC Back Camera";
        
        private readonly CameraParameters _cameraParams;
        
        private MediaCapture _mediaCapture;
        private MediaFrameReader _frameReader;
        
        public int FrameHeight { get; set; }
        public int FrameWidth { get; set; }
        private const double Tolerance = 0.001;

        private readonly SimpleLogger _logger;
        #endregion // Member Variables
        
        /// <summary>
        /// Invoked on each frame that is captured.
        /// </summary>
        internal event EventHandler<FrameArrivedEventArgs> FrameArrived;

        /// <summary>
        /// Invoked after the camera is initialized using <see cref="Initialize"/>.
        /// </summary>
        internal event EventHandler<CameraInitializedEventArgs> CameraInitialized;
        
        #region Constructor
        public FrameProviderHL(SimpleLogger logger)
        {
            _logger = logger;

            var architecture = "Unknown";

            var versionInfo = AnalyticsInfo.VersionInfo;
            var systemArchitecture = versionInfo.DeviceFamily;
            switch (systemArchitecture)
            {
                case "Windows.Desktop":
                    _logger.Log("Windows desktop system architecture detected");
                    _cameraParams = new CameraParameters(1280, 720, 30);
                    break;
                case "Windows.Holographic":
                    _logger.Log("Windows.Holographic system architecture detected");
                    // _cameraParams = new CameraParameters(896, 504, 30);
                    _cameraParams = new CameraParameters(2272, 1278, 15);
                    break;
                default:
                    _logger.Log("Unknown architecture detected");
                    _cameraParams = new CameraParameters(640, 480, 30);
                    break;
            }
            
            _logger?.Log($"Camera parameters: {_cameraParams}");
        }
        #endregion 

        #region internal stuff
        [ComImport]
        [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal unsafe interface IMemoryBufferByteAccess
        {
            void GetBuffer(out byte* buffer, out uint capacity);
        }
        #endregion

        
        /// <summary>
        /// Retrieves the <see cref="MediaFrameSourceGroup">source group</see> using the display name of the camera.
        /// Defaults to the world-facing color camera of the HoloLens 2.
        /// </summary>
        private async Task<MediaFrameSourceGroup> SelectGroup(string displayName = LocatableCameraDisplayName)
        {
            IReadOnlyList<MediaFrameSourceGroup> groups = await MediaFrameSourceGroup.FindAllAsync();
            foreach (MediaFrameSourceGroup group in groups)
            {
                _logger.Log($"Found group {group.DisplayName}");
                if (group.DisplayName != displayName && group.DisplayName != "Integrated Camera") continue;
                _logger.Log($"Selected group {group.DisplayName}");
                return group;
            }
            throw new ArgumentException($"No source group for display name {displayName} found.");
        }
        
        /// <summary>
        /// Retrieve the device id from given display name. Defaults to the world-facing color camera of the HoloLens 2.
        /// </summary>
        /// <param name="displayName"></param>
        /// <returns></returns>
        private async Task<string> GetDeviceId(string displayName = LocatableCameraDisplayName)
        {
            MediaFrameSourceGroup group = await SelectGroup(displayName);
            return group.Id;
        }
        
        /// <summary>
        /// Initializes <see cref="MediaCapture"/> to use the world-facing locatable color camera.
        /// </summary>
        private async Task<bool> InitializeMediaCapture()
        {
            if (_mediaCapture != null)
            {
                _logger.LogWarning("Media capture already initialized");
                return false;
            }
            
            string deviceId = await GetDeviceId();

            var mediaInitSettings = new MediaCaptureInitializationSettings { VideoDeviceId = deviceId };

            IReadOnlyList<MediaCaptureVideoProfile> profiles = MediaCapture.FindAllVideoProfiles(deviceId);
            //IReadOnlyList<MediaCaptureVideoProfile> profiles = MediaCapture.FindKnownVideoProfiles(deviceId, KnownVideoProfile.VideoConferencing);
            //IReadOnlyList<MediaCaptureVideoProfile> profiles = MediaCapture.FindKnownVideoProfiles(deviceId, KnownVideoProfile.VideoRecording);

            var match = (from profile in profiles
                from desc in profile.SupportedRecordMediaDescription
                where desc.Subtype == "NV12" && desc.Width == 2272 && desc.Height == 1278 && Math.Abs(desc.FrameRate - 30) < Tolerance
                select new { profile, desc}).FirstOrDefault();

            if (match != null)
            {
                _logger.Log($"Found profile with desc: {match.desc.Subtype} {match.desc.FrameRate}fps {match.desc.Width}x{match.desc.Height} {match.desc.IsHdrVideoSupported}");
                mediaInitSettings.VideoProfile = match.profile;
                mediaInitSettings.RecordMediaDescription = match.desc;
            }
            else
            {
                _logger.Log($"No profile found, using first profile: {profiles[0].Id}");
                mediaInitSettings.VideoProfile = profiles[0];
            }

            // Exclusive control is necessary to control frame-rate and resolution.
            // Note: The resolution and frame-rate of the built-in MRC camera UI might be reduced from its normal values when another app is using the photo/video camera.
            // See <see href="https://docs.microsoft.com/en-us/windows/mixed-reality/develop/platform-capabilities-and-apis/mixed-reality-capture-for-developers"/>
            mediaInitSettings.SharingMode = MediaCaptureSharingMode.ExclusiveControl;
            mediaInitSettings.MemoryPreference = MediaCaptureMemoryPreference.Cpu;

            _mediaCapture = new MediaCapture();
            try
            {
                await _mediaCapture.InitializeAsync(mediaInitSettings);
            }
            catch (Exception ex)
            {
                _logger.Log("MediaCapture initialization failed: " + ex.Message);
                return false;
            }
            _logger.Log("Media capture successfully initialized.");
            return true;
        }
        
        
        /// <summary>
        /// Creates the frame reader using the target format and registers the <see cref="OnFrameArrived"/> event. The width is padded to be divisibly by 64.
        /// </summary>
        /// <returns></returns>
        private async Task<bool> CreateFrameReader()
        {
            const MediaStreamType mediaStreamType = MediaStreamType.VideoRecord;
            try
            {
                foreach (var fs in _mediaCapture.FrameSources)
                {
                    _logger.Log($"FrameSource: {fs.Value.Info.MediaStreamType} {fs.Value.CurrentFormat.Subtype} {fs.Value.Info.SourceKind} {fs.Value.Info.DeviceInformation.Name}");
                }
                MediaFrameSource source = _mediaCapture.FrameSources.Values.Single(frameSource => frameSource.Info.MediaStreamType == mediaStreamType);
                
                var preferredFormat = source.SupportedFormats.Where(format => 
                                format.VideoFormat.Height == _cameraParams.CameraResolutionHeight
                                &&
                                format.VideoFormat.Width == _cameraParams.CameraResolutionWidth
                                &&
                                Math.Round(format.FrameRate.Numerator / format.FrameRate.Denominator - _cameraParams.FrameRate) < Tolerance
                                &&
                                format.Subtype == "NV12");

                _logger.Log("matching Formats:");
                var mediaFrameFormats = preferredFormat as MediaFrameFormat[] ?? preferredFormat.ToArray();
                foreach (var format in mediaFrameFormats)
                {
                    _logger.Log($"{format.VideoFormat.Width}x{format.VideoFormat.Height} {format.FrameRate.Numerator}/{format.FrameRate.Denominator} {format.Subtype}");
                }
                
                var selectedFormat = mediaFrameFormats.FirstOrDefault();
                await source.SetFormatAsync(selectedFormat);

                _frameReader = await _mediaCapture.CreateFrameReaderAsync(source, selectedFormat.Subtype);
                _frameReader.FrameArrived += OnFrameArrived;

                FrameWidth = Convert.ToInt32(selectedFormat.VideoFormat.Width);
                FrameHeight = Convert.ToInt32(selectedFormat.VideoFormat.Height);

                _logger.Log($"FrameReader initialized using {FrameWidth} x {FrameHeight}, frame rate: {selectedFormat.FrameRate.Numerator}/{selectedFormat.FrameRate.Denominator}");
            }
            catch (Exception exception)
            {
                _logger.LogError("Frame Reader could not be initialized");
                _logger.LogException(exception.Message);
                return false;
            }

            return true;
        }
        
        private async void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            if (sender == null) throw new ArgumentNullException(nameof(sender));
            if (args == null) throw new ArgumentNullException(nameof(args));

            using (MediaFrameReference mediaFrameReference = sender.TryAcquireLatestFrame())
            {
                if (mediaFrameReference == null) return;
                var videoMediaFrame = mediaFrameReference.VideoMediaFrame;
                var softwareBitmap = videoMediaFrame?.SoftwareBitmap;
                
                if (softwareBitmap != null)
                {
                    var width = softwareBitmap.PixelWidth;
                    var height = softwareBitmap.PixelHeight;

                    // copy only luminance plane of NV12
                    byte[] rawPixelData = new byte[width * height];
                    using (var buffer = softwareBitmap.LockBuffer(BitmapBufferAccessMode.Read))
                    using (var reference = buffer.CreateReference())
                        unsafe
                        {
                            byte* pixelData;
                            uint capacity;
                            ((IMemoryBufferByteAccess)reference).GetBuffer(out pixelData, out capacity);
                            Marshal.Copy((IntPtr)pixelData, rawPixelData, 0, rawPixelData.Length);
                        }
                    
                    FrameArrived?.Invoke(this, new FrameArrivedEventArgs(rawPixelData, width, height));
                    softwareBitmap.Dispose();
                } // end if
                else
                {
                    _logger.LogWarning("SoftwareBitmap is null");
                }
            } // end using   
        } // end OnFrameArrived
        
        /// <summary>
        /// The camera of the device will be configured using the <see cref="LocatableCameraProfile"/> and <see cref="ColorFormat"/>, the pixel format is NV12.
        /// </summary>
        /// <returns>Whether video pipeline is successfully initialized</returns>
        private async Task<bool> InitializeMediaCaptureAsyncTask()
        {
            _logger.Log("Initializing media capture");
            if (!await InitializeMediaCapture()) return false;
            _logger.Log("Creating frame reader");
            if (!await CreateFrameReader()) return false;
            _logger.Log("Frame reader creation successful");
            return true;
        }

        /// <summary>
        /// Starts the video pipeline and frame reading.
        /// </summary>
        /// <returns>Whether the frame reader is successfully started</returns>
        private async Task<bool> StartFrameReaderAsyncTask()
        {
            MediaFrameReaderStartStatus mediaFrameReaderStartStatus = await _frameReader.StartAsync();
            if (mediaFrameReaderStartStatus == MediaFrameReaderStartStatus.Success)
            {
                _logger.Log("Started Frame reader");
                return true;
            }

            _logger.LogError($"Could not start frame reader, status: {mediaFrameReaderStartStatus}");
            return false;
        }

        
        #region Public Methods

        public async Task<bool> Initialize()
        {
            bool initialized = await InitializeMediaCaptureAsyncTask();
            if (initialized)
            {
                CameraInitializedEventArgs args = new CameraInitializedEventArgs(FrameWidth, FrameHeight);
                CameraInitialized?.Invoke(this, args);
            }
            return initialized;
        }
        
        public async Task<bool> StartCapture()
        {
            return await StartFrameReaderAsyncTask();
        }

        public async Task<bool> StopCapture()
        {
            if (_mediaCapture != null)
            {
                if (_frameReader != null)
                {
                    await _frameReader.StopAsync();
                    var t = _frameReader.StopAsync();
                    while (t.Status != AsyncStatus.Completed)
                    {
                        Thread.Sleep(100);
                    }
                    _frameReader.FrameArrived -= OnFrameArrived;
                    _frameReader.Dispose();
                    _frameReader = null;
                }

                _mediaCapture.Dispose();
                _mediaCapture = null;
            }
            return true;
        }
        #endregion // Public Methods
    } // end class
} // end namespace