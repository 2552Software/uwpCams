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
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using s2552Media;
using System;
using Microsoft.Graphics.Canvas.UI.Xaml;

// lots of maybe good camera stuff down the road https://github.com/Microsoft/Windows-universal-samples/blob/master/Samples/CameraGetPreviewFrame/cs/MainPage.xaml.cs

namespace KinectTestApp
{
    public sealed partial class MainPage : Page
    {

        MediaSourceReaders readers = new MediaSourceReaders();

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += this.OnLoaded;
        }

        async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await this.readers.InitialiseAsync();
        }
        private void OnCanvasControlSizeChanged(object sender, SizeChangedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            throw new NotImplementedException();
        }

    }
}

