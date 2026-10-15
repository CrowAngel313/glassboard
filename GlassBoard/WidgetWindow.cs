using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace GlassBoard;

public sealed class WidgetWindow : GlassWindow
{
	private readonly WidgetManager M;

	private readonly Thumb moveGrip;

	private readonly Thumb resizeGrip;

	public readonly int PanelId;

	public List<string> Kinds = new List<string>();

	private bool stopped;

	private bool editingNotes;

	private bool building;

	private bool resizing;

	private string signature = "";

	private StackPanel stack;

	private ScrollViewer viewport;

	private Viewbox contentScale;

	private double minimumWidgetHeight = 200.0;

	private ImageSource shownArtwork;

	private bool? shownPlaying;

	private TextBlock clock;

	private TextBlock date;

	private TextBlock clockStatus;

	private TextBlock weatherTemperature;

	private TextBlock weatherPlace;

	private TextBlock weatherDetails;

	private TextBlock weatherStatus;

	private TextBlock musicTitle;

	private TextBlock musicArtist;

	private TextBlock musicStatus;

	private TextBlock noteStatus;

	private Border cover;

	private Button play;

	private ComboBox notePicker;

	private TextBox noteTitle;

	private TextBox noteBody;

	private NoteItem currentNote;

	public bool IsStopped => stopped;

