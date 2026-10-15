using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace GlassBoard;

public class GlassWindow : Window
{
	protected Grid Root = new Grid();

	protected Border Surface = new Border();

	protected Grid ContentGrid = new Grid();

	protected bool LightTheme;

	protected double GlassPercent = 72.0;

	protected bool MotionEnabled = true;

	protected double MotionLevel = 1.0;

	protected double BlurRadius = 18.0;

	protected double EdgeStrength = 1.0;

	protected System.Windows.Media.Brush TextBrush => new SolidColorBrush(LightTheme ? System.Windows.Media.Color.FromRgb(25, 28, 34) : System.Windows.Media.Color.FromRgb(247, 247, 249));

	protected System.Windows.Media.Brush MutedBrush => new SolidColorBrush(LightTheme ? System.Windows.Media.Color.FromRgb(65, 69, 77) : System.Windows.Media.Color.FromRgb(184, 188, 198));

	protected void ConfigureGlass(DockOptions options)
	{
		BlurRadius = options.BlurRadius;
		EdgeStrength = options.EdgeStrength;
	}

	protected static ControlTemplate GripTemplate(bool corner = false)
	{
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(corner ? typeof(TextBlock) : typeof(Border));
		if (corner)
		{
			frameworkElementFactory.SetValue(TextBlock.TextProperty, "◢");
			frameworkElementFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromArgb(135, 180, 195, 215)));
			frameworkElementFactory.SetValue(TextBlock.FontSizeProperty, 18.0);
		}
		else
		{
			frameworkElementFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(3.0));
			frameworkElementFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 180, 195, 215)));
		}
		return new ControlTemplate(typeof(Thumb))
		{
			VisualTree = frameworkElementFactory
		};
	}

	public GlassWindow(double radius)
	{
		base.WindowStyle = WindowStyle.None;
		base.AllowsTransparency = true;
		base.Background = System.Windows.Media.Brushes.Transparent;
		base.ResizeMode = ResizeMode.NoResize;
		base.ShowInTaskbar = false;
		base.Topmost = true;
		base.Content = Root;
		Root.Margin = new Thickness(14.0);
		Surface.CornerRadius = new CornerRadius(radius);
		Surface.BorderThickness = new Thickness(1.0);
		Surface.Effect = new DropShadowEffect
		{
			BlurRadius = 25.0,
			ShadowDepth = 7.0,
			Opacity = 0.28,
			Color = Colors.Black
		};
		Root.Children.Add(Surface);
		Root.Children.Add(ContentGrid);
		base.SourceInitialized += delegate
		{
			try
			{
				if (!Native.SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, 0u))
				{
					Store.Log(new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowDisplayAffinity: allow desktop capture"));
				}
			}
			catch (Exception ex)
			{
				Store.Log(ex);
			}
		};
	}

	protected Button Button(string label, RoutedEventHandler click, double width = 80.0)
	{
		Button b = new Button
		{
			Content = label,
			Width = width,
			Height = 36.0,
			Margin = new Thickness(4.0),
			Foreground = TextBrush,
			Cursor = Cursors.Hand,
			// 去掉键盘焦点的虚线框。WPF 默认会在获得焦点的按钮上画一个
			// 虚线矩形（分页的「‹」按钮尤其明显，看起来像个坏掉的空框）。
			FocusVisualStyle = null
		};
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(Border));
		frameworkElementFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(12.0));
		frameworkElementFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(LightTheme ? 38 : 25), byte.MaxValue, byte.MaxValue, byte.MaxValue)));
		frameworkElementFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(System.Windows.Media.Color.FromArgb(42, byte.MaxValue, byte.MaxValue, byte.MaxValue)));
		frameworkElementFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1.0));
		FrameworkElementFactory frameworkElementFactory2 = new FrameworkElementFactory(typeof(ContentPresenter));
		frameworkElementFactory2.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
		frameworkElementFactory2.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		frameworkElementFactory.AppendChild(frameworkElementFactory2);
		b.Template = new ControlTemplate(typeof(Button))
		{
			VisualTree = frameworkElementFactory
		};
		b.MouseEnter += delegate
		{
			if (MotionEnabled)
			{
				b.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.78, TimeSpan.FromMilliseconds(120.0)));
			}
			else
			{
				b.Opacity = 0.78;
			}
		};
		b.MouseLeave += delegate
		{
			if (MotionEnabled)
			{
				b.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150.0)));
			}
			else
			{
				b.Opacity = 1.0;
			}
		};
		b.PreviewMouseLeftButtonDown += delegate
		{
			b.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.6, TimeSpan.FromMilliseconds(MotionEnabled ? 65 : 0)));
		};
		b.PreviewMouseLeftButtonUp += delegate
		{
			b.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(MotionEnabled ? 100 : 0)));
		};
		if (click != null)
		{
			b.Click += click;
		}
		return b;
	}

	public void Backdrop(bool light)
	{
		LightTheme = light;
		SetGlassOpacity(GlassPercent);
		Surface.BorderBrush = new LinearGradientBrush(System.Windows.Media.Color.FromArgb((byte)Math.Min(255.0, 145.0 * EdgeStrength), byte.MaxValue, byte.MaxValue, byte.MaxValue), System.Windows.Media.Color.FromArgb((byte)Math.Min(255.0, 35.0 * EdgeStrength), byte.MaxValue, byte.MaxValue, byte.MaxValue), 90.0);
		if (base.IsVisible)
		{
			RefreshBackdropStyle();
			return;
		}
		(from b in Root.Children.OfType<Border>()
			where b.Tag as string == "backdrop"
			select b).ToList().ForEach(delegate(Border b)
		{
			Root.Children.Remove(b);
		});
		if (base.Width < 40.0 || base.Height < 40.0)
		{
			return;
		}
		try
		{
			Matrix matrix = Matrix.Identity;
			new WindowInteropHelper(this).EnsureHandle();
			PresentationSource presentationSource = PresentationSource.FromVisual(this);
			if (presentationSource != null)
			{
				matrix = presentationSource.CompositionTarget.TransformToDevice;
			}
			int sourceX = (int)Math.Round((base.Left + 14.0) * matrix.M11);
			int sourceY = (int)Math.Round((base.Top + 14.0) * matrix.M22);
			int num = Math.Max(1, (int)Math.Ceiling((base.Width - 28.0) * matrix.M11));
			int num2 = Math.Max(1, (int)Math.Ceiling((base.Height - 28.0) * matrix.M22));
			using Bitmap bitmap = new Bitmap(num, num2);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.CopyFromScreen(sourceX, sourceY, 0, 0, new System.Drawing.Size(num, num2));
			}
			using Bitmap bitmap2 = new Bitmap(bitmap, Math.Max(1, num / 8), Math.Max(1, num2 / 8));
			using MemoryStream memoryStream = new MemoryStream();
			bitmap2.Save(memoryStream, ImageFormat.Png);
			memoryStream.Position = 0L;
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.StreamSource = memoryStream;
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			Border border = new Border
			{
				Tag = "backdrop",
				CornerRadius = Surface.CornerRadius,
				Background = new ImageBrush(bitmapImage)
				{
					Stretch = Stretch.Fill
				},
				Opacity = (light ? 0.8 : 0.66)
			};
			border.Clip = new RectangleGeometry(new Rect(0.0, 0.0, base.Width - 28.0, base.Height - 28.0), Surface.CornerRadius.TopLeft, Surface.CornerRadius.TopLeft);
			border.Effect = new BlurEffect
			{
				Radius = BlurRadius,
				RenderingBias = RenderingBias.Performance
			};
			Border element = new Border
			{
				Tag = "backdrop",
				Clip = border.Clip,
				Child = border,
				Opacity = GlassPercent / 100.0
			};
			Root.Children.Insert(0, element);
		}
		catch (Exception ex)
		{
			Store.Log(ex);
		}
	}

	private void RefreshBackdropStyle()
	{
		foreach (Border item in from b in Root.Children.OfType<Border>()
			where b.Tag as string == "backdrop"
			select b)
		{
			RectangleGeometry clip = (RectangleGeometry)(item.Clip = new RectangleGeometry(new Rect(0.0, 0.0, Math.Max(1.0, base.Width - 28.0), Math.Max(1.0, base.Height - 28.0)), Surface.CornerRadius.TopLeft, Surface.CornerRadius.TopLeft));
			if (item.Child is Border border)
			{
				border.Clip = clip;
				border.CornerRadius = Surface.CornerRadius;
				border.Opacity = (LightTheme ? 0.8 : 0.66);
				border.Effect = new BlurEffect
				{
					Radius = BlurRadius,
					RenderingBias = RenderingBias.Performance
				};
			}
		}
	}

	protected void ReleaseBackdrop()
	{
		(from b in Root.Children.OfType<Border>()
			where b.Tag as string == "backdrop"
			select b).ToList().ForEach(delegate(Border b)
		{
			Root.Children.Remove(b);
		});
	}

	public void SetGlassOpacity(double percent)
	{
		GlassPercent = LayoutRules.GlassOpacity(percent);
		byte a = (byte)Math.Round(GlassPercent * 255.0 / 100.0);
		Surface.Background = new SolidColorBrush(LightTheme ? System.Windows.Media.Color.FromArgb(a, 234, 237, 244) : System.Windows.Media.Color.FromArgb(a, 24, 26, 32));
		foreach (Border item in from b in Root.Children.OfType<Border>()
			where b.Tag as string == "backdrop"
			select b)
		{
			item.Opacity = GlassPercent / 100.0;
		}
	}

	protected void AnimateOpen()
	{
		BeginAnimation(UIElement.OpacityProperty, null);
		base.Opacity = 1.0;
		if (!MotionEnabled)
		{
			Root.RenderTransform = Transform.Identity;
			return;
		}
		CubicEase easingFunction = new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		};
		TimeSpan timeSpan = TimeSpan.FromMilliseconds(260.0);
		BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, timeSpan)
		{
			EasingFunction = easingFunction
		});
		Root.RenderTransformOrigin = new System.Windows.Point(0.5, 0.8);
		ScaleTransform scaleTransform = new ScaleTransform(1.0, 1.0);
		TranslateTransform translateTransform = new TranslateTransform();
		TransformGroup transformGroup = new TransformGroup();
		transformGroup.Children.Add(scaleTransform);
		transformGroup.Children.Add(translateTransform);
		Root.RenderTransform = transformGroup;
		scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0 - 0.035 * MotionLevel, 1.0, timeSpan)
		{
			EasingFunction = easingFunction
		});
		scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0 - 0.035 * MotionLevel, 1.0, timeSpan)
		{
			EasingFunction = easingFunction
		});
		translateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14.0 * MotionLevel, 0.0, timeSpan)
		{
			EasingFunction = easingFunction
		});
	}
}
