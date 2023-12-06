
using System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Hololens2_CameraTest
{
    /// <summary>
    /// A simple logger to display text to TextBlock asynchronisely.
    /// </summary>
    public class SimpleLogger
    {
        private CoreDispatcher _dispatcher;
        private TextBlock _textBlock;
        public ScrollViewer scrollViewer;

        private string _messageText = string.Empty;
        private readonly object _messageLock = new object();
        private int _messageCount;

        public SimpleLogger(TextBlock textBlock)
        {
            _textBlock = textBlock;
            _dispatcher = _textBlock.Dispatcher;
        }

        internal async void LogWarning(string message)
        {
            Log(message);
        }
        
        internal async void LogError(string message)
        {
            Log(message);
        }
        
        internal async void LogException(string message)
        {
            Log(message);
        }
        
        /// <summary>
        /// Logs a message to be displayed.
        /// </summary>
        internal async void Log(string message)
        {
            //var newMessage = $"{_messageText}\n[{_messageCount++}] {DateTime.Now:hh:MM:ss} : {message}";
            var newMessage = $"{_messageText}\n[{_messageCount++}] {DateTime.Now:hh:MM:ss} : {message}";

            lock (_messageLock)
            {
                _messageText = newMessage;
            }

            await _dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                lock (_messageLock)
                {
                    _textBlock.Text = _messageText;
                    if (scrollViewer != null)
                    {
                        scrollViewer.ChangeView(0, scrollViewer.ScrollableHeight, null);
                    }
                }
            });
        }
    }
}