	public WidgetWindow(WidgetManager manager, int id)
		: base(29.0)
	{
		M = manager;
		PanelId = id;
		base.Title = "GlassBoard 组件面板 " + (char)(65 + id);
		base.ShowActivated = false;
		moveGrip = new Thumb
		{
			Width = 48.0,
			Height = 5.0,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0.0, 9.0, 0.0, 0.0),
			Cursor = Cursors.SizeAll,
			ToolTip = "拖动组件面板"
		};
		moveGrip.Template = GlassWindow.GripTemplate();
		Root.Children.Add(moveGrip);
		moveGrip.DragDelta += delegate(object s, DragDeltaEventArgs e)
		{
			if (!M.C.Prefs.Widgets.Locked)
			{
				Rect workArea2 = SystemParameters.WorkArea;
				base.Left = FeatureRules.Position(base.Left + e.HorizontalChange, workArea2.Left, workArea2.Width, base.Width);
				base.Top = FeatureRules.Position(base.Top + e.VerticalChange, workArea2.Top, workArea2.Height, base.Height);
			}
		};
		moveGrip.DragCompleted += delegate
		{
			WidgetPanelPosition widgetPanelPosition3 = M.C.Prefs.Widgets.Position(PanelId);
			widgetPanelPosition3.Left = base.Left;
			widgetPanelPosition3.Top = base.Top;
			if (PanelId == 0)
			{
				M.C.Prefs.Placement.WidgetLeft = base.Left;
				M.C.Prefs.Placement.WidgetTop = base.Top;
			}
			M.C.Save();
			Backdrop(LightTheme);
			ApplyWidgetGlass();
		};
		resizeGrip = new Thumb
		{
			Width = 20.0,
			Height = 20.0,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Bottom,
			Margin = new Thickness(0.0, 0.0, 7.0, 7.0),
			Cursor = Cursors.SizeNWSE,
			ToolTip = "拖动调整组件大小；内容自动适配，最小宽度 360，最小高度按内容计算"
		};
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(Border));
		frameworkElementFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
		FrameworkElementFactory frameworkElementFactory2 = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
		frameworkElementFactory2.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 4,16 L 16,4 M 10,16 L 16,10"));
		frameworkElementFactory2.SetValue(Shape.StrokeProperty, Polish.Muted(light: false));
		frameworkElementFactory2.SetValue(Shape.StrokeThicknessProperty, 1.5);
		frameworkElementFactory.AppendChild(frameworkElementFactory2);
		resizeGrip.Template = new ControlTemplate(typeof(Thumb))
		{
			VisualTree = frameworkElementFactory
		};
		Root.Children.Add(resizeGrip);
		resizeGrip.DragDelta += delegate(object s, DragDeltaEventArgs e)
		{
			if (!M.C.Prefs.Widgets.Locked)
			{
				Rect workArea = SystemParameters.WorkArea;
				WidgetPanelPosition widgetPanelPosition2 = M.C.Prefs.Widgets.Position(PanelId);
				widgetPanelPosition2.Width = Math.Max(Math.Min(360.0, workArea.Width - 30.0), Math.Min(workArea.Width - 30.0, base.Width + e.HorizontalChange));
				widgetPanelPosition2.Height = Math.Max(Math.Min(minimumWidgetHeight, workArea.Height - 30.0), Math.Min(workArea.Height - 30.0, base.Height + e.VerticalChange));
				base.Width = widgetPanelPosition2.Width.Value;
				FitContent();
				base.Left = FeatureRules.Position(base.Left, workArea.Left, workArea.Width, base.Width);
				base.Top = FeatureRules.Position(base.Top, workArea.Top, workArea.Height, base.Height);
			}
		};
		resizeGrip.DragCompleted += delegate
		{
			WidgetPanelPosition widgetPanelPosition = M.C.Prefs.Widgets.Position(PanelId);
			widgetPanelPosition.Left = base.Left;
			widgetPanelPosition.Top = base.Top;
			M.C.Save();
			Backdrop(LightTheme);
			ApplyWidgetGlass();
		};
		base.Closed += delegate
		{
			bool disable = !stopped;
			stopped = true;
			M.Closed(this, disable);
		};
	}

	public void Stop()
	{
		if (!stopped)
		{
			stopped = true;
			Close();
		}
	}

	public void UpdateOptions(List<string> kinds)
	{
		WidgetOptions widgets = M.C.Prefs.Widgets;
		Kinds = kinds.ToList();
		bool flag = signature != string.Join(",", Kinds) + "/" + M.C.Prefs.Light + "/" + widgets.CompactMusic;
		LightTheme = M.C.Prefs.Light;
		MotionEnabled = M.C.Prefs.Dock.MotionEnabled;
		MotionLevel = M.C.Prefs.Dock.MotionLevel;
		ConfigureGlass(M.C.Prefs.Dock);
		GlassPercent = M.C.Prefs.Dock.OpacityPercent;
		base.Topmost = widgets.Topmost;
		moveGrip.Visibility = ((widgets.Locked || Kinds.SequenceEqual(new string[1] { "clock" })) ? Visibility.Collapsed : Visibility.Visible);
		resizeGrip.Visibility = (widgets.Locked ? Visibility.Collapsed : Visibility.Visible);
		Polish.Apply(this, LightTheme);
		Rect workArea = SystemParameters.WorkArea;
		base.Width = Math.Max(Math.Min(360.0, workArea.Width - 30.0), Math.Min(widgets.Position(PanelId).Width.GetValueOrDefault(450.0), workArea.Width - 30.0));
		if (flag)
		{
			Build();
			signature = string.Join(",", Kinds) + "/" + LightTheme + "/" + widgets.CompactMusic;
		}
		Tick();
		UpdateMusic();
		UpdateWeather();
		FitContent();
		WidgetPanelPosition widgetPanelPosition = widgets.Position(PanelId);
		double? num = widgetPanelPosition.Left;
		double? num2 = widgetPanelPosition.Top;
		if (PanelId == 0)
		{
			num = num ?? M.C.Prefs.Placement.WidgetLeft;
			num2 = num2 ?? M.C.Prefs.Placement.WidgetTop;
		}
		base.Left = FeatureRules.Position(num ?? (workArea.Right - base.Width - 18.0 - (double)(PanelId % 2) * (base.Width - 8.0)), workArea.Left, workArea.Width, base.Width);
		base.Top = FeatureRules.Position(num2 ?? (workArea.Top + 18.0 + (double)(PanelId / 2 * 245)), workArea.Top, workArea.Height, base.Height);
		Backdrop(LightTheme);
		ApplyWidgetGlass();
		Tick();
		UpdateMusic();
		UpdateWeather();
	}

	private void FitContent()
	{
		if (stack == null || contentScale == null || building || resizing)
		{
			return;
		}
		resizing = true;
		try
		{
			Rect workArea = SystemParameters.WorkArea;
			double num = Math.Max(100.0, base.Width - 28.0);
			if (clock != null)
			{
				clock.FontSize = 48.0;
			}
			stack.Width = 390.0;
			stack.Measure(new Size(422.0, double.PositiveInfinity));
			double num2 = Math.Ceiling(stack.DesiredSize.Height);
			minimumWidgetHeight = WidgetSizing.MinimumHeight(num2, workArea.Height - 30.0);
			double defaultValue = num2 * (num / 422.0) + 28.0;
			WidgetPanelPosition widgetPanelPosition = M.C.Prefs.Widgets.Position(PanelId);
			double num3 = Math.Max(minimumWidgetHeight, Math.Min(workArea.Height - 30.0, widgetPanelPosition.Height.GetValueOrDefault(defaultValue)));
			contentScale.Width = num;
			contentScale.Height = Math.Max(100.0, num3 - 28.0);
			viewport.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
			viewport.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
			if (widgetPanelPosition.Height.HasValue)
			{
				widgetPanelPosition.Height = num3;
			}
			if (Math.Abs(base.Height - num3) > 0.5 || double.IsNaN(base.Height))
			{
				base.Height = num3;
				if (base.IsVisible)
				{
					base.Top = FeatureRules.Position(base.Top, workArea.Top, workArea.Height, base.Height);
					Backdrop(LightTheme);
					ApplyWidgetGlass();
				}
			}
		}
		finally
		{
			resizing = false;
		}
	}

	private void ApplyWidgetGlass()
	{
		double num = WidgetSizing.Background(M.C.Prefs.Widgets, M.C.Prefs.Widgets.Position(PanelId));
		byte a = (byte)Math.Round(num * 255.0 / 100.0);
		Surface.Background = new SolidColorBrush(LightTheme ? Color.FromArgb(a, 234, 237, 244) : Color.FromArgb(a, 24, 26, 32));
		Surface.BorderBrush = ((num == 0.0) ? Brushes.Transparent : Polish.Line(LightTheme));
		Surface.Effect = ((num == 0.0) ? null : new DropShadowEffect
		{
			BlurRadius = 25.0,
			ShadowDepth = 7.0,
			Opacity = 0.28,
			Color = Colors.Black
		});
		foreach (Border item in from b in Root.Children.OfType<Border>()
			where b.Tag as string == "backdrop"
			select b)
		{
			item.Opacity = num / 100.0;
		}
		if (stack == null)
		{
			return;
		}
		foreach (Border item2 in stack.Children.OfType<Border>())
		{
			Brush brush = Polish.Card(LightTheme).Clone();
			brush.Opacity = num / 100.0;
			item2.Background = brush;
			item2.BorderBrush = ((num == 0.0) ? Brushes.Transparent : Polish.Line(LightTheme));
		}
	}

	private Button Small(string text, Action action, double width = 30.0)
	{
		Button button = Button(text, delegate
		{
			action();
		}, width);
		button.Height = 28.0;
		button.Margin = new Thickness(2.0, 0.0, 2.0, 0.0);
		button.FontSize = 12.0;
		return button;
	}

	private Button Glyph(string icon, Action action, double size = 34.0, bool primary = false)
	{
		Button button = Button("", delegate
		{
			action();
		}, size);
		button.Height = size;
		button.Margin = new Thickness(3.0);
		button.Content = Polish.Icon(icon, primary ? Polish.Ink(light: true) : base.TextBrush, (size >= 48.0) ? 22 : 17);
		if (primary)
		{
			button.Style = Polish.ButtonStyle(light: true);
			button.Background = new LinearGradientBrush(Color.FromRgb(224, 234, byte.MaxValue), Colors.White, 90.0);
			button.BorderBrush = Brushes.Transparent;
			FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(Border));
			frameworkElementFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(size / 2.0));
			frameworkElementFactory.SetBinding(Border.BackgroundProperty, new Binding("Background")
			{
				RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
			});
			FrameworkElementFactory frameworkElementFactory2 = new FrameworkElementFactory(typeof(ContentPresenter));
			frameworkElementFactory2.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
			frameworkElementFactory2.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
			frameworkElementFactory.AppendChild(frameworkElementFactory2);
			button.Template = new ControlTemplate(typeof(Button))
			{
				VisualTree = frameworkElementFactory
			};
		}
		return button;
	}

	private void Build()
	{
		building = true;
		try
		{
			shownArtwork = null;
			shownPlaying = null;
			ContentGrid.Children.Clear();
			clock = (date = (clockStatus = (weatherTemperature = (weatherPlace = (weatherDetails = (weatherStatus = (musicTitle = (musicArtist = (musicStatus = (noteStatus = null))))))))));
			cover = null;
			play = null;
			notePicker = null;
			noteTitle = (noteBody = null);
			currentNote = null;
			stack = new StackPanel
			{
				Margin = new Thickness(16.0, Kinds.SequenceEqual(new string[1] { "clock" }) ? 16 : 25, 16.0, 16.0)
			};
			contentScale = new Viewbox
			{
				Child = stack,
				Stretch = Stretch.Uniform,
				StretchDirection = StretchDirection.Both,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Top
			};
			viewport = new ScrollViewer
			{
				Content = contentScale,
				VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
			};
			ContentGrid.Children.Add(viewport);
			foreach (string kind in Kinds)
			{
				StackPanel stackPanel = new StackPanel();
				Border element = new Border
				{
					Background = Polish.Card(LightTheme),
					BorderBrush = Polish.Line(LightTheme),
					BorderThickness = new Thickness(1.0),
					CornerRadius = new CornerRadius(22.0),
					Tag = "widgetCard",
					Padding = new Thickness(18.0),
					Margin = new Thickness(0.0, 0.0, 0.0, 14.0),
					Child = stackPanel
				};
				stack.Children.Add(element);
				Header(stackPanel, kind);
				switch (kind)
				{
				case "clock":
					Clock(stackPanel);
					break;
				case "weather":
					Weather(stackPanel);
					break;
				case "music":
					Music(stackPanel);
					break;
				default:
					Notes(stackPanel);
					break;
				}
			}
			if (stack.Children.Count > 0)
			{
				((Border)stack.Children[stack.Children.Count - 1]).Margin = new Thickness(0.0);
			}
		}
		finally
		{
			building = false;
		}
	}

	private void Header(StackPanel parent, string kind)
	{
		int num = Array.IndexOf(WidgetRules.Kinds, kind);
		Grid grid = new Grid
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		TextBlock textBlock = new TextBlock
		{
			Text = WidgetRules.Names[num],
			Foreground = base.MutedBrush,
			FontSize = 11.0,
			FontWeight = FontWeights.SemiBold,
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Left,
			Cursor = Cursors.SizeAll,
			ToolTip = "拖动标题移动组件"
		};
		textBlock.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
		{
			if (!M.C.Prefs.Widgets.Locked && e.ButtonState == MouseButtonState.Pressed)
			{
				DragMove();
				WidgetPanelPosition widgetPanelPosition2 = M.C.Prefs.Widgets.Position(PanelId);
				Rect workArea = SystemParameters.WorkArea;
				base.Left = FeatureRules.Position(base.Left, workArea.Left, workArea.Width, base.Width);
				base.Top = FeatureRules.Position(base.Top, workArea.Top, workArea.Height, base.Height);
				widgetPanelPosition2.Left = base.Left;
				widgetPanelPosition2.Top = base.Top;
				M.C.Save();
				Backdrop(LightTheme);
				ApplyWidgetGlass();
			}
		};
		grid.Children.Add(textBlock);
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		Button options = Small("•••", delegate
		{
		});
		options.ToolTip = "融合、分离与组件设置";
		options.Click += delegate
		{
			ContextMenu contextMenu = new ContextMenu();
			if (kind == "clock")
			{
				MenuItem menuItem = new MenuItem
				{
					Header = "闹钟与倒计时"
				};
				menuItem.Click += delegate
				{
					ClockSettings();
				};
				contextMenu.Items.Add(menuItem);
				contextMenu.Items.Add(new Separator());
			}
			if (kind == "music")
			{
				MenuItem menuItem2 = new MenuItem
				{
					Header = "系统音乐模式",
					IsCheckable = true,
					IsChecked = !M.Music.Local
				};
				menuItem2.Click += delegate
				{
					M.Music.Mode(local: false);
				};
				contextMenu.Items.Add(menuItem2);
				MenuItem menuItem3 = new MenuItem
				{
					Header = "本地音乐模式",
					IsCheckable = true,
					IsChecked = M.Music.Local
				};
				menuItem3.Click += delegate
				{
					M.Music.Mode(local: true);
				};
				contextMenu.Items.Add(menuItem3);
				MenuItem compatibility = new MenuItem
				{
					Header = "自动补充音乐封面",
					IsCheckable = true,
					IsChecked = M.C.Prefs.Widgets.AutoNativeMediaCompatibility
				};
				compatibility.Click += delegate
				{
					M.C.Prefs.Widgets.AutoNativeMediaCompatibility = compatibility.IsChecked;
					M.C.Prefs.Widgets.NativeMediaCompatibility = false;
					M.C.Save();
					M.Music.Update();
				};
				contextMenu.Items.Add(compatibility);
				MenuItem menuItem4 = new MenuItem
				{
					Header = "查看音乐读取状态"
				};
				menuItem4.Click += delegate
				{
					MessageBox.Show(this, M.Music.Diagnostics(), "音乐读取状态");
				};
				contextMenu.Items.Add(menuItem4);
				MenuItem menuItem5 = new MenuItem
				{
					Header = "播放列表"
				};
				menuItem5.Click += delegate
				{
					Playlist();
				};
				contextMenu.Items.Add(menuItem5);
				MenuItem menuItem6 = new MenuItem
				{
					Header = "添加音乐"
				};
				menuItem6.Click += delegate
				{
					M.Music.Pick(this, append: true);
				};
				contextMenu.Items.Add(menuItem6);
				contextMenu.Items.Add(new Separator());
			}
			MenuItem menuItem7 = new MenuItem
			{
				Header = "分离为独立面板"
			};
			menuItem7.Click += delegate
			{
				M.Assign(kind, WidgetRules.FreePanel(M.C.Prefs.Widgets, kind));
			};
			contextMenu.Items.Add(menuItem7);
			for (int i = 0; i < 4; i++)
			{
				int panel = i;
				MenuItem menuItem8 = new MenuItem
				{
					Header = "融合到面板 " + (char)(65 + i),
					IsCheckable = true,
					IsChecked = (WidgetRules.Panel(M.C.Prefs.Widgets, kind) == i)
				};
				menuItem8.Click += delegate
				{
					M.Assign(kind, panel);
				};
				contextMenu.Items.Add(menuItem8);
			}
			contextMenu.Items.Add(new Separator());
			MenuItem lockPosition = new MenuItem
			{
				Header = "锁定组件位置和尺寸",
				IsCheckable = true,
				IsChecked = M.C.Prefs.Widgets.Locked
			};
			lockPosition.Click += delegate
			{
				M.C.Prefs.Widgets.Locked = lockPosition.IsChecked;
				M.C.Save();
				M.Sync();
			};
			contextMenu.Items.Add(lockPosition);
			MenuItem menuItem9 = new MenuItem
			{
				Header = "恢复自动尺寸"
			};
			menuItem9.Click += delegate
			{
				WidgetPanelPosition widgetPanelPosition = M.C.Prefs.Widgets.Position(PanelId);
				widgetPanelPosition.Width = (widgetPanelPosition.Height = null);
				M.C.Save();
				M.Sync();
			};
			contextMenu.Items.Add(menuItem9);
			MenuItem newItem = new MenuItem
			{
				Header = "背景透明度（0% 为全透明）",
				IsEnabled = false
			};
			contextMenu.Items.Add(newItem);
			Slider transparency = new Slider
			{
				Width = 190.0,
				Minimum = 0.0,
				Maximum = 100.0,
				Value = WidgetSizing.Background(M.C.Prefs.Widgets, M.C.Prefs.Widgets.Position(PanelId)),
				IsSnapToTickEnabled = true,
				TickFrequency = 5.0
			};
			transparency.ValueChanged += delegate
			{
				M.C.Prefs.Widgets.Position(PanelId).OpacityPercent = transparency.Value;
				ApplyWidgetGlass();
			};
			transparency.PreviewMouseLeftButtonUp += delegate
			{
				M.C.Save();
			};
			transparency.KeyUp += delegate
			{
				M.C.Save();
			};
			contextMenu.Items.Add(new MenuItem
			{
				Header = transparency,
				StaysOpenOnClick = true
			});
			contextMenu.Closed += delegate
			{
				M.C.Save();
			};
			MenuItem menuItem10 = new MenuItem
			{
				Header = "组件设置"
			};
			menuItem10.Click += delegate
			{
				M.C.ConfigureWidgets();
			};
			contextMenu.Items.Add(menuItem10);
			options.ContextMenu = contextMenu;
			contextMenu.PlacementTarget = options;
			contextMenu.IsOpen = true;
		};
		stackPanel.Children.Add(options);
		Button button = Glyph("close", delegate
		{
			M.Enable(kind, value: false);
		}, 26.0);
		button.ToolTip = "关闭这个组件";
		stackPanel.Children.Add(button);
		grid.Children.Add(stackPanel);
		parent.Children.Add(grid);
	}

	private void Clock(StackPanel parent)
	{
		clock = new TextBlock
		{
			FontSize = 48.0,
			FontWeight = FontWeights.Light,
			Foreground = base.TextBrush
		};
		date = new TextBlock
		{
			FontSize = 12.0,
			Foreground = base.MutedBrush,
			Margin = new Thickness(3.0, 5.0, 0.0, 0.0)
		};
		clockStatus = new TextBlock
		{
			FontSize = 11.0,
			Foreground = base.MutedBrush,
			Margin = new Thickness(3.0, 9.0, 0.0, 0.0),
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		StackPanel stackPanel = new StackPanel
		{
			Background = Brushes.Transparent,
			Cursor = Cursors.SizeAll,
			ToolTip = "拖动时间或日期移动组件；锁定时不可移动"
		};
		stackPanel.Children.Add(clock);
		stackPanel.Children.Add(date);
		stackPanel.Children.Add(clockStatus);
		stackPanel.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
		{
			if (e.ClickCount != 1 || M.C.Prefs.Widgets.Locked)
			{
				return;
			}
			e.Handled = true;
			try
			{
				DragMove();
				Rect workArea = SystemParameters.WorkArea;
				base.Left = FeatureRules.Position(base.Left, workArea.Left, workArea.Width, base.Width);
				base.Top = FeatureRules.Position(base.Top, workArea.Top, workArea.Height, base.Height);
				WidgetPanelPosition widgetPanelPosition = M.C.Prefs.Widgets.Position(PanelId);
				widgetPanelPosition.Left = base.Left;
				widgetPanelPosition.Top = base.Top;
				M.C.Save();
				Backdrop(LightTheme);
				ApplyWidgetGlass();
			}
			catch (InvalidOperationException)
			{
			}
		};
		parent.Children.Add(stackPanel);
	}

	private void ClockSettings()
	{
		WidgetOptions o = M.C.Prefs.Widgets;
		Window w = FeatureUi.Dialog("闹钟与倒计时", this, 540.0, 650.0);
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(24.0)
		};
		w.Content = new ScrollViewer
		{
			Content = stackPanel,
			VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "闹钟",
			FontSize = 21.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		});
		ListBox list = new ListBox
		{
			Height = 150.0
		};
		stackPanel.Children.Add(list);
		Action fill = delegate
		{
			int selectedIndex3 = list.SelectedIndex;
			list.ItemsSource = o.Alarms.Select((ClockAlarm a) => (a.Enabled ? "开启  " : "关闭  ") + a.Hour.ToString("00") + ":" + a.Minute.ToString("00") + " · " + (a.Daily ? "每天" : "单次") + " · " + a.Label).ToList();
			if (selectedIndex3 >= 0 && selectedIndex3 < o.Alarms.Count)
			{
				list.SelectedIndex = selectedIndex3;
			}
		};
		fill();
		WrapPanel wrapPanel = new WrapPanel
		{
			Margin = new Thickness(0.0, 8.0, 0.0, 12.0)
		};
		stackPanel.Children.Add(wrapPanel);
		wrapPanel.Children.Add(FeatureUi.Button("开启 / 关闭", delegate
		{
			int selectedIndex2 = list.SelectedIndex;
			if (selectedIndex2 >= 0)
			{
				ClockAlarm clockAlarm = o.Alarms[selectedIndex2];
				clockAlarm.Enabled = !clockAlarm.Enabled;
				if (clockAlarm.Enabled)
				{
					clockAlarm.DueLocal = ClockRules.Next(clockAlarm.Hour, clockAlarm.Minute, DateTime.Now);
				}
				M.C.Save();
				fill();
			}
		}));
		wrapPanel.Children.Add(FeatureUi.Button("删除已选", delegate
		{
			int selectedIndex = list.SelectedIndex;
			if (selectedIndex >= 0)
			{
				o.Alarms.RemoveAt(selectedIndex);
				M.C.Save();
				fill();
			}
		}));
		WrapPanel wrapPanel2 = new WrapPanel();
		stackPanel.Children.Add(wrapPanel2);
		TextBox time = new TextBox
		{
			Text = DateTime.Now.AddMinutes(1.0).ToString("HH:mm"),
			Width = 75.0,
			MinHeight = 38.0,
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
		};
		TextBox label = new TextBox
		{
			Text = "闹钟",
			Width = 180.0,
			MinHeight = 38.0
		};
		wrapPanel2.Children.Add(time);
		wrapPanel2.Children.Add(label);
		CheckBox daily = new CheckBox
		{
			Content = "每天重复",
			IsChecked = false,
			Margin = new Thickness(0.0, 10.0, 0.0, 8.0)
		};
		stackPanel.Children.Add(daily);
		stackPanel.Children.Add(FeatureUi.Button("添加闹钟", delegate
		{
			if (!DateTime.TryParseExact(time.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result2))
			{
				MessageBox.Show(w, "请填写 24 小时制时间，例如 08:30。");
			}
			else if (o.Alarms.Count >= 50)
			{
				MessageBox.Show(w, "最多添加 50 个闹钟。");
			}
			else
			{
				o.Alarms.Add(new ClockAlarm
				{
					Hour = result2.Hour,
					Minute = result2.Minute,
					Label = (string.IsNullOrWhiteSpace(label.Text) ? "闹钟" : label.Text.Trim()),
					Daily = daily.IsChecked.GetValueOrDefault(),
					DueLocal = ClockRules.Next(result2.Hour, result2.Minute, DateTime.Now)
				});
				M.C.Save();
				fill();
			}
		}));
		stackPanel.Children.Add(new TextBlock
		{
			Text = "倒计时",
			FontSize = 21.0,
			Margin = new Thickness(0.0, 22.0, 0.0, 10.0)
		});
		TextBlock countdown = new TextBlock
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		stackPanel.Children.Add(countdown);
		TextBox minutes = new TextBox
		{
			Text = "5",
			Width = 80.0,
			MinHeight = 38.0,
			HorizontalAlignment = HorizontalAlignment.Left
		};
		stackPanel.Children.Add(minutes);
		stackPanel.Children.Add(new TextBlock
		{
			Text = "分钟 · 1–1440",
			Margin = new Thickness(0.0, 5.0, 0.0, 8.0)
		});
		WrapPanel wrapPanel3 = new WrapPanel();
		stackPanel.Children.Add(wrapPanel3);
		wrapPanel3.Children.Add(FeatureUi.Button("开始", delegate
		{
			if (!int.TryParse(minutes.Text, out var result) || result < 1 || result > 1440)
			{
				MessageBox.Show(w, "请输入 1 到 1440 分钟。");
			}
			else
			{
				ClockRules.Start(o, result * 60, DateTime.UtcNow);
				M.C.Save();
			}
		}));
		wrapPanel3.Children.Add(FeatureUi.Button("暂停 / 继续", delegate
		{
			if (o.TimerDueUtc.HasValue)
			{
				ClockRules.Pause(o, DateTime.UtcNow);
			}
			else
			{
				ClockRules.Resume(o, DateTime.UtcNow);
			}
			M.C.Save();
		}));
		wrapPanel3.Children.Add(FeatureUi.Button("取消", delegate
		{
			ClockRules.Cancel(o);
			M.C.Save();
		}));
		stackPanel.Children.Add(new TextBlock
		{
			Text = "到时弹窗并播放系统提示音，可稍后 5 分钟再提醒。GlassBoard 需保持运行；隐藏时钟仍会提醒。",
			TextWrapping = TextWrapping.Wrap,
			FontSize = 12.0,
			Margin = new Thickness(0.0, 16.0, 0.0, 0.0)
		});
		DispatcherTimer refresh = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1.0)
		};
		refresh.Tick += delegate
		{
			countdown.Text = ClockRules.Display(o, DateTime.UtcNow);
		};
		w.Closed += delegate
		{
			refresh.Stop();
		};
		countdown.Text = ClockRules.Display(o, DateTime.UtcNow);
		refresh.Start();
		w.ShowDialog();
		Tick();
	}

	private void Weather(StackPanel parent)
	{
		weatherPlace = new TextBlock
		{
			Foreground = base.TextBrush,
			FontSize = 14.0,
			FontWeight = FontWeights.Medium,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		};
		parent.Children.Add(weatherPlace);
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(60.0)
		});
		StackPanel stackPanel = new StackPanel();
		grid.Children.Add(stackPanel);
		weatherTemperature = new TextBlock
		{
			Foreground = base.TextBrush,
			FontSize = 43.0,
			FontWeight = FontWeights.Light,
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
		};
		stackPanel.Children.Add(weatherTemperature);
		FrameworkElement element = Polish.Icon("sun", Polish.Color("#EDBF66"), 44.0);
		Grid.SetColumn(element, 1);
		grid.Children.Add(element);
		parent.Children.Add(grid);
		weatherDetails = new TextBlock
		{
			Foreground = base.TextBrush,
			FontSize = 12.0,
			Margin = new Thickness(0.0, 5.0, 0.0, 8.0)
		};
		parent.Children.Add(weatherDetails);
		Grid grid2 = new Grid();
		weatherStatus = new TextBlock
		{
			Foreground = base.MutedBrush,
			FontSize = 10.0,
			VerticalAlignment = VerticalAlignment.Center,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		};
		parent.Children.Add(weatherStatus);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		stackPanel2.Children.Add(Small("刷新", delegate
		{
			M.Weather.Update(force: true);
		}, 45.0));
		Button button = Small("Open-Meteo", delegate
		{
			Process.Start(new ProcessStartInfo("https://open-meteo.com/")
			{
				UseShellExecute = true
			});
		}, 88.0);
		button.FontSize = 10.0;
		stackPanel2.Children.Add(button);
		grid2.Children.Add(stackPanel2);
		parent.Children.Add(grid2);
	}

	private void Music(StackPanel parent)
	{
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(72.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		cover = new Border
		{
			Width = 72.0,
			Height = 72.0,
			CornerRadius = new CornerRadius(16.0),
			Background = Polish.Card(LightTheme),
			Clip = new RectangleGeometry(new Rect(0.0, 0.0, 72.0, 72.0), 16.0, 16.0)
		};
		grid.Children.Add(cover);
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(14.0, 2.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetColumn(stackPanel, 1);
		grid.Children.Add(stackPanel);
		musicTitle = new TextBlock
		{
			Foreground = base.TextBrush,
			FontSize = 17.0,
			FontWeight = FontWeights.SemiBold,
			TextWrapping = TextWrapping.Wrap,
			MaxHeight = 48.0,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		musicArtist = new TextBlock
		{
			Foreground = base.MutedBrush,
			FontSize = 12.0,
			TextTrimming = TextTrimming.CharacterEllipsis,
			Margin = new Thickness(0.0, 5.0, 0.0, 0.0)
		};
		stackPanel.Children.Add(musicTitle);
		stackPanel.Children.Add(musicArtist);
		parent.Children.Add(grid);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 8.0, 0.0, 3.0)
		};
		Button button = Glyph("previous", delegate
		{
			M.Music.Action("previous");
		});
		button.ToolTip = "上一首";
		stackPanel2.Children.Add(button);
		play = Glyph("play", delegate
		{
			if (M.Music.Local && M.C.Prefs.Widgets.Playlist.Count == 0)
			{
				M.Music.Pick(this);
			}
			else
			{
				M.Music.Action("toggle");
			}
		}, 44.0, primary: true);
		play.ToolTip = "播放 / 暂停";
		stackPanel2.Children.Add(play);
		Button button2 = Glyph("next", delegate
		{
			M.Music.Action("next");
		});
		button2.ToolTip = "下一首";
		stackPanel2.Children.Add(button2);
		parent.Children.Add(stackPanel2);
		musicStatus = new TextBlock
		{
			Foreground = base.MutedBrush,
			FontSize = 10.0,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
		};
		parent.Children.Add(musicStatus);
	}

	private void Playlist()
	{
		if (M.Music == null)
		{
			return;
		}
		Window w = FeatureUi.Dialog("播放列表", this, 540.0, 510.0);
		DockPanel dockPanel = new DockPanel
		{
			Margin = new Thickness(22.0)
		};
		w.Content = dockPanel;
		WrapPanel wrapPanel = new WrapPanel();
		DockPanel.SetDock(wrapPanel, Dock.Bottom);
		dockPanel.Children.Add(wrapPanel);
		ListBox list = new ListBox();
		dockPanel.Children.Add(list);
		Action fill = delegate
		{
			list.ItemsSource = null;
			list.ItemsSource = M.C.Prefs.Widgets.Playlist.Select((string p) => System.IO.Path.GetFileNameWithoutExtension(p)).ToList();
			list.SelectedIndex = M.C.Prefs.Widgets.TrackIndex;
		};
		fill();
		wrapPanel.Children.Add(FeatureUi.Button("添加", delegate
		{
			M.Music.Pick(w, append: true);
			fill();
		}));
		wrapPanel.Children.Add(FeatureUi.Button("播放已选", delegate
		{
			if (list.SelectedIndex >= 0)
			{
				M.Music.Select(list.SelectedIndex);
			}
			fill();
		}));
		wrapPanel.Children.Add(FeatureUi.Button("移除已选", delegate
		{
			if (list.SelectedIndex >= 0)
			{
				M.Music.Remove(list.SelectedIndex);
			}
			fill();
		}));
		list.MouseDoubleClick += delegate
		{
			if (list.SelectedIndex >= 0)
			{
				M.Music.Select(list.SelectedIndex);
			}
		};
		w.ShowDialog();
	}

	private void Notes(StackPanel parent)
	{
		notePicker = new ComboBox
		{
			MinHeight = 42.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0),
			HorizontalContentAlignment = HorizontalAlignment.Stretch
		};
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(TextBlock));
		frameworkElementFactory.SetBinding(TextBlock.TextProperty, new Binding("DisplayTitle"));
		frameworkElementFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
		frameworkElementFactory.SetValue(TextBlock.ForegroundProperty, base.TextBrush);
		frameworkElementFactory.SetValue(TextBlock.FontSizeProperty, 14.0);
		frameworkElementFactory.SetValue(FrameworkElement.MaxWidthProperty, Math.Max(80.0, base.Width - 160.0));
		notePicker.ItemTemplate = new DataTemplate
		{
			VisualTree = frameworkElementFactory
		};
		parent.Children.Add(notePicker);
		noteTitle = new TextBox
		{
			FontSize = 17.0,
			MaxLength = 120,
			FontWeight = FontWeights.SemiBold,
			MinHeight = 48.0,
			Padding = new Thickness(12.0, 9.0, 12.0, 9.0),
			TextWrapping = TextWrapping.Wrap,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			VerticalContentAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0),
			ToolTip = "备忘录标题"
		};
		parent.Children.Add(noteTitle);
		noteBody = new TextBox
		{
			Height = 170.0,
			VerticalContentAlignment = VerticalAlignment.Top,
			AcceptsReturn = true,
			AcceptsTab = true,
			TextWrapping = TextWrapping.Wrap,
			VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
			FontSize = 14.0,
			ToolTip = "随手记下想法，内容自动保存",
			Background = Polish.Color(LightTheme ? "#72FFF6D8" : "#703E3B35")
		};
		parent.Children.Add(noteBody);
		noteTitle.TextChanged += delegate
		{
			NoteEdited();
		};
		noteBody.TextChanged += delegate
		{
			NoteEdited();
		};
		notePicker.DropDownOpened += delegate
		{
			M.Notes.Flush();
			notePicker.Items.Refresh();
		};
		notePicker.SelectionChanged += delegate
		{
			if (!editingNotes && notePicker.SelectedItem is NoteItem noteItem)
			{
				M.Notes.Select(noteItem.Id);
				LoadNote();
			}
		};
		WrapPanel wrapPanel = new WrapPanel
		{
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
		};
		wrapPanel.Children.Add(Small("新建", delegate
		{
			M.Notes.Add();
			M.RefreshNotes();
		}, 51.0));
		wrapPanel.Children.Add(Small("清单", delegate
		{
			noteBody.SelectedText = "☐ ";
			noteBody.Focus();
		}, 51.0));
		wrapPanel.Children.Add(Small("导出", delegate
		{
			SaveFileDialog saveFileDialog = new SaveFileDialog
			{
				FileName = "备忘录.txt",
				Filter = "文本文件|*.txt"
			};
			if (saveFileDialog.ShowDialog(this).GetValueOrDefault())
			{
				try
				{
					M.Notes.Export(saveFileDialog.FileName);
				}
				catch (Exception ex)
				{
					MessageBox.Show(this, ex.Message, "导出失败");
				}
			}
		}, 51.0));
		wrapPanel.Children.Add(Small("删除", delegate
		{
			if (MessageBox.Show(this, "删除这条备忘录？", "GlassBoard", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
			{
				M.Notes.Remove();
				M.RefreshNotes();
			}
		}, 51.0));
		parent.Children.Add(wrapPanel);
		noteStatus = new TextBlock
		{
			Foreground = base.MutedBrush,
			FontSize = 10.0,
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
		};
		parent.Children.Add(noteStatus);
		ReloadNotes();
	}

	private void NoteEdited()
	{
		if (editingNotes || currentNote == null)
		{
			return;
		}
		bool num = currentNote.Title != noteTitle.Text;
		currentNote.Title = noteTitle.Text;
		currentNote.Body = noteBody.Text;
		if (num)
		{
			editingNotes = true;
			try
			{
				notePicker.Items.Refresh();
			}
			finally
			{
				editingNotes = false;
			}
		}
		M.Notes.Changed();
		if (noteStatus != null)
		{
			noteStatus.Text = M.Notes.Status;
		}
		FitContent();
	}

	public void ReloadNotes()
	{
		if (notePicker != null)
		{
			editingNotes = true;
			notePicker.ItemsSource = null;
			notePicker.ItemsSource = M.Notes.Book.Items;
			notePicker.SelectedItem = M.Notes.Book.Selected;
			editingNotes = false;
			LoadNote();
		}
	}

	private void LoadNote()
	{
		editingNotes = true;
		currentNote = M.Notes.Book.Selected;
		noteTitle.Text = currentNote.Title;
		noteBody.Text = currentNote.Body;
		editingNotes = false;
		FitContent();
	}

	public void Tick()
	{
		if (clock != null)
		{
			clock.Text = DateTime.Now.ToString("HH:mm");
			date.Text = DateTime.Now.ToString("M月d日  dddd");
			if (clockStatus != null)
			{
				clockStatus.Text = ClockRules.Display(M.C.Prefs.Widgets, DateTime.UtcNow);
				clockStatus.ToolTip = clockStatus.Text;
			}
		}
		if (noteStatus != null)
		{
			noteStatus.Text = M.Notes.Status;
		}
	}

	public void UpdateWeather()
	{
		if (weatherTemperature != null)
		{
			WeatherSnapshot snapshot = M.Weather.Snapshot;
			weatherTemperature.Text = ((snapshot == null) ? "—°" : (snapshot.Current.Temperature.ToString("0.#") + "°"));
			weatherPlace.Text = ((snapshot != null) ? (snapshot.Place.Name + " · " + snapshot.Place.Country) : (string.IsNullOrWhiteSpace(M.C.Prefs.Widgets.City) ? "选择你的城市" : M.C.Prefs.Widgets.City));
			weatherDetails.Text = ((snapshot == null) ? "" : (FeatureRules.WeatherLabel(snapshot.Current.Code) + "  ·  风速 " + snapshot.Current.Wind.ToString("0.#") + " km/h"));
			weatherStatus.Text = M.Weather.Message;
			weatherPlace.ToolTip = weatherPlace.Text;
			FitContent();
		}
	}

	public void UpdateMusic()
	{
		if (!building && musicStatus != null && play != null && M.Music != null)
		{
			MusicEngine music = M.Music;
			musicTitle.Text = music.Title;
			musicTitle.ToolTip = music.Title;
			musicArtist.Text = music.Artist;
			musicArtist.ToolTip = music.Album;
			musicStatus.Text = ((!string.IsNullOrWhiteSpace(music.Status)) ? music.Status : (music.Local ? ("本地播放 · " + M.C.Prefs.Widgets.Playlist.Count + " 首") : "系统媒体会话"));
			musicStatus.ToolTip = music.Status;
			if (shownArtwork != (music.Artwork ?? music.AppIcon) || cover.Child == null)
			{
				shownArtwork = music.Artwork ?? music.AppIcon;
				cover.Child = ((shownArtwork == null) ? Polish.Icon("music", Polish.Color("#E8EDFF"), 34.0) : new Image
				{
					Source = shownArtwork,
					Stretch = ((music.Artwork == null) ? Stretch.Uniform : Stretch.UniformToFill)
				});
			}
			if (shownPlaying != music.Playing)
			{
				shownPlaying = music.Playing;
				play.Content = Polish.Icon(music.Playing ? "pause" : "play", Polish.Ink(light: true), 22.0);
			}
			FitContent();
		}
	}
}
