using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GlassBoard;

/// <summary>
/// 系统音频峰值捕获（WASAPI IAudioMeterInformation）。
/// 初始化失败时 IsAvailable=false，功能自动禁用，不影响主程序。
/// </summary>
public static class AudioMeter
{
	[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
	private class MMDeviceEnumerator { }

	[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMMDeviceEnumerator
	{
		int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
		int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
	}

	[ComImport, Guid("D666063F-1587-4E43-BFDB-BE5F5A752737"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMMDevice
	{
		int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
	}

	[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioMeterInformation
	{
		int GetPeakValue(out float pfPeak);
	}

	private static IAudioMeterInformation meter;

	public static bool IsAvailable { get; private set; }

	public static bool Init()
	{
		try
		{
			IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
			IMMDevice dev;
			if (enumerator.GetDefaultAudioEndpoint(0, 0, out dev) != 0 || dev == null)
			{
				IsAvailable = false;
				return false;
			}
			Guid iid = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
			object o;
			if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0 || o == null)
			{
				IsAvailable = false;
				return false;
			}
			meter = (IAudioMeterInformation)o;
			float v;
			meter.GetPeakValue(out v);
			IsAvailable = true;
			return true;
		}
		catch
		{
			IsAvailable = false;
			return false;
		}
	}

	public static float GetPeak()
	{
		try
		{
			if (meter == null)
			{
				return 0f;
			}
			float v;
			if (meter.GetPeakValue(out v) != 0)
			{
				return 0f;
			}
			if (v < 0f) v = 0f;
			if (v > 1f) v = 1f;
			return v;
		}
		catch
		{
			return 0f;
		}
	}
}

/// <summary>
/// 音频律动小组件：24 根柱子跟随系统音量跳动。独立小窗，可拖动，右键关闭。
/// </summary>
public class VisualizerWindow : Window
{
	private readonly Rectangle[] bars = new Rectangle[24];
	private readonly float[] levels = new float[24];
	private readonly float[] history = new float[24];
	private readonly DispatcherTimer timer;

	public VisualizerWindow()
	{
		Title = "音频律动";
		Width = 340;
		Height = 150;
		WindowStyle = WindowStyle.None;
		AllowsTransparency = true;
		Background = Brushes.Transparent;
		Topmost = true;
		ShowInTaskbar = false;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.Manual;

		Border border = new Border
		{
			CornerRadius = new CornerRadius(14),
			Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x14, 0x14, 0x14)),
			BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
			BorderThickness = new Thickness(1)
		};
		Grid grid = new Grid { Margin = new Thickness(12) };
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
		TextBlock title = new TextBlock
		{
			Text = "音频律动（右键关闭）",
			FontSize = 11,
			Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0, 0, 0, 6)
		};
		Grid.SetRow(title, 0);
		grid.Children.Add(title);
		Canvas canvas = new Canvas { ClipToBounds = true };
		Grid.SetRow(canvas, 1);
		grid.Children.Add(canvas);

		LinearGradientBrush grad = new LinearGradientBrush
		{
			StartPoint = new Point(0, 1),
			EndPoint = new Point(0, 0)
		};
		grad.GradientStops.Add(new GradientStop(Color.FromRgb(0, 229, 255), 0.0));
		grad.GradientStops.Add(new GradientStop(Color.FromRgb(255, 45, 149), 1.0));

		for (int i = 0; i < 24; i++)
		{
			Rectangle r = new Rectangle
			{
				Width = 9,
				Height = 4,
				Fill = grad,
				RadiusX = 3,
				RadiusY = 3
			};
			Canvas.SetLeft(r, i * 13 + 2);
			Canvas.SetBottom(r, 0);
			canvas.Children.Add(r);
			bars[i] = r;
		}
		border.Child = grid;
		Content = border;

		// 右下角默认位置
		try
		{
			var wa = SystemParameters.WorkArea;
			Left = wa.Width - 360;
			Top = wa.Height - 200;
		}
		catch { }

		MouseLeftButtonDown += (s, e) =>
		{
			if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
			{
				try { DragMove(); } catch { }
			}
		};

		ContextMenu cm = new ContextMenu();
		MenuItem mi = new MenuItem { Header = "关闭" };
		mi.Click += (s, e) => Close();
		cm.Items.Add(mi);
		ContextMenu = cm;

		timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
		timer.Tick += (s, e) =>
		{
			try
			{
				float peak = AudioMeter.GetPeak();
				// 历史队列右移，制造频谱延迟感
				for (int i = 23; i > 0; i--) history[i] = history[i - 1];
				history[0] = peak;
				for (int i = 0; i < 24; i++)
				{
					float target = history[i];
					float lvl = levels[i];
					if (target > lvl) lvl = target;
					else lvl = Math.Max(0, lvl - 0.045f);
					levels[i] = lvl;
					bars[i].Height = 4 + lvl * 84;
				}
			}
			catch { }
		};
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		timer.Start();
	}

	protected override void OnClosed(EventArgs e)
	{
		try { timer.Stop(); } catch { }
		base.OnClosed(e);
	}
}
