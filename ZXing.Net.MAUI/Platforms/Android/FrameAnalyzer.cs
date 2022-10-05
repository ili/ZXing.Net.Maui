using AndroidX.Camera.Core;
using Java.Nio;
using Microsoft.Maui.Graphics;
using System;

namespace ZXing.Net.Maui
{
	internal class FrameAnalyzer : Java.Lang.Object, ImageAnalysis.IAnalyzer
	{
		readonly Action<IImageProxy> frameCallback;

		public FrameAnalyzer(Action<IImageProxy> callback)
		{
			frameCallback = callback;
		}

		public void Analyze(IImageProxy image)
		{
			frameCallback?.Invoke(image);

			image.Close();
		}
	}
}
