using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;

namespace GlassBoard;

public class DockWindow : GlassWindow
{
	private Controller C;

	private StackPanel Items = new StackPanel
	{
		Orientation = System.Windows.Controls.Orientation.Horizontal,
		VerticalAlignment = VerticalAlignment.Center,
		HorizontalAlignment = System.Windows.HorizontalAlignment.Center
	};

	private HwndSource source;

	private HotkeyManager hotkeys;

	private DispatcherTimer autoTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(120.0)
	};

	private DispatcherTimer runningTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(3.0)
	};

	private AutoHidePolicy hidePolicy = new AutoHidePolicy();

	private bool renderedHidden;

	private double shownTop;

	private double shownLeft;

	private Thumb moveGrip;

	private bool dragging;

	/// <summary>已按下但还没超过拖拽阈值。</summary>
	private bool dragCandidate;

	/// <summary>拖动起点：窗口位置与按下时的屏幕坐标。</summary>
	private System.Windows.Point dragOrigin;

	private System.Windows.Point dragPointer;

	/// <summary>超过这个位移（像素）才算拖动，避免把点击启动误判成拖动。</summary>
	private const double DragThreshold = 6.0;

	/// <summary>拖动落点距屏幕边缘多少像素内就吸附到该边。</summary>
	private const double SnapDistance = 96.0;

	public double RestingTop => shownTop;

	public DockWindow(Controller c)
		: base(29.0)
	{
		C = c;
		base.Title = "GlassBoard Dock";
		base.Height = 112.0;
		base.ShowActivated = false;
		base.Topmost = false;
		ContentGrid.Children.Add(Items);
		moveGrip = new Thumb
		{
			Width = 42.0,
			Height = 6.0,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0.0, 7.0, 0.0, 0.0),
			Cursor = System.Windows.Input.Cursors.SizeAll,
			ToolTip = "按住拖动 Dock 到任意位置；靠近屏幕边缘会自动吸附到该边"
		};
		Root.Children.Add(moveGrip);
		moveGrip.Template = GlassWindow.GripTemplate();
		base.Closing += delegate(object s, CancelEventArgs e)
		{
			if (!C.ShuttingDown)
			{
				e.Cancel = true;
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					C.Quit();
				});
			}
		};
		// 整块 Dock 都能拖动：按下后位移超过阈值就转为拖拽，没超过则仍按普通点击处理（照常启动软件）。
		// 用鼠标捕获手动跟手，比 DragMove() 可靠：后者要求窗口处于激活状态。
		PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
		{
			BeginDrag(e);
		};
		PreviewMouseMove += delegate(object s, System.Windows.Input.MouseEventArgs e)
		{
			ContinueDrag(e);
		};
		PreviewMouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
		{
			EndDrag(e);
		};
		Surface.Cursor = System.Windows.Input.Cursors.SizeAll;
		moveGrip.DragStarted += delegate
		{
			dragging = true;
		};
		moveGrip.DragDelta += delegate(object s, DragDeltaEventArgs e)
		{
			if (!C.Prefs.Placement.DockLocked)
			{
				Rect workArea = SystemParameters.WorkArea;
				base.Left = FeatureRules.Position(base.Left + e.HorizontalChange, workArea.Left, workArea.Width, base.Width);
				base.Top = FeatureRules.Position(base.Top + e.VerticalChange, workArea.Top, workArea.Height, base.Height);
			}
		};
		moveGrip.DragCompleted += delegate
		{
			dragging = false;
			SnapToNearestEdge();
			shownTop = base.Top;
			shownLeft = base.Left;
			C.Save();
			Refresh(capture: true);
		};
		autoTimer.Tick += delegate
		{
			UpdateAutoHide();
		};
		runningTimer.Tick += delegate
		{
			if (C.Prefs.Dock.ShowRunning)
			{
				RunningWindows.Scan();
				UpdateRunning();
				C.Board.UpdateRunning();
			}
		};
		runningTimer.Start();
		base.SourceInitialized += delegate
		{
			source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
			source.AddHook(Hook);
			hotkeys = new HotkeyManager(RegisterKey, delegate(int id)
			{
				Native.UnregisterHotKey(source.Handle, id);
			});
			List<string> list = hotkeys.Start(C.Prefs.Hotkeys);
			if (list.Count > 0)
			{
				TrayWarning(string.Join("、", list));
			}
		};
		System.Windows.Controls.ContextMenu contextMenu = new System.Windows.Controls.ContextMenu();
		System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
		{
			Header = "外观设置"
		};
		menuItem.Click += delegate
		{
			C.Settings();
		};
		contextMenu.Items.Add(menuItem);
		System.Windows.Controls.MenuItem menuItem3 = new System.Windows.Controls.MenuItem
		{
			Header = "Dock 贴到左侧",
			IsCheckable = true,
			IsChecked = C.Prefs.Dock.Edge == 1
		};
		menuItem3.Click += delegate
		{
			SetEdge(1);
		};
		contextMenu.Items.Add(menuItem3);
		System.Windows.Controls.MenuItem menuItem4 = new System.Windows.Controls.MenuItem
		{
			Header = "Dock 贴到底部",
			IsCheckable = true,
			IsChecked = C.Prefs.Dock.Edge == 0
		};
		menuItem4.Click += delegate
		{
			SetEdge(0);
		};
		contextMenu.Items.Add(menuItem4);
		System.Windows.Controls.MenuItem menuItem5 = new System.Windows.Controls.MenuItem
		{
			Header = "Dock 贴到右侧",
			IsCheckable = true,
			IsChecked = C.Prefs.Dock.Edge == 2
		};
		menuItem5.Click += delegate
		{
			SetEdge(2);
		};
		contextMenu.Items.Add(menuItem5);
		System.Windows.Controls.MenuItem menuItem6 = new System.Windows.Controls.MenuItem
		{
			Header = "隐藏底板（纯透明）",
			IsCheckable = true,
			IsChecked = C.Prefs.Dock.Transparent
		};
		menuItem6.Click += delegate
		{
			C.Prefs.Dock.Background = ((!C.Prefs.Dock.Transparent) ? 1 : 0);
			C.Save();
			Refresh(capture: true);
		};
		contextMenu.Items.Add(menuItem6);
		System.Windows.Controls.MenuItem menuItem7 = new System.Windows.Controls.MenuItem
		{
			Header = "锁定 Dock 位置",
			IsCheckable = true,
			IsChecked = C.Prefs.Placement.DockLocked
		};
		menuItem7.Click += delegate
		{
			C.Prefs.Placement.DockLocked = !C.Prefs.Placement.DockLocked;
			C.Save();
			Refresh(capture: false);
		};
		contextMenu.Items.Add(menuItem7);
		System.Windows.Controls.MenuItem menuItem2 = new System.Windows.Controls.MenuItem
		{
			Header = "退出"
		};
		menuItem2.Click += delegate
		{
			C.Quit();
		};
		contextMenu.Items.Add(menuItem2);
		base.ContextMenu = contextMenu;
		SystemEvents.DisplaySettingsChanged += DisplayChanged;
	}

	/// <summary>设置停靠边并清除手工位置，让 Dock 回到该边的默认位置。</summary>
	public void SetEdge(int edge)
	{
		DockOptions dock = C.Prefs.Dock;
		dock.Edge = Math.Max(0, Math.Min(2, edge));
		C.Prefs.Placement.DockLeft = null;
		C.Prefs.Placement.DockTop = null;
		C.Save();
		Refresh(capture: true);
	}

	/// <summary>按下时只记录起点；真正是否拖拽由位移阈值决定，避免影响正常点击启动。</summary>
	private void BeginDrag(MouseButtonEventArgs e)
	{
		if (C.Prefs.Placement.DockLocked || e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		dragOrigin = new System.Windows.Point(base.Left, base.Top);
		dragPointer = PointToScreen(e.GetPosition(this));
		dragCandidate = true;
		dragging = false;
	}

	/// <summary>位移超过阈值后进入拖拽；拖动时窗口跟随鼠标。</summary>
	private void ContinueDrag(System.Windows.Input.MouseEventArgs e)
	{
		if (!dragCandidate || e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		System.Windows.Point current = PointToScreen(e.GetPosition(this));
		double dx = current.X - dragPointer.X;
		double dy = current.Y - dragPointer.Y;
		if (!dragging)
		{
			if (Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold)
			{
				return;
			}
			dragging = true;
			Surface.CaptureMouse();
			Cursor = System.Windows.Input.Cursors.SizeAll;
		}
		Rect workArea = SystemParameters.WorkArea;
		base.Left = FeatureRules.Position(dragOrigin.X + dx, workArea.Left, workArea.Width, base.Width);
		base.Top = FeatureRules.Position(dragOrigin.Y + dy, workArea.Top, workArea.Height, base.Height);
		e.Handled = true;
	}

	/// <summary>松手：真的拖动过才吸附保存；只是点击则让事件继续传给按钮。</summary>
	private void EndDrag(MouseButtonEventArgs e)
	{
		if (!dragCandidate)
		{
			return;
		}
		dragCandidate = false;
		if (!dragging)
		{
			return;
		}
		dragging = false;
		Surface.ReleaseMouseCapture();
		Cursor = null;
		SnapToNearestEdge();
		C.Save();
		Refresh(capture: true);
		e.Handled = true;
	}

	/// <summary>松手后按落点判定停靠边：靠近左/右边缘转竖向并吸附，靠近底部转横向并吸附。</summary>
	private void SnapToNearestEdge()
	{
		DockOptions dock = C.Prefs.Dock;
		Rect workArea = SystemParameters.WorkArea;
		double toLeft = base.Left - workArea.Left;
		double toRight = workArea.Right - (base.Left + base.Width);
		double toBottom = workArea.Bottom - (base.Top + base.Height);
		bool nearLeft = toLeft <= SnapDistance;
		bool nearRight = toRight <= SnapDistance;
		bool nearBottom = toBottom <= SnapDistance;
		if (nearLeft || nearRight)
		{
			dock.Edge = (nearLeft && (!nearRight || toLeft <= toRight)) ? 1 : 2;
			C.Prefs.Placement.DockLeft = null;
			C.Prefs.Placement.DockTop = null;
			return;
		}
		if (nearBottom)
		{
			dock.Edge = 0;
			C.Prefs.Placement.DockTop = null;
			C.Prefs.Placement.DockLeft = base.Left;
			return;
		}
		// 远离边缘：保留当前方向，停在用户拖到的位置。
		C.Prefs.Placement.DockLeft = base.Left;
		C.Prefs.Placement.DockTop = base.Top;
	}

	private void TrayWarning(string shortcuts)
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			if (C.Tray != null)
			{
				C.Tray.ShowBalloonTip(3000, "GlassBoard", shortcuts + " 已被占用，对应功能仍可从托盘操作。", ToolTipIcon.Info);
			}
		});
	}

	private bool RegisterKey(HotkeySetting key)
	{
		bool num = Native.RegisterHotKey(source.Handle, key.ActionId, key.Modifiers | 0x4000u, key.Key);
		if (!num)
		{
			Store.Log(new Win32Exception(Marshal.GetLastWin32Error(), "RegisterHotKey: " + HotkeyRules.Actions[key.ActionId - 1] + " " + HotkeyRules.Label(key)));
		}
		return num;
	}

	private void DisplayChanged(object s, EventArgs e)
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			C.Board.Hide();
			Refresh();
		});
	}

	public bool ApplyHotkeys(List<HotkeySetting> candidate, out string error)
	{
		if (hotkeys == null)
		{
			error = "快捷键窗口尚未就绪。";
			return false;
		}
		return hotkeys.TryApply(candidate, out error);
	}

	public bool HotkeyActive(int id)
	{
		if (hotkeys != null)
		{
			return hotkeys.Active.Any((HotkeySetting k) => k.ActionId == id);
		}
		return false;
	}

	public string RetryHotkeys()
	{
		if (hotkeys == null)
		{
			return "快捷键窗口尚未就绪。";
		}
		hotkeys.Release();
		List<string> list = hotkeys.Start(C.Prefs.Hotkeys);
		if (list.Count != 0)
		{
			return "以下组合被占用，请改键或使用开关：\n" + string.Join("\n", list);
		}
		return "已重新注册启用的快捷键。";
	}

	public List<HotkeySetting> SuspendHotkeys()
	{
		List<HotkeySetting> result = ((hotkeys == null) ? new List<HotkeySetting>() : hotkeys.Active.Select((HotkeySetting k) => k.Copy()).ToList());
		if (hotkeys != null)
		{
			hotkeys.Release();
		}
		return result;
	}

	public void ResumeHotkeys(List<HotkeySetting> keys)
	{
		if (hotkeys != null)
		{
			hotkeys.Release();
			List<string> list = hotkeys.Start(keys);
			if (list.Count > 0)
			{
				TrayWarning(string.Join("、", list));
			}
		}
	}

	public void ReleaseHotkey()
	{
		autoTimer.Stop();
		runningTimer.Stop();
		if (hotkeys != null)
		{
			hotkeys.Release();
		}
		SystemEvents.DisplaySettingsChanged -= DisplayChanged;
	}

	private IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
	{
		switch (msg)
		{
		case 32815:
			handled = true;
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				C.ShellAction(3);
			});
			return IntPtr.Zero;
		case 786:
		{
			int id = w.ToInt32();
			handled = true;
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				if (id == 1)
				{
					C.Toggle();
				}
				else if (id == 2)
				{
					C.ShellAction(1);
				}
				else if (id == 3)
				{
					C.ShellAction(2);
				}
				else if (id == 4)
				{
					C.AddFromAnywhere();
				}
				else if (id == 5)
				{
					C.Settings();
				}
				else if (id == 6)
				{
					C.ShellAction(3);
				}
				else if (id == 7)
				{
					C.ToggleTaskbarAutoHide();
				}
			});
			break;
		}
		}
		return IntPtr.Zero;
	}

	public void Refresh(bool capture = true)
	{
		DockOptions dock = C.Prefs.Dock;
		dock.Normalize();
		LightTheme = C.Prefs.Light;
		MotionEnabled = dock.MotionEnabled;
		MotionLevel = dock.MotionLevel;
		ConfigureGlass(dock);
		base.Topmost = dock.AlwaysOnTop || dock.AutoHide;
		BeginAnimation(Window.TopProperty, null);
		BeginAnimation(Window.LeftProperty, null);
		renderedHidden = false;
		hidePolicy = new AutoHidePolicy();
		ApplySurfaceStyle(dock);
		moveGrip.Visibility = ((C.Prefs.Placement.DockLocked || dock.Transparent) ? Visibility.Collapsed : Visibility.Visible);
		moveGrip.Width = (dock.Vertical ? 6.0 : 42.0);
		moveGrip.Height = (dock.Vertical ? 42.0 : 6.0);
		moveGrip.HorizontalAlignment = (dock.Vertical ? System.Windows.HorizontalAlignment.Left : System.Windows.HorizontalAlignment.Center);
		moveGrip.VerticalAlignment = (dock.Vertical ? VerticalAlignment.Center : VerticalAlignment.Top);
		moveGrip.Margin = (dock.Vertical ? new Thickness(7.0, 0.0, 0.0, 0.0) : new Thickness(0.0, 7.0, 0.0, 0.0));
		Items.Orientation = (dock.Vertical ? System.Windows.Controls.Orientation.Vertical : System.Windows.Controls.Orientation.Horizontal);
		Items.Children.Clear();
		if (dock.Vertical)
		{
			Items.Margin = new Thickness(0.0, dock.HoverPadding(), 0.0, dock.HoverPadding());
		}
		else
		{
			Items.Margin = new Thickness(dock.HoverPadding(), 0.0, dock.HoverPadding(), 0.0);
		}
		Surface.CornerRadius = new CornerRadius(dock.Radius);
		// 「展开应用面板」与「设置」使用真实 Windows 图标，取代原先手绘的 ▦ 和 ⚙。
		Items.Children.Add(DockButton(null, ShellIcons.Miya("board") ?? ShellIcons.BuiltIn("apps"), delegate
		{
			C.Toggle();
		}, "展开大号应用面板"));
		if (dock.ShowComputer)
		{
			Items.Children.Add(DockButton("computer", ShellIcons.BuiltIn("computer"), delegate
			{
				C.Board.Hide();
				Native.Computer();
			}, "此电脑"));
		}
		if (dock.ShowSeparator)
		{
			Items.Children.Add(new Border
			{
				Width = (dock.Vertical ? dock.IconSize * 0.7 : 1.0),
				Height = (dock.Vertical ? 1.0 : dock.IconSize * 0.7),
				Margin = new Thickness(8.0),
				Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(65, 220, 220, 230))
			});
		}
		Rect workArea = SystemParameters.WorkArea;
		List<Entry> list = C.Prefs.Apps.Where((Entry a) => a.Pinned).ToList();
		double extent = (dock.Vertical ? workArea.Height : workArea.Width) - 20.0;
		int num = dock.VisiblePinned(extent, list.Count);
		int num2 = ((list.Count > num) ? Math.Max(0, num - 1) : num);
		foreach (Entry a2 in list.Take(num2))
		{
			System.Windows.Controls.Button button = DockButton(null, FeatureUi.Icon(a2), delegate
			{
				C.Launch(a2);
			}, a2.Name, a2);
			button.ContextMenu = C.Menu(a2);
			Items.Children.Add(button);
		}
		if (list.Count > num2 && num > 0)
		{
			System.Windows.Controls.Button more = DockButton("···", null, delegate
			{
			}, "更多固定应用");
			System.Windows.Controls.ContextMenu menu = new System.Windows.Controls.ContextMenu();
			foreach (Entry item in list.Skip(num2))
			{
				System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
				{
					Header = item.Name
				};
				Entry entry = item;
				menuItem.Click += delegate
				{
					C.Launch(entry);
				};
				menu.Items.Add(menuItem);
			}
			more.ContextMenu = menu;
			more.Click += delegate
			{
				menu.PlacementTarget = more;
				menu.Placement = (dock.Vertical ? PlacementMode.Right : PlacementMode.Top);
				menu.IsOpen = true;
			};
			Items.Children.Add(more);
		}
		if (list.Count == 0)
		{
			Items.Children.Add(DockButton("+", null, delegate
			{
				C.Toggle(force: true);
			}, "打开面板，右键固定软件"));
		}
		Items.Children.Add(DockButton(null, ShellIcons.Miya("settings") ?? ShellIcons.BuiltIn("settings"), delegate
		{
			C.Settings();
		}, "DIY 设置"));
		int pinnedShown = ((list.Count == 0) ? 1 : num);
		if (dock.Vertical)
		{
			base.Width = dock.ThicknessFor();
			base.Height = Math.Min(workArea.Height - 20.0, dock.LengthFor(pinnedShown));
		}
		else
		{
			base.Width = Math.Min(workArea.Width - 20.0, dock.LengthFor(pinnedShown));
			base.Height = dock.ThicknessFor();
		}
		if (dock.Vertical)
		{
			double num5 = ((dock.Edge == 1) ? (workArea.Left + C.Prefs.DockGap) : (workArea.Right - base.Width - C.Prefs.DockGap));
			double num6 = workArea.Top + (workArea.Height - base.Height) / 2.0 + dock.Offset;
			base.Left = FeatureRules.Position(C.Prefs.Placement.DockLeft ?? num5, workArea.Left, workArea.Width, base.Width);
			base.Top = FeatureRules.Position(C.Prefs.Placement.DockTop ?? num6, workArea.Top, workArea.Height, base.Height);
		}
		else
		{
			double num7 = ((dock.Alignment == 0) ? (workArea.Left + 10.0) : ((dock.Alignment == 2) ? (workArea.Right - base.Width - 10.0) : (workArea.Left + (workArea.Width - base.Width) / 2.0)));
			base.Left = FeatureRules.Position(C.Prefs.Placement.DockLeft ?? (num7 + dock.Offset), workArea.Left, workArea.Width, base.Width);
			base.Top = FeatureRules.Position(C.Prefs.Placement.DockTop ?? (workArea.Bottom - base.Height - C.Prefs.DockGap), workArea.Top, workArea.Height, base.Height);
		}
		shownTop = base.Top;
		shownLeft = base.Left;
		if (dock.AutoHide)
		{
			autoTimer.Start();
		}
		else
		{
			autoTimer.Stop();
		}
		GlassPercent = dock.OpacityPercent;
		if (dock.Transparent)
		{
			ReleaseBackdrop();
		}
		else if (capture)
		{
			Backdrop(C.Prefs.Light);
		}
		else
		{
			SetGlassOpacity(GlassPercent);
			foreach (Border item2 in from b in Root.Children.OfType<Border>()
				where b.Tag as string == "backdrop"
				select b)
			{
				RectangleGeometry clip = (RectangleGeometry)(item2.Clip = new RectangleGeometry(new Rect(0.0, 0.0, Math.Max(1.0, base.Width - 28.0), Math.Max(1.0, base.Height - 28.0)), dock.Radius, dock.Radius));
				if (item2.Child is Border border)
				{
					border.Clip = clip;
					border.CornerRadius = Surface.CornerRadius;
					border.Effect = new BlurEffect
					{
						Radius = BlurRadius,
						RenderingBias = RenderingBias.Performance
					};
				}
			}
		}
		System.Windows.Media.Color color = Accent(dock.Accent);
		if (dock.Transparent)
		{
			Surface.BorderBrush = System.Windows.Media.Brushes.Transparent;
		}
		else
		{
			Surface.BorderBrush = new LinearGradientBrush(System.Windows.Media.Color.FromArgb((byte)Math.Min(255.0, 160.0 * EdgeStrength), color.R, color.G, color.B), System.Windows.Media.Color.FromArgb((byte)Math.Min(255.0, 30.0 * EdgeStrength), byte.MaxValue, byte.MaxValue, byte.MaxValue), 90.0);
		}
		UpdateRunning();
	}

	/// <summary>
	/// 底板样式。纯透明模式不绘制可见底板、描边与阴影，只有 Dock 本体条带可点。
	/// 关键点：底板必须保留 alpha=1 的极淡填充——分层窗口按像素 alpha 参与命中测试，
	/// 用全透明（alpha=0）会让整块 Dock 直接穿透到桌面，用户就没法按住它拖动了。
	/// </summary>
	private void ApplySurfaceStyle(DockOptions dock)
	{
		if (dock.Transparent)
		{
			Root.Margin = new Thickness(0.0);
			Surface.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
			Surface.BorderBrush = System.Windows.Media.Brushes.Transparent;
			Surface.BorderThickness = new Thickness(0.0);
			Surface.Effect = null;
			base.Background = null;
			ReleaseBackdrop();
		}
		else
		{
			Root.Margin = new Thickness(14.0);
			Surface.BorderThickness = new Thickness(1.0);
			Surface.Effect = new DropShadowEffect
			{
				BlurRadius = 25.0,
				ShadowDepth = 7.0,
				Opacity = 0.28,
				Color = Colors.Black
			};
			base.Background = System.Windows.Media.Brushes.Transparent;
			SetGlassOpacity(dock.OpacityPercent);
		}
	}

	private void UpdateAutoHide()
	{
		if (source == null || !base.IsVisible)
		{
			return;
		}
		DockOptions dock = C.Prefs.Dock;
		System.Drawing.Point position = System.Windows.Forms.Cursor.Position;
		System.Windows.Point point = source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(position.X, position.Y));
		Rect workArea = SystemParameters.WorkArea;
		bool over;
		bool edge;
		if (dock.Vertical)
		{
			over = !renderedHidden && point.Y >= base.Top && point.Y <= base.Top + base.Height && point.X >= shownLeft && point.X <= shownLeft + base.Width;
			edge = point.Y >= base.Top - 12.0 && point.Y <= base.Top + base.Height + 12.0 && ((dock.Edge == 1) ? (point.X <= workArea.Left + dock.EdgeRange) : (point.X >= workArea.Right - dock.EdgeRange));
		}
		else
		{
			over = !renderedHidden && point.X >= base.Left && point.X <= base.Left + base.Width && point.Y >= shownTop && point.Y <= shownTop + base.Height;
			edge = point.X >= base.Left - 12.0 && point.X <= base.Left + base.Width + 12.0 && point.Y >= workArea.Bottom - dock.EdgeRange;
		}
		bool flag = (base.ContextMenu != null && base.ContextMenu.IsOpen) || Items.Children.OfType<System.Windows.Controls.Button>().Any((System.Windows.Controls.Button b) => b.ContextMenu != null && b.ContextMenu.IsOpen);
		bool hide = hidePolicy.Update(DateTime.UtcNow, dock.AutoHide, over, edge, dragging || flag || C.UiBusy || C.Board.IsVisible, dock.HideDelay);
		if (hide == renderedHidden)
		{
			return;
		}
		renderedHidden = hide;
		DependencyProperty property = ((!dock.Vertical) ? Window.TopProperty : Window.LeftProperty);
		double target;
		if (!dock.Vertical)
		{
			target = (hide ? workArea.Bottom : shownTop);
		}
		else if (dock.Edge == 1)
		{
			target = (hide ? (workArea.Left - base.Width) : shownLeft);
		}
		else
		{
			target = (hide ? workArea.Right : shownLeft);
		}
		BeginAnimation(property, null);
		if (!MotionEnabled)
		{
			SetAxis(property, target);
			return;
		}
		DoubleAnimation doubleAnimation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(190.0))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			},
			FillBehavior = FillBehavior.Stop
		};
		doubleAnimation.Completed += delegate
		{
			if (renderedHidden == hide)
			{
				SetAxis(property, target);
			}
		};
		BeginAnimation(property, doubleAnimation);
	}

	private void SetAxis(DependencyProperty property, double value)
	{
		if (property == Window.TopProperty)
		{
			base.Top = value;
		}
		else
		{
			base.Left = value;
		}
	}

	private void UpdateRunning()
	{
		foreach (System.Windows.Controls.Button item in Items.Children.OfType<System.Windows.Controls.Button>())
		{
			Entry entry = item.Tag as Entry;
			Grid grid = item.Content as Grid;
			if (entry == null || grid == null)
			{
				continue;
			}
			foreach (Border item2 in from b in grid.Children.OfType<Border>()
				where b.Tag as string == "running"
				select b)
			{
				item2.Visibility = ((!C.Prefs.Dock.ShowRunning || !RunningWindows.IsRunning(entry)) ? Visibility.Collapsed : Visibility.Visible);
			}
		}
	}

	private static System.Windows.Media.Color Accent(int index)
	{
		return (new System.Windows.Media.Color[6]
		{
			System.Windows.Media.Color.FromRgb(121, 185, 250),
			System.Windows.Media.Color.FromRgb(183, 149, 245),
			System.Windows.Media.Color.FromRgb(105, 218, 183),
			System.Windows.Media.Color.FromRgb(242, 193, 104),
			System.Windows.Media.Color.FromRgb(244, 150, 178),
			System.Windows.Media.Color.FromRgb(194, 203, 217)
		})[index];
	}

	private System.Windows.Controls.Button DockButton(string text, ImageSource icon, Action action, string tip, Entry entry = null)
	{
		DockOptions o = C.Prefs.Dock;
		double iconSize = o.IconSize;
		System.Windows.Controls.Button button = Button("", delegate
		{
			action();
		}, iconSize + 16.0);
		button.Height = iconSize + 16.0 + (double)(o.ShowLabels ? 18 : 0);
		button.Margin = (o.Vertical ? new Thickness(0.0, o.Spacing / 2.0, 0.0, o.Spacing / 2.0) : new Thickness(o.Spacing / 2.0, 0.0, o.Spacing / 2.0, 0.0));
		button.ToolTip = tip;
		ToolTipService.SetInitialShowDelay(button, 200);
		ToolTipService.SetShowDuration(button, 3500);
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(Border));
		frameworkElementFactory.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
		FrameworkElementFactory frameworkElementFactory2 = new FrameworkElementFactory(typeof(ContentPresenter));
		frameworkElementFactory2.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
		frameworkElementFactory2.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		frameworkElementFactory.AppendChild(frameworkElementFactory2);
		button.Template = new ControlTemplate(typeof(System.Windows.Controls.Button))
		{
			VisualTree = frameworkElementFactory
		};
		FrameworkElement element;
		if (icon != null)
		{
			System.Windows.Controls.Image image = new System.Windows.Controls.Image
			{
				Source = icon,
				Width = iconSize,
				Height = iconSize,
				Stretch = Stretch.Uniform
			};
			// 高清图标按显示尺寸平滑缩放，避免二次模糊。
			RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
			element = image;
		}
		else
		{
			element = new TextBlock
			{
				Text = (text ?? "□"),
				FontSize = iconSize * 0.72,
				Width = iconSize,
				Height = iconSize,
				TextAlignment = TextAlignment.Center,
				Foreground = base.TextBrush,
				VerticalAlignment = VerticalAlignment.Center
			};
		}
		StackPanel stackPanel = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel.Children.Add(element);
		if (o.ShowLabels)
		{
			stackPanel.Children.Add(new TextBlock
			{
				Text = tip,
				FontSize = 10.0,
				Foreground = base.MutedBrush,
				TextAlignment = TextAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis,
				MaxWidth = iconSize + 12.0,
				Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
			});
		}
		Grid grid2 = new Grid();
		grid2.Children.Add(stackPanel);
		if (entry != null)
		{
			button.Tag = entry;
			grid2.Children.Add(new Border
			{
				Tag = "running",
				Width = 5.0,
				Height = 4.0,
				CornerRadius = new CornerRadius(2.0),
				Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(135, 205, byte.MaxValue)),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Bottom,
				Visibility = Visibility.Collapsed
			});
		}
		button.Content = grid2;
		ScaleTransform scale = new ScaleTransform(1.0, 1.0);
		TranslateTransform lift = new TranslateTransform();
		TransformGroup transformGroup = new TransformGroup();
		transformGroup.Children.Add(scale);
		transformGroup.Children.Add(lift);
		button.RenderTransform = transformGroup;
		button.RenderTransformOrigin = new System.Windows.Point(0.5, 0.65);
		Action<bool> animate = delegate(bool hover)
		{
			if (MotionEnabled)
			{
				CubicEase easingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				};
				double toValue = (hover ? Math.Min(1.6, 1.0 + (o.Zoom - 1.0) * MotionLevel) : 1.0);
				scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(toValue, TimeSpan.FromMilliseconds(180.0))
				{
					EasingFunction = easingFunction
				});
				scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(toValue, TimeSpan.FromMilliseconds(180.0))
				{
					EasingFunction = easingFunction
				});
				double toValue2 = (hover ? (-5.0 * MotionLevel) : 0.0);
				lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(toValue2, TimeSpan.FromMilliseconds(180.0))
				{
					EasingFunction = easingFunction
				});
			}
		};
		button.MouseEnter += delegate
		{
			animate(obj: true);
		};
		button.MouseLeave += delegate
		{
			animate(obj: false);
		};
		return button;
	}
}
