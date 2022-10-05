using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;

namespace ZXing.Net.Maui;

public partial class CameraBarcodeReaderViewHandler : ViewHandler<ICameraBarcodeReaderView, NativePlatformCameraPreviewView>
{
	public static PropertyMapper<ICameraBarcodeReaderView, CameraBarcodeReaderViewHandler> CameraBarcodeReaderViewMapper = new()
	{
		[nameof(ICameraBarcodeReaderView.Options)] = MapOptions,
		[nameof(ICameraBarcodeReaderView.IsDetecting)] = MapIsDetecting,
		[nameof(ICameraBarcodeReaderView.TargetCaptureResolution)] = MapTargetCaptureResolution,
		[nameof(ICameraBarcodeReaderView.Visibility)] = (handler, virtualView) => handler.cameraManager.UpdateIsVisible(virtualView.Visibility == Visibility.Visible),
		[nameof(ICameraBarcodeReaderView.IsTorchOn)] = (handler, virtualView) => handler.cameraManager.UpdateTorch(virtualView.IsTorchOn),
		[nameof(ICameraBarcodeReaderView.CameraLocation)] = (handler, virtualView) => handler.cameraManager.UpdateCameraLocation(virtualView.CameraLocation)
	};

	public static CommandMapper<ICameraBarcodeReaderView, CameraBarcodeReaderViewHandler> CameraBarcodeReaderCommandMapper = new()
	{
		[nameof(ICameraBarcodeReaderView.Focus)] = MapFocus,
		[nameof(ICameraBarcodeReaderView.AutoFocus)] = MapAutoFocus,
	};

	public CameraBarcodeReaderViewHandler() : base(CameraBarcodeReaderViewMapper)
	{
	}

	public CameraBarcodeReaderViewHandler(PropertyMapper mapper = null) : base(mapper ?? CameraBarcodeReaderViewMapper)
	{
	}

	public event EventHandler<BarcodeDetectionEventArgs> BarcodesDetected;
	public event EventHandler<CameraFrameBufferEventArgs> FrameReady;

	CameraManager cameraManager;

	Readers.IBarcodeReader barcodeReader;
	private static BarcodeReaderOptions _options;

	protected Readers.IBarcodeReader BarcodeReader
		=> barcodeReader ??= Services.GetService<Readers.IBarcodeReader>();

	protected override NativePlatformCameraPreviewView CreatePlatformView()
	{
		if (cameraManager == null)
			cameraManager = new(MauiContext, VirtualView?.CameraLocation ?? CameraLocation.Rear);
		var v = cameraManager.CreateNativeView();
		return v;
	}

	protected override async void ConnectHandler(NativePlatformCameraPreviewView nativeView)
	{
		base.ConnectHandler(nativeView);

		if (await cameraManager.CheckPermissions())
			cameraManager.Connect();

		cameraManager.FrameReady += CameraManager_FrameReady;
	}

	protected override void DisconnectHandler(NativePlatformCameraPreviewView nativeView)
	{
		cameraManager.FrameReady -= CameraManager_FrameReady;

		cameraManager.Disconnect();

		base.DisconnectHandler(nativeView);
	}

	private void CameraManager_FrameReady(object sender, CameraFrameBufferEventArgs e)
	{
		FrameReady?.Invoke(this, e);

		if (VirtualView.IsDetecting && BarcodesDetected != null)
		{
			foreach (var r in Services.GetServices<Readers.IBarcodeReader>())
			{
				r.Options = _options;
				var barcodes = r.Decode(e.Data);

				if (barcodes?.Any() ?? false)
				{
					BarcodesDetected?.Invoke(this, new BarcodeDetectionEventArgs(barcodes));
					break;
				}
			}
		}
	}

	public static void MapOptions(CameraBarcodeReaderViewHandler handler, ICameraBarcodeReaderView cameraBarcodeReaderView)
	{ 
		handler.BarcodeReader.Options = cameraBarcodeReaderView.Options;
		_options = cameraBarcodeReaderView.Options;
		handler.cameraManager?.UpdateAutoRotate(cameraBarcodeReaderView.Options.AutoRotate);
	}

	public static void MapIsDetecting(CameraBarcodeReaderViewHandler handler, ICameraBarcodeReaderView cameraBarcodeReaderView)
	{ }

	public static void MapTargetCaptureResolution(CameraBarcodeReaderViewHandler handler, ICameraBarcodeReaderView cameraBarcodeReaderView)
		=> handler.cameraManager?.UpdateTargetCaptureResolution(cameraBarcodeReaderView.TargetCaptureResolution);


	public void Focus(Point point)
		=> cameraManager?.Focus(point);

	public void AutoFocus()
		=> cameraManager?.AutoFocus();

	public static void MapFocus(CameraBarcodeReaderViewHandler handler, ICameraBarcodeReaderView cameraBarcodeReaderView, object? parameter)
	{
		if (parameter is not Point point)
			throw new ArgumentException("Invalid parameter", "point");

		handler.Focus(point);
	}

	public static void MapAutoFocus(CameraBarcodeReaderViewHandler handler, ICameraBarcodeReaderView cameraBarcodeReaderView, object? parameters)
		=> handler.AutoFocus();
}
