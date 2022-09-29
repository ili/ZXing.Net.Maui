using System;
using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.Media;
using Android.Nfc;
using Android.OS;
using Android.Renderscripts;
using Android.Runtime;
using Android.Util;
using Android.Views;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using Java.Util;
using Java.Util.Concurrent;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Extensions.DependencyInjection;
using static Android.Hardware.Camera;
using static Android.Provider.Telephony;
using static Java.Util.Concurrent.Flow;
using AView = Android.Views.View;
using Android.Hardware;
using static Android.Graphics.Paint;
using AndroidX.Camera.Camera2.InterOp;
using MSize = Microsoft.Maui.Graphics.Size;

namespace ZXing.Net.Maui
{
	internal partial class CameraManager
	{
		AndroidX.Camera.Core.Preview cameraPreview;
		ImageAnalysis imageAnalyzer;
		PreviewView previewView;
		IExecutorService cameraExecutor;
		//CameraSelector cameraSelector = null;
		ProcessCameraProvider cameraProvider;
		ICamera camera;
		FrameAnalyzer frameAnalyzer;
		Google.Common.Util.Concurrent.IListenableFuture cameraProviderFuture;

		public NativePlatformCameraPreviewView CreateNativeView()
		{
			previewView = new PreviewView(Context.Context);
			cameraExecutor = Executors.NewSingleThreadExecutor();

			previewView.ViewAttachedToWindow += PreviewView_ViewAttachedToWindow;
			previewView.ViewDetachedFromWindow += PreviewView_ViewDetachedFromWindow;

			return previewView;
		}

		private void PreviewView_ViewDetachedFromWindow(object sender, NativePlatformView.ViewDetachedFromWindowEventArgs e)
		{
			Disconnect();
		}

		private void PreviewView_ViewAttachedToWindow(object sender, NativePlatformView.ViewAttachedToWindowEventArgs e)
		{
			UpdateCamera();
		}

		public void Connect()
		{
			if (cameraProviderFuture is null)
			{
				cameraProviderFuture = ProcessCameraProvider.GetInstance(Context.Context);

				cameraProviderFuture.AddListener(new Java.Lang.Runnable(() =>
				{
					// Used to bind the lifecycle of cameras to the lifecycle owner
					if (cameraProvider is null)
						cameraProvider = (ProcessCameraProvider)cameraProviderFuture.Get();

					UpdateCamera();

				}), ContextCompat.GetMainExecutor(Context.Context)); //GetMainExecutor: returns an Executor that runs on the main thread.
			}
		}

		private void PreviewView_Touch(object sender, NativePlatformView.TouchEventArgs e)
		{	
			if (e.Event.Action == MotionEventActions.Down)
			{
				var p = new Point((int)e.Event.GetX(), (int)e.Event.GetY());
				Focus(p);
				e.Handled = true;
			}
		}

		public void Disconnect()
		{
			cameraProvider?.UnbindAll();


			cameraPreview?.SetSurfaceProvider(null);
			cameraPreview?.Dispose();
			cameraPreview = null;

			imageAnalyzer?.ClearAnalyzer();
			imageAnalyzer?.Dispose();
			imageAnalyzer = null;

			frameAnalyzer?.Dispose();
			frameAnalyzer = null;

			if (previewView.Parent is View parent and not null)
				parent.Touch -= PreviewView_Touch;

		}

		public bool IsVisible
		{
			get => previewView.Visibility == ViewStates.Visible;
			set
			{
				previewView.Visibility = value ? ViewStates.Visible : ViewStates.Invisible;
				UpdateCamera();
			}
		}

		public MSize TargetCaptureResolution { get; private set; } = MSize.Zero;

		public void UpdateTargetCaptureResolution(MSize targetCaptureResolution)
		{
			TargetCaptureResolution = targetCaptureResolution;

			if (cameraProvider is not null)
				UpdateCamera();
		}

		public void UpdateCamera()
		{
			Disconnect();

			if (cameraProvider != null && IsVisible)
			{
				if (frameAnalyzer is null)
				{
					frameAnalyzer = new FrameAnalyzer((buffer, size) =>
						FrameReady?.Invoke(
							this,
							new CameraFrameBufferEventArgs(new Readers.PixelBufferHolder
							{
								Data = buffer,
								Size = size
							})));
				}

				// Frame by frame analyze
				imageAnalyzer = new ImageAnalysis.Builder()
					.SetDefaultResolution(new Android.Util.Size(640, 480))
					.SetOutputImageRotationEnabled(true)
					.SetTargetResolution(new Android.Util.Size((int)TargetCaptureResolution.Width, (int)TargetCaptureResolution.Height))
					.SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest)
					.Build();

				imageAnalyzer.SetAnalyzer(cameraExecutor, frameAnalyzer);
				
				// Unbind use cases before rebinding
				var cameraLocation = CameraLocation;

				// Preview
				if (cameraPreview is null)
				{
					cameraPreview = new AndroidX.Camera.Core.Preview.Builder().Build();
					cameraPreview.SetSurfaceProvider(previewView.SurfaceProvider);
				}

				CameraSelector cameraSelector = null;
				// Select back camera as a default, or front camera otherwise
				if (cameraLocation == CameraLocation.Rear && cameraProvider.HasCamera(CameraSelector.DefaultBackCamera))
					cameraSelector = CameraSelector.DefaultBackCamera;
				else if (cameraLocation == CameraLocation.Front && cameraProvider.HasCamera(CameraSelector.DefaultFrontCamera))
					cameraSelector = CameraSelector.DefaultFrontCamera;
				else
					cameraSelector = CameraSelector.DefaultBackCamera;

				if (cameraSelector == null)
					throw new System.Exception("Camera not found");

				// The Context here SHOULD be something that's a lifecycle owner
				if (previewView.Context is AndroidX.Lifecycle.ILifecycleOwner previewViewLifecycleOwner)
					camera = cameraProvider.BindToLifecycle(previewViewLifecycleOwner, cameraSelector, cameraPreview, imageAnalyzer);
				else if (Context.Context is AndroidX.Lifecycle.ILifecycleOwner lifecycleOwner)
					camera = cameraProvider.BindToLifecycle(lifecycleOwner, cameraSelector, cameraPreview, imageAnalyzer);
				// if not, this should be sufficient as a fallback
				else if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is AndroidX.Lifecycle.ILifecycleOwner maLifecycleOwner)
					camera = cameraProvider.BindToLifecycle(maLifecycleOwner, cameraSelector, cameraPreview, imageAnalyzer);

				if (previewView.Parent is View parent and not null)
					parent.Touch += PreviewView_Touch;
			}
		}

		public void UpdateTorch(bool on)
		{
			camera?.CameraControl?.EnableTorch(on);
		}

		public void Focus(Point point)
		{
			if (camera?.CameraControl == null)
				return;
				
			camera.CameraControl.CancelFocusAndMetering();

			var factory = new SurfaceOrientedMeteringPointFactory(previewView.LayoutParameters.Width, previewView.LayoutParameters.Height);
			var fpoint = factory.CreatePoint(point.X, point.Y);
			var action = new FocusMeteringAction.Builder(fpoint, FocusMeteringAction.FlagAf)
									.DisableAutoCancel()
									.Build();

			camera.CameraControl.StartFocusAndMetering(action);
		}

		public void AutoFocus()
		{

		}

		public void Dispose()
		{
			Disconnect();

			cameraExecutor?.Shutdown();
			cameraExecutor?.Dispose();
			cameraExecutor = null;
		}
	}
}
