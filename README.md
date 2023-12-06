# Hololens2_CameraTest

This is a simple UWP demo application to check how to access the front facing camera of the MS Hololens 2.

The front facing camera is accessed using MediaCapture and MediaFrameReader. The video stream format is set to NV12 and for each incoming frame the luminance values from the image are copied into a byte array. Then an event is triggered with the byte array as an argument.