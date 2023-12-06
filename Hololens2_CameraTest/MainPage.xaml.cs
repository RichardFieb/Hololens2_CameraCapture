using System;
using Windows.Graphics.Imaging;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;


namespace Hololens2_CameraTest
{
    /// <summary>
    /// Simple Test App to display the Hololens 2 Camera image to the screen
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private readonly FrameProviderHL _camera;
        private readonly SimpleLogger _logger;

        private readonly SoftwareBitmapSource _imageSource;
        private SoftwareBitmap _image;

        
        public MainPage()
        {
            this.InitializeComponent();
            
            _logger = new SimpleLogger(outputTextBlock)
            {
                scrollViewer = outputScrollViewer
            };

            // Create a SoftwareBitmapSource to display the Camera image to the screen
            _imageSource = new SoftwareBitmapSource();
            _camera = new FrameProviderHL(_logger);
            
            _camera.FrameArrived += OnFrameArrived;
            _camera.CameraInitialized += OnCameraInitialized;
            _camera.Initialize();

            
        }

        protected override async void OnNavigatedFrom(NavigationEventArgs e)
        {
            await _camera.StopCapture();
        }

        private void OnCameraInitialized(object sender, CameraInitializedEventArgs eventArgs)
        {
            _logger.Log("Camera initialized");

            _image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, eventArgs.FrameWidth, eventArgs.FrameHeight, BitmapAlphaMode.Premultiplied);
            try
            {
                _imageSource.SetBitmapAsync(_image);
                LiveImage.Source = _imageSource;
            }
            catch (Exception ex)
            {
                _logger.Log(ex.Message);
            }


            _camera.StartCapture();
        }
    
        private async void OnFrameArrived(object sender, FrameArrivedEventArgs eventArgs)
        {
            SoftwareBitmap outputBitmap = null;
            outputBitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, eventArgs.FrameWidth, eventArgs.FrameHeight, BitmapAlphaMode.Straight);
            unsafe
            {
                using (var bufferOut = outputBitmap.LockBuffer(BitmapBufferAccessMode.ReadWrite))
                using (var referenceOut = bufferOut.CreateReference())
                {
                    ((FrameProviderHL.IMemoryBufferByteAccess)referenceOut).GetBuffer(out var dataInBytes,
                        out var capacityOut);
                    // Fill-in the BGRA pixels, set alpha to 255
                    for (int i = 0, j = 0; i < eventArgs.FrameWidth * eventArgs.FrameHeight * 4; i += 4, j++)
                    {
                        dataInBytes[i + 0] = (byte)eventArgs.Frame[j];
                        dataInBytes[i + 1] = (byte)eventArgs.Frame[j];
                        dataInBytes[i + 2] = (byte)eventArgs.Frame[j];
                        dataInBytes[i + 3] = (byte)255;
                    }
                }
            }

            if (outputBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                outputBitmap.BitmapAlphaMode == BitmapAlphaMode.Straight)
            {
                try
                {
                    await _imageSource.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                    {
                        _imageSource.SetBitmapAsync(SoftwareBitmap.Convert(outputBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied));
                    });
                }
                catch (Exception ex)
                {
                    _logger.Log(ex.Message);
                }
            }
            outputBitmap?.Dispose();
        }
    }
}
