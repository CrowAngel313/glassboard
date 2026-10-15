using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GlassBoard;

public class BoardWindow : GlassWindow
{
	private Border dragPreview;

	private Image dragPreviewIcon;

	private TextBlock dragPreviewText;

	private bool activeDrag;

	private string hoverGroup;

	private string folderSignature = "";

	private readonly DispatcherTimer hoverTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(700.0)
	};

	public bool ModalBusy;

	private Controller C;

	private TextBox Search;

	private UniformGrid Tiles;

	private TextBlock Pager;

	private TextBlock Status;

	private TextBlock Empty;

	private Grid layout;

	private int page;

	private int capacity = 35;

	private int columns = 7;

	private Point dragStart;

	private Entry dragEntry;

	private bool editing;

	private readonly HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private WrapPanel bulkBar;

	private RowDefinition bulkRow;

	private TextBlock selectionCount;

	private Button selectAll;

	private Button clearSelection;

	private Button deleteSelected;

	private Button undoRemoval;

	private Button editSelection;

	private Button moveSelected;

	private ContextMenu iconMenu;

	private ComboBox groups;

	private ComboBox moveGroup;

	private string currentGroup = "全部";

	private bool syncingGroups;

	private bool iconSizeSync;

	private Slider boardIconSlider;

	private TextBlock iconSizeText;

	private Button addExisting;

	private Thumb moveGrip;

	private Thumb resizeGrip;

	private int visibilitySerial;

	private WrapPanel folderBar;

	private bool foldersVisible = true;

	private readonly DispatcherTimer idleRelease = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(2.0)
	};

	private bool preparingView;

	private string pendingStatus;

	public string CurrentGroup => currentGroup;

	internal bool HasView => layout != null;

	private void InitDragExperience()
	{
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		dragPreviewIcon = new Image
		{
			Width = 28.0,
			Height = 28.0,
			Stretch = Stretch.Uniform,
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
		};
		dragPreviewText = new TextBlock
		{
			Foreground = Brushes.White,
			VerticalAlignment = VerticalAlignment.Center,
			MaxWidth = 230.0,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		stackPanel.Children.Add(dragPreviewIcon);
		stackPanel.Children.Add(dragPreviewText);
		dragPreview = new Border
		{
			Child = stackPanel,
			Padding = new Thickness(12.0, 9.0, 12.0, 9.0),
			CornerRadius = new CornerRadius(14.0),
			Background = Polish.Color("#E52A3447"),
			BorderBrush = Polish.Color("#80B8CEEF"),
			BorderThickness = new Thickness(1.0),
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
			IsHitTestVisible = false,
			Visibility = Visibility.Collapsed
		};
		Root.Children.Add(dragPreview);
		hoverTimer.Tick += delegate
		{
			hoverTimer.Stop();
			if (activeDrag && hoverGroup != null && hoverGroup != currentGroup)
			{
				OpenGroup(hoverGroup);
			}
		};
		base.PreviewDragOver += delegate(object s, DragEventArgs e)
		{
			string[] array = DraggedEntries(e.Data);
			bool dataPresent = e.Data.GetDataPresent(DataFormats.FileDrop);
			if (array.Length != 0 || dataPresent)
			{
				activeDrag = true;
				Point position2 = e.GetPosition(Root);
				if (dragPreviewText.Text.Length == 0)
				{
					if (dataPresent)
					{
						string[] array2 = (string[])e.Data.GetData(DataFormats.FileDrop);
						dragPreviewText.Text = ((array2.Length == 1) ? Path.GetFileName(array2[0]) : ("添加 " + array2.Length + " 个文件"));
					}
					else
					{
						dragPreviewText.Text = "正在移动图标";
					}
				}
				dragPreview.RenderTransform = new TranslateTransform(position2.X + 16.0, position2.Y + 16.0);
				dragPreview.Visibility = Visibility.Visible;
			}
		};
		base.PreviewDragLeave += delegate(object s, DragEventArgs e)
		{
			Point position = e.GetPosition(Root);
			if (position.X < 0.0 || position.Y < 0.0 || position.X > base.ActualWidth || position.Y > base.ActualHeight)
			{
				EndDragPreview();
				Render();
			}
		};
		base.PreviewDrop += delegate
		{
			EndDragPreview();
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				if (!activeDrag && base.IsVisible)
				{
					Render();
				}
			});
		};
		base.Closed += delegate
		{
			hoverTimer.Stop();
		};
	}

	private void BeginGroupHover(string name)
	{
		if (!(hoverGroup == name))
		{
			hoverGroup = name;
			hoverTimer.Stop();
			hoverTimer.Start();
		}
	}

	private void StopGroupHover(string name)
	{
		if (name == null || hoverGroup == name)
		{
			hoverGroup = null;
			hoverTimer.Stop();
		}
	}

	private void EndDragPreview()
	{
		activeDrag = false;
		hoverTimer.Stop();
		hoverGroup = null;
		dragPreview.Visibility = Visibility.Collapsed;
		dragPreviewText.Text = "";
		dragPreviewIcon.Source = null;
		folderSignature = "";
	}

	private void StartEntryDrag(FrameworkElement tile, Entry entry)
	{
		string[] array = ((editing && selected.Contains(entry.Path)) ? selected.ToArray() : new string[1] { entry.Path });
		DataObject dataObject = new DataObject();
		dataObject.SetData("GlassBoard.Entry", entry.Path);
		dataObject.SetData("GlassBoard.Entries", array);
		dataObject.SetData("GlassBoard.SourceGroup", currentGroup);
		dragPreviewText.Text = ((array.Length == 1) ? entry.Name : (entry.Name + " 等 " + array.Length + " 个"));
		try
		{
			dragPreviewIcon.Source = Native.Icon(entry.IconPath ?? entry.Path);
		}
		catch
		{
			dragPreviewIcon.Source = null;
		}
		bool modalBusy = ModalBusy;
		ModalBusy = true;
		activeDrag = true;
		try
		{
			DragDrop.DoDragDrop(tile, dataObject, DragDropEffects.Copy | DragDropEffects.Move);
		}
		finally
		{
			EndDragPreview();
			ModalBusy = modalBusy;
			Render();
		}
	}

	private DragDropEffects GroupDragEffect(IDataObject data, string name)
	{
		if (name == "全部")
		{
			return DragDropEffects.None;
		}
		if (DraggedEntries(data).Length != 0)
		{
			if (!data.GetDataPresent("GlassBoard.SourceGroup") || !((string)data.GetData("GlassBoard.SourceGroup") == "全部"))
			{
				return DragDropEffects.Move;
			}
			return DragDropEffects.Copy;
		}
		if (!data.GetDataPresent(DataFormats.FileDrop))
		{
			return DragDropEffects.None;
		}
		return DragDropEffects.Copy;
	}

	private void MoreMenu()
	{
		ContextMenu menu = new ContextMenu
		{
			Style = (base.Resources[typeof(ContextMenu)] as Style)
		};
		iconMenu = menu;
		menu.Closed += delegate
		{
			if (iconMenu == menu)
			{
				iconMenu = null;
			}
		};
		Action<string, Action, bool> obj = delegate(string label, Action action, bool enabled)
		{
			MenuItem menuItem = new MenuItem
			{
				Header = label,
				IsEnabled = enabled
			};
			menuItem.Click += delegate
			{
				bool modalBusy = ModalBusy;
				ModalBusy = true;
				try
				{
					menu.IsOpen = false;
					action();
				}
				finally
				{
					ModalBusy = modalBusy;
				}
			};
			menu.Items.Add(menuItem);
		};
		obj("撤销 · Ctrl+Z", delegate
		{
			C.UndoBoard();
		}, C.CanUndoRemoval);
		obj("重做 · Ctrl+Y", delegate
		{
			C.RedoBoard();
		}, C.CanRedoBoard);
		menu.Items.Add(new Separator());
		obj("图标间距与默认大小", delegate
		{
			IconSettings();
		}, arg3: true);
		obj(foldersVisible ? "收起分组文件夹" : "展开分组文件夹", delegate
		{
			foldersVisible = !foldersVisible;
			folderBar.Visibility = ((!foldersVisible) ? Visibility.Collapsed : Visibility.Visible);
		}, arg3: true);
		obj("自动分组", delegate
		{
			C.AutoGroupApps();
		}, arg3: true);
		obj("清理非应用入口（书签/批处理/重复）", delegate
		{
			C.CleanupNonApps(this);
		}, arg3: true);
		obj("管理分组", delegate
		{
			C.ManageGroups();
		}, arg3: true);
		obj("工作区", delegate
		{
			C.ManageWorkspaces();
		}, arg3: true);
		obj("外观方案", delegate
		{
			C.ManageSchemes();
		}, arg3: true);
		menu.Items.Add(new Separator());
		obj("导出完整备份", delegate
		{
			C.ExportFullBackup();
		}, arg3: true);
		obj("恢复完整备份", delegate
		{
			C.RestoreFullBackup();
		}, arg3: true);
		obj("赞赏 Miya", delegate
		{
			C.Settings(5);
		}, arg3: true);
		menu.PlacementTarget = this;
		menu.IsOpen = true;
	}

	public void ClearSearch()
	{
		if (Search != null)
		{
			Search.Clear();
		}
	}

	public BoardWindow(Controller c)
		: base(42.0)
	{
		C = c;
		base.Title = "GlassBoard 应用启动台";
		base.AllowDrop = true;
		InitDragExperience();
		base.IsVisibleChanged += delegate
		{
			if (base.IsVisible)
			{
				idleRelease.Stop();
			}
			else if (layout != null)
			{
				idleRelease.Start();
			}
		};
		idleRelease.Tick += delegate
		{
			if (base.IsVisible)
			{
				idleRelease.Stop();
			}
			else if (ReleaseHiddenView())
			{
				idleRelease.Stop();
			}
		};
		base.Closed += delegate
		{
			idleRelease.Stop();
		};
		base.PreviewKeyDown += delegate(object s, KeyEventArgs e)
		{
			if (Search != null)
			{
				if (e.Key == Key.Escape)
				{
					CollapseSmooth();
					e.Handled = true;
				}
				else if (!Search.IsKeyboardFocusWithin && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && (e.Key == Key.Z || e.Key == Key.Y))
				{
					if (e.Key == Key.Y || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
					{
						C.RedoBoard();
					}
					else
					{
						C.UndoBoard();
					}
					e.Handled = true;
				}
				else if (editing && !Search.IsKeyboardFocusWithin && e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
				{
					SelectFiltered();
					e.Handled = true;
				}
				else if (editing && !Search.IsKeyboardFocusWithin && e.Key == Key.Delete)
				{
					C.RemoveMany(selected.ToArray());
					e.Handled = true;
				}
				else if (e.Key == Key.Next)
				{
					ChangePage(1);
					e.Handled = true;
				}
				else if (e.Key == Key.Prior)
				{
					ChangePage(-1);
					e.Handled = true;
				}
				else if (e.Key == Key.Return && Search.IsKeyboardFocusWithin && !editing)
				{
					Entry entry = Filtered().FirstOrDefault();
					if (entry != null)
					{
						C.Launch(entry);
					}
					e.Handled = true;
				}
			}
		};
		base.Drop += ExternalDrop;
		base.Deactivated += delegate
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				if (base.IsVisible && !base.IsActive && !ModalBusy && !IsContextMenuOpen() && Mouse.LeftButton != MouseButtonState.Pressed)
				{
					CollapseSmooth();
				}
			});
		};
		moveGrip = new Thumb
		{
			Width = 60.0,
			Height = 8.0,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
			Cursor = Cursors.SizeAll,
			ToolTip = "拖动面板位置"
		};
		Root.Children.Add(moveGrip);
		resizeGrip = new Thumb
		{
			Width = 20.0,
			Height = 20.0,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Bottom,
			Margin = new Thickness(0.0, 0.0, 5.0, 5.0),
			Cursor = Cursors.SizeNWSE,
			ToolTip = "拖动调整面板大小"
		};
		Root.Children.Add(resizeGrip);
		moveGrip.Template = GlassWindow.GripTemplate();
		resizeGrip.Template = GlassWindow.GripTemplate(corner: true);
		base.Closing += delegate(object s, CancelEventArgs e)
		{
			if (!C.ShuttingDown)
			{
				e.Cancel = true;
				CollapseSmooth();
			}
		};
		moveGrip.DragStarted += delegate
		{
			ModalBusy = true;
		};
		moveGrip.DragDelta += delegate(object s, DragDeltaEventArgs e)
		{
			if (!C.Prefs.Placement.PanelLocked)
			{
				Rect workArea2 = SystemParameters.WorkArea;
				base.Left = FeatureRules.Position(base.Left + e.HorizontalChange, workArea2.Left, workArea2.Width, base.Width);
				base.Top = FeatureRules.Position(base.Top + e.VerticalChange, workArea2.Top, workArea2.Height, base.Height);
			}
		};
		resizeGrip.DragStarted += delegate
		{
			ModalBusy = true;
		};
		resizeGrip.DragDelta += delegate(object s, DragDeltaEventArgs e)
		{
			if (!C.Prefs.Placement.PanelLocked)
			{
				Rect workArea = SystemParameters.WorkArea;
				base.Width = Math.Max(Math.Min(860.0, workArea.Width - 32.0), Math.Min(workArea.Width - 32.0, base.Width + e.HorizontalChange));
				base.Height = Math.Max(Math.Min(560.0, workArea.Height - 32.0), Math.Min(workArea.Height - 32.0, base.Height + e.VerticalChange));
				base.Left = FeatureRules.Position(base.Left, workArea.Left, workArea.Width, base.Width);
				base.Top = FeatureRules.Position(base.Top, workArea.Top, workArea.Height, base.Height);
			}
		};
		moveGrip.DragCompleted += delegate
		{
			SavePlacement();
			ModalBusy = false;
			Backdrop(C.Prefs.Light);
		};
		resizeGrip.DragCompleted += delegate
		{
			SavePlacement();
			ModalBusy = false;
			BuildForSize();
			Backdrop(C.Prefs.Light);
		};
	}

	private void SavePlacement()
	{
		PlacementOptions placement = C.Prefs.Placement;
		placement.PanelLeft = base.Left;
		placement.PanelTop = base.Top;
		placement.PanelWidth = base.Width;
		placement.PanelHeight = base.Height;
		C.Save();
	}

	private void BuildForSize()
	{
		string text = ((Search == null) ? "" : Search.Text);
		columns = BoardIconRules.Columns(base.Width - 104.0, C.Prefs.BoardIconSize, C.Prefs.BoardIconGap);
		int num = BoardIconRules.Rows(base.Height - 258.0, C.Prefs.BoardIconSize, C.Prefs.BoardIconGap);
		capacity = columns * num;
		Build(num);
		Search.Text = text;
	}

	public void RefreshIcons()
	{
		if (boardIconSlider != null)
		{
			iconSizeSync = true;
			boardIconSlider.Value = C.Prefs.BoardIconSize;
			iconSizeText.Text = "图标 " + Math.Round(C.Prefs.BoardIconSize) + " px";
			iconSizeSync = false;
		}
		if (Tiles != null)
		{
			int num = BoardIconRules.Columns((Tiles.ActualWidth > 0.0) ? Tiles.ActualWidth : (base.Width - 104.0), C.Prefs.BoardIconSize, C.Prefs.BoardIconGap);
			int num2 = BoardIconRules.Rows((Tiles.ActualHeight > 0.0) ? Tiles.ActualHeight : (base.Height - 258.0), C.Prefs.BoardIconSize, C.Prefs.BoardIconGap);
			columns = num;
			capacity = num * num2;
			Tiles.Columns = num;
			Tiles.Rows = num2;
			Render();
		}
	}

	private void IconSettings()
	{
		ContextMenu menu = new ContextMenu
		{
			Style = (base.Resources[typeof(ContextMenu)] as Style)
		};
		iconMenu = menu;
		menu.Closed += delegate
		{
			if (iconMenu == menu)
			{
				iconMenu = null;
			}
		};
		Action<string, double, double, double, Action<double>> obj = delegate(string name, double min, double max, double value, Action<double> change)
		{
			StackPanel stackPanel = new StackPanel
			{
				Width = 230.0
			};
			TextBlock label = new TextBlock
			{
				Text = name + "：" + Math.Round(value) + " px",
				Foreground = base.TextBrush,
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			};
			stackPanel.Children.Add(label);
			Slider slider = new Slider
			{
				Minimum = min,
				Maximum = max,
				Value = value,
				TickFrequency = 1.0,
				IsSnapToTickEnabled = true,
				Style = (base.Resources[typeof(Slider)] as Style)
			};
			stackPanel.Children.Add(slider);
			slider.ValueChanged += delegate
			{
				label.Text = name + "：" + Math.Round(slider.Value) + " px";
				change(slider.Value);
				RefreshIcons();
				C.Save();
			};
			menu.Items.Add(new MenuItem
			{
				Header = stackPanel,
				StaysOpenOnClick = true
			});
		};
		obj("图标大小", 28.0, 80.0, C.Prefs.BoardIconSize, delegate(double v)
		{
			C.Prefs.BoardIconSize = v;
		});
		obj("图标间距", 0.0, 20.0, C.Prefs.BoardIconGap, delegate(double v)
		{
			C.Prefs.BoardIconGap = v;
		});
		MenuItem menuItem = new MenuItem
		{
			Header = "恢复默认"
		};
		menuItem.Click += delegate
		{
			C.Prefs.BoardIconSize = 48.0;
			C.Prefs.BoardIconGap = 5.0;
			RefreshIcons();
			C.Save();
		};
		menu.Items.Add(menuItem);
		menu.PlacementTarget = this;
		menu.IsOpen = true;
	}

	public void CollapseSmooth()
	{
		if (!base.IsVisible)
		{
			return;
		}
		int serial = ++visibilitySerial;
		if (!MotionEnabled)
		{
			Hide();
			return;
		}
		DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(110.0));
		doubleAnimation.Completed += delegate
		{
			if (serial == visibilitySerial)
			{
				Hide();
			}
		};
		BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
	}

	internal bool ReleaseHiddenView()
	{
		if (base.IsVisible || preparingView || ModalBusy || activeDrag || base.IsMouseCaptureWithin)
		{
			return false;
		}
		if (iconMenu != null)
		{
			iconMenu.IsOpen = false;
		}
		iconMenu = null;
		EndDragPreview();
		ContentGrid.Children.Clear();
		ReleaseBackdrop();
		Search = null;
		Tiles = null;
		Pager = (Status = (Empty = null));
		layout = null;
		bulkBar = (folderBar = null);
		bulkRow = null;
		selectionCount = (iconSizeText = null);
		selectAll = (clearSelection = (deleteSelected = (undoRemoval = (editSelection = (moveSelected = (addExisting = null))))));
		groups = (moveGroup = null);
		boardIconSlider = null;
		dragEntry = null;
		return true;
	}

	private bool IsContextMenuOpen()
	{
		// 分组文件夹条上的右键菜单也要算进来：它属于本窗口，
		// 若不检查，右键分组时窗口会因为「判定为没有菜单打开」而自动收起，菜单根本点不到。
		if (folderBar != null && folderBar.Children.OfType<FrameworkElement>().Any((FrameworkElement x) => x.ContextMenu != null && x.ContextMenu.IsOpen))
		{
			return true;
		}
		if (iconMenu == null || !iconMenu.IsOpen)
		{
			if (Tiles != null)
			{
				return Tiles.Children.OfType<FrameworkElement>().Any((FrameworkElement x) => x.ContextMenu != null && x.ContextMenu.IsOpen);
			}
			return false;
		}
		return true;
	}

	public void Open()
	{
		idleRelease.Stop();
		preparingView = true;
		try
		{
			visibilitySerial++;
			Rect workArea = SystemParameters.WorkArea;
			double defaultValue = ((C.Prefs.PanelSize == 0) ? 980 : 1280);
			double defaultValue2 = ((C.Prefs.PanelSize == 0) ? 620 : 730);
			if (C.Prefs.PanelSize == 2)
			{
				defaultValue = workArea.Width - 32.0;
				defaultValue2 = workArea.Height - C.Dock.Height - 40.0;
			}
			PlacementOptions placement = C.Prefs.Placement;
			base.Width = Math.Min(placement.PanelWidth.GetValueOrDefault(defaultValue), workArea.Width - 32.0);
			base.Height = Math.Min(placement.PanelHeight.GetValueOrDefault(defaultValue2), workArea.Height - 32.0);
			base.Height = Math.Max(Math.Min(400.0, workArea.Height - 130.0), base.Height);
			base.Width = Math.Max(Math.Min(860.0, workArea.Width - 32.0), base.Width);
			base.Height = Math.Max(Math.Min(560.0, workArea.Height - 32.0), base.Height);
			base.Left = FeatureRules.Position(placement.PanelLeft ?? (workArea.Left + (workArea.Width - base.Width) / 2.0), workArea.Left, workArea.Width, base.Width);
			base.Top = FeatureRules.Position(placement.PanelTop ?? (workArea.Top + (workArea.Height - C.Dock.Height - base.Height) / 2.0), workArea.Top, workArea.Height, base.Height);
			Thumb thumb = moveGrip;
			Visibility visibility2 = (resizeGrip.Visibility = (placement.PanelLocked ? Visibility.Collapsed : Visibility.Visible));
			thumb.Visibility = visibility2;
			editing = false;
			selected.Clear();
			LightTheme = C.Prefs.Light;
			MotionEnabled = C.Prefs.Dock.MotionEnabled;
			MotionLevel = C.Prefs.Dock.MotionLevel;
			ConfigureGlass(C.Prefs.Dock);
			GlassPercent = C.Prefs.OpacityPercent.GetValueOrDefault(72.0);
			Polish.Apply(this, LightTheme);
			BuildForSize();
			Backdrop(C.Prefs.Light);
			Show();
			Activate();
			Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);
			AnimateOpen();
			Search.Focus();
		}
		finally
		{
			preparingView = false;
		}
	}

	private void Build(int rows)
	{
		ContentGrid.Children.Clear();
		layout = new Grid
		{
			Margin = new Thickness(38.0, 22.0, 38.0, 20.0)
		};
		layout.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(55.0)
		});
		layout.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		bulkRow = new RowDefinition
		{
			Height = new GridLength(0.0)
		};
		layout.RowDefinitions.Add(bulkRow);
		layout.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(1.0, GridUnitType.Star)
		});
		layout.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(45.0)
		});
		layout.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(32.0)
		});
		ContentGrid.Children.Add(layout);
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(116.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(80.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(40.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(44.0)
		});
		layout.Children.Add(grid);
		Border border = new Border
		{
			CornerRadius = new CornerRadius(18.0),
			BorderThickness = new Thickness(1.0),
			BorderBrush = new SolidColorBrush(Color.FromArgb(90, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			Background = new SolidColorBrush(Color.FromArgb(25, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			Margin = new Thickness(10.0, 0.0, 18.0, 9.0)
		};
		Grid grid2 = (Grid)(border.Child = new Grid());
		TextBlock hint = new TextBlock
		{
			Text = "搜索应用、文件夹…",
			Foreground = base.MutedBrush,
			Margin = new Thickness(18.0, 0.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center,
			IsHitTestVisible = false
		};
		grid2.Children.Add(hint);
		Search = new TextBox
		{
			FontSize = 15.0,
			Foreground = base.TextBrush,
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			CaretBrush = base.TextBrush,
			VerticalContentAlignment = VerticalAlignment.Center,
			Padding = new Thickness(18.0, 0.0, 18.0, 0.0)
		};
		Search.TextChanged += delegate
		{
			hint.Visibility = ((Search.Text.Length != 0) ? Visibility.Hidden : Visibility.Visible);
			page = 0;
			Render(animate: true);
		};
		grid2.Children.Add(Search);
		grid.Children.Add(border);
		Button element = Button("+ 批量添加", delegate
		{
			C.Choose();
		}, 106.0);
		Grid.SetColumn(element, 1);
		grid.Children.Add(element);
		editSelection = Button(editing ? "完成" : "多选", null, 68.0);
		editSelection.ToolTip = "跨页勾选，一次移除多个入口";
		editSelection.Click += delegate
		{
			SetSelectionMode(!editing);
			Search.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
			Render();
		};
		Grid.SetColumn(editSelection, 2);
		grid.Children.Add(editSelection);
		Button button = Button("⚙", delegate
		{
			C.Settings();
		}, 30.0);
		button.Content = Polish.Icon("settings", base.TextBrush, 18.0);
		button.ToolTip = "外观、组件与快捷键";
		Grid.SetColumn(button, 3);
		grid.Children.Add(button);
		Button element2 = Button("×", delegate
		{
			CollapseSmooth();
		}, 32.0);
		Grid.SetColumn(element2, 4);
		grid.Children.Add(element2);
		WrapPanel wrapPanel = new WrapPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetRow(wrapPanel, 1);
		layout.Children.Add(wrapPanel);
		groups = new ComboBox
		{
			Width = 145.0,
			Height = 30.0,
			Margin = new Thickness(8.0, 0.0, 10.0, 0.0)
		};
		wrapPanel.Children.Add(groups);
		groups.SelectionChanged += delegate
		{
			if (!syncingGroups)
			{
				string text = (groups.SelectedItem as string) ?? "全部";
				if (text != currentGroup)
				{
					selected.Clear();
				}
				currentGroup = text;
				if (addExisting != null)
				{
					addExisting.IsEnabled = currentGroup != "全部";
				}
				page = 0;
				Render(animate: true);
			}
		};
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(6.0, 0.0, 8.0, 0.0)
		};
		iconSizeText = new TextBlock
		{
			Text = "图标 " + Math.Round(C.Prefs.BoardIconSize) + " px",
			Foreground = base.TextBrush,
			VerticalAlignment = VerticalAlignment.Center,
			Width = 75.0
		};
		stackPanel.Children.Add(iconSizeText);
		boardIconSlider = new Slider
		{
			Minimum = 28.0,
			Maximum = 80.0,
			Value = C.Prefs.BoardIconSize,
			Width = 110.0,
			TickFrequency = 2.0,
			IsSnapToTickEnabled = true,
			ToolTip = "缩小图标，让每页容纳更多入口"
		};
		boardIconSlider.ValueChanged += delegate
		{
			if (!iconSizeSync)
			{
				C.Prefs.BoardIconSize = boardIconSlider.Value;
				iconSizeText.Text = "图标 " + Math.Round(boardIconSlider.Value) + " px";
				RefreshIcons();
				C.Save();
			}
		};
		stackPanel.Children.Add(boardIconSlider);
		wrapPanel.Children.Add(stackPanel);
		wrapPanel.Children.Add(Button("新建分组", delegate
		{
			C.CreateGroupAndOpen();
		}, 92.0));
		addExisting = Button("添加已有图标", delegate
		{
			C.AddExistingToGroup(currentGroup);
		}, 112.0);
		wrapPanel.Children.Add(addExisting);
		wrapPanel.Children.Add(Button("添加文件夹", delegate
		{
			C.AddFolder();
		}, 100.0));
		wrapPanel.Children.Add(Button("更多", delegate
		{
			MoreMenu();
		}, 68.0));
		bulkBar = new WrapPanel
		{
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		Grid.SetRow(bulkBar, 2);
		layout.Children.Add(bulkBar);
		selectAll = Button("全选结果", delegate
		{
			SelectFiltered();
		}, 90.0);
		selectAll.ToolTip = "选中当前搜索结果的所有页；已有勾选会保留";
		bulkBar.Children.Add(selectAll);
		clearSelection = Button("取消全选", delegate
		{
			selected.Clear();
			Render();
		}, 90.0);
		bulkBar.Children.Add(clearSelection);
		deleteSelected = Button("移除已选", delegate
		{
			C.RemoveMany(selected.ToArray());
		}, 112.0);
		deleteSelected.Foreground = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 155, 155));
		bulkBar.Children.Add(deleteSelected);
		undoRemoval = Button("撤销", delegate
		{
			C.UndoBoard();
		}, 72.0);
		wrapPanel.Children.Add(undoRemoval);
		selectionCount = new TextBlock
		{
			Foreground = base.MutedBrush,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(10.0, 0.0, 0.0, 0.0)
		};
		bulkBar.Children.Add(selectionCount);
		moveGroup = new ComboBox
		{
			Width = 112.0,
			Height = 30.0,
			Margin = new Thickness(12.0, 0.0, 4.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		bulkBar.Children.Add(moveGroup);
		moveSelected = Button("移动", delegate
		{
			if (moveGroup.SelectedItem is string to && selected.Count != 0)
			{
				C.TransferEntries(selected.ToArray(), currentGroup, to);
			}
		}, 100.0);
		bulkBar.Children.Add(moveSelected);
		SyncGroups();
		DockPanel dockPanel = new DockPanel
		{
			Margin = new Thickness(0.0, 10.0, 0.0, 4.0)
		};
		Grid.SetRow(dockPanel, 3);
		layout.Children.Add(dockPanel);
		folderSignature = "";
		folderBar = new WrapPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
			Visibility = ((!foldersVisible) ? Visibility.Collapsed : Visibility.Visible)
		};
		DockPanel.SetDock(folderBar, Dock.Top);
		dockPanel.Children.Add(folderBar);
		Grid grid3 = new Grid();
		dockPanel.Children.Add(grid3);
		Tiles = new UniformGrid
		{
			Columns = columns,
			Rows = rows
		};
		grid3.Children.Add(Tiles);
		Tiles.SizeChanged += delegate
		{
			int num = BoardIconRules.Columns(Tiles.ActualWidth, C.Prefs.BoardIconSize, C.Prefs.BoardIconGap);
			int num2 = BoardIconRules.Rows(Tiles.ActualHeight, C.Prefs.BoardIconSize, C.Prefs.BoardIconGap);
			if (num != columns || num * num2 != capacity)
			{
				RefreshIcons();
			}
		};
		Empty = new TextBlock
		{
			Text = "将软件、快捷方式或文件夹拖到这里\n也可以点击「添加文件夹」或「批量添加」",
			TextAlignment = TextAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Center,
			Foreground = base.MutedBrush,
			FontSize = 19.0,
			LineHeight = 32.0
		};
		grid3.Children.Add(Empty);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		Grid.SetRow(stackPanel2, 4);
		layout.Children.Add(stackPanel2);
		stackPanel2.Children.Add(Button("‹", delegate
		{
			ChangePage(-1);
		}, 40.0));
		Pager = new TextBlock
		{
			Width = 100.0,
			TextAlignment = TextAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = base.TextBrush,
			FontSize = 14.0
		};
		stackPanel2.Children.Add(Pager);
		stackPanel2.Children.Add(Button("›", delegate
		{
			ChangePage(1);
		}, 40.0));
		Grid grid4 = new Grid();
		grid4.ColumnDefinitions.Add(new ColumnDefinition());
		grid4.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(230.0)
		});
		Grid.SetRow(grid4, 5);
		layout.Children.Add(grid4);
		Status = new TextBlock
		{
			Text = "批量添加 · 多选移除 · 右键固定到 Dock",
			Foreground = base.MutedBrush,
			FontSize = 11.0,
			TextTrimming = TextTrimming.CharacterEllipsis,
			VerticalAlignment = VerticalAlignment.Center
		};
		StackPanel stackPanel3 = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel3.Children.Add(Status);
		stackPanel3.Children.Add(new TextBlock
		{
			Text = "由 Miya 制作",
			Foreground = base.MutedBrush,
			FontSize = 10.0,
			Opacity = 0.7,
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0),
			IsHitTestVisible = false
		});
		grid4.Children.Add(stackPanel3);
		StackPanel stackPanel4 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		Grid.SetColumn(stackPanel4, 1);
		grid4.Children.Add(stackPanel4);
		TextBlock amount = new TextBlock
		{
			Text = "透明度 " + Math.Round(100.0 - GlassPercent) + "%",
			Foreground = base.MutedBrush,
			Width = 83.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel4.Children.Add(amount);
		Slider transparency = new Slider
		{
			Minimum = 5.0,
			Maximum = 90.0,
			Value = 100.0 - GlassPercent,
			Width = 140.0,
			TickFrequency = 5.0,
			IsSnapToTickEnabled = true,
			ToolTip = "只改变玻璃背景，图标和文字保持清晰"
		};
		stackPanel4.Children.Add(transparency);
		transparency.ValueChanged += delegate
		{
			C.Opacity(100.0 - transparency.Value);
			amount.Text = "透明度 " + Math.Round(transparency.Value) + "%";
			C.Save();
		};
		Render(animate: true);
	}

	public void SyncGroups()
	{
		if (addExisting != null)
		{
			addExisting.IsEnabled = currentGroup != "全部";
		}
		if (groups != null)
		{
			syncingGroups = true;
			List<string> list = FeatureRules.Filters(C.Prefs);
			groups.ItemsSource = list;
			if (!list.Contains(currentGroup))
			{
				currentGroup = "全部";
				selected.Clear();
				page = 0;
			}
			groups.SelectedItem = currentGroup;
			if (moveGroup != null)
			{
				string text = moveGroup.SelectedItem as string;
				moveGroup.ItemsSource = FeatureRules.MoveTargets(C.Prefs);
				moveGroup.SelectedItem = (list.Contains(text) ? text : "未分组");
			}
			syncingGroups = false;
			if (addExisting != null)
			{
				addExisting.IsEnabled = currentGroup != "全部";
			}
		}
	}

	private List<Entry> Filtered()
	{
		string q = ((Search == null) ? "" : Search.Text.Trim());
		return C.Prefs.Apps.Where((Entry a) => (currentGroup == "全部" || FeatureRules.InGroup(a, currentGroup)) && LayoutRules.Matches(a.Name, q)).ToList();
	}

	public void SetStatus(string text)
	{
		pendingStatus = text;
		if (Status != null)
		{
			Status.Text = text;
		}
	}

	private void SetSelectionMode(bool enabled)
	{
		editing = enabled;
		selected.Clear();
		dragEntry = null;
		if (editSelection != null)
		{
			editSelection.Content = (enabled ? "完成" : "多选");
		}
	}

	public void FinishSelection()
	{
		SetSelectionMode(enabled: false);
	}

	private void SelectFiltered()
	{
		foreach (Entry item in Filtered())
		{
			selected.Add(item.Path);
		}
		Render();
	}

	private void UpdateSelectionBar()
	{
		selected.RemoveWhere((string p) => !C.Prefs.Apps.Any((Entry a) => string.Equals(a.Path, p, StringComparison.OrdinalIgnoreCase)));
		bool flag = editing;
		bulkBar.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		bulkRow.Height = (flag ? GridLength.Auto : new GridLength(0.0));
		moveGroup.IsEnabled = editing;
		moveSelected.Content = ((currentGroup == "全部") ? "添加到分组" : "移动");
		moveSelected.IsEnabled = editing && selected.Count > 0;
		selectAll.IsEnabled = editing;
		clearSelection.IsEnabled = editing && selected.Count > 0;
		deleteSelected.IsEnabled = editing && selected.Count > 0;
		deleteSelected.Content = "移除已选（" + selected.Count + "）";
		undoRemoval.IsEnabled = C.CanUndoRemoval;
		undoRemoval.ToolTip = "撤销：" + C.UndoLabel + " · Ctrl+Z";
		undoRemoval.Visibility = ((!C.CanUndoRemoval) ? Visibility.Collapsed : Visibility.Visible);
		selectionCount.Text = (editing ? ("已选 " + selected.Count + " · 跨页保留") : "可撤销最近操作");
	}

	public void Render(bool animate = false)
	{
		if (Tiles == null || (!base.IsVisible && !preparingView))
		{
			return;
		}
		UpdateSelectionBar();
		RefreshFolders();
		Tiles.Children.Clear();
		List<Entry> list = Filtered();
		int num = LayoutRules.Pages(list.Count, capacity);
		page = Math.Min(page, num - 1);
		Pager.Text = page + 1 + " / " + num;
		Empty.Visibility = ((list.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		if (pendingStatus != null)
		{
			Status.Text = pendingStatus;
		}
		int num2 = 0;
		foreach (Entry item in list.Skip(page * capacity).Take(capacity))
		{
			Tiles.Children.Add(Tile(item, num2++, animate));
		}
	}

	private void ChangePage(int delta)
	{
		int num = LayoutRules.Pages(Filtered().Count, capacity);
		page = Math.Max(0, Math.Min(num - 1, page + delta));
		Render(animate: true);
	}

	private FrameworkElement Tile(Entry a, int index, bool animate)
	{
		bool isSelected = editing && selected.Contains(a.Path);
		SolidColorBrush selectedBrush = new SolidColorBrush(Color.FromArgb(48, 111, 167, byte.MaxValue));
		Border tile = new Border
		{
			Tag = a,
			Background = (isSelected ? selectedBrush : Brushes.Transparent),
			BorderThickness = new Thickness(1.0),
			BorderBrush = (isSelected ? new SolidColorBrush(Color.FromArgb(150, 146, 187, byte.MaxValue)) : Brushes.Transparent),
			CornerRadius = new CornerRadius(18.0),
			Margin = new Thickness(C.Prefs.BoardIconGap),
			Padding = new Thickness(4.0),
			Cursor = Cursors.Hand,
			ToolTip = a.Name,
			ContextMenu = (editing ? null : C.Menu(a, currentGroup)),
			AllowDrop = !editing
		};
		Grid grid = new Grid();
		tile.Child = grid;
		StackPanel stackPanel = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		grid.Children.Add(stackPanel);
		ImageSource imageSource = FeatureUi.Icon(a);
		if (imageSource != null)
		{
			Image image = new Image
			{
				Source = imageSource,
				Width = C.Prefs.BoardIconSize,
				Height = C.Prefs.BoardIconSize,
				Stretch = Stretch.Uniform,
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			};
			// 高清图标按显示尺寸平滑缩放，避免二次模糊。
			RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
			stackPanel.Children.Add(image);
		}
		else
		{
			stackPanel.Children.Add(new TextBlock
			{
				Text = "▣",
				Foreground = base.TextBrush,
				FontSize = C.Prefs.BoardIconSize * 0.84,
				Height = C.Prefs.BoardIconSize,
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			});
		}
		stackPanel.Children.Add(new TextBlock
		{
			Text = a.Name,
			Foreground = base.TextBrush,
			FontSize = 12.0,
			TextAlignment = TextAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis,
			MaxWidth = Math.Max(20.0, ((Tiles.ActualWidth > 0.0) ? Tiles.ActualWidth : (base.Width - 104.0)) / (double)columns - 2.0 * C.Prefs.BoardIconGap - 10.0)
		});
		grid.Children.Add(new Border
		{
			Tag = "running",
			Width = 5.0,
			Height = 5.0,
			CornerRadius = new CornerRadius(3.0),
			Background = new SolidColorBrush(Color.FromRgb(135, 205, byte.MaxValue)),
			VerticalAlignment = VerticalAlignment.Bottom,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 0.0, 1.0),
			ToolTip = "正在运行，点击切回窗口",
			Visibility = ((!C.Prefs.Dock.ShowRunning || !RunningWindows.IsRunning(a)) ? Visibility.Collapsed : Visibility.Visible)
		});
		if (editing)
		{
			CheckBox element = new CheckBox
			{
				IsChecked = isSelected,
				IsHitTestVisible = false,
				Focusable = false,
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(2.0)
			};
			grid.Children.Add(element);
		}
		ScaleTransform scale = new ScaleTransform(1.0, 1.0);
		TranslateTransform lift = new TranslateTransform();
		TransformGroup transformGroup = new TransformGroup();
		transformGroup.Children.Add(scale);
		transformGroup.Children.Add(lift);
		tile.RenderTransform = transformGroup;
		tile.RenderTransformOrigin = new Point(0.5, 0.5);
		Action<bool> hover = delegate(bool over)
		{
			tile.Background = (isSelected ? selectedBrush : (over ? new SolidColorBrush(Color.FromArgb(24, byte.MaxValue, byte.MaxValue, byte.MaxValue)) : Brushes.Transparent));
			if (MotionEnabled)
			{
				CubicEase easingFunction2 = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				};
				scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(over ? (1.0 + 0.025 * MotionLevel) : 1.0, TimeSpan.FromMilliseconds(170.0))
				{
					EasingFunction = easingFunction2
				});
				scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(over ? (1.0 + 0.025 * MotionLevel) : 1.0, TimeSpan.FromMilliseconds(170.0))
				{
					EasingFunction = easingFunction2
				});
				lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(over ? (-3.0 * MotionLevel) : 0.0, TimeSpan.FromMilliseconds(170.0))
				{
					EasingFunction = easingFunction2
				});
			}
		};
		tile.MouseEnter += delegate
		{
			hover(obj: true);
		};
		tile.MouseLeave += delegate
		{
			hover(obj: false);
		};
		if (animate && MotionEnabled)
		{
			CubicEase easingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			};
			TimeSpan value = TimeSpan.FromMilliseconds(Math.Min(index, 14) * 12);
			tile.Opacity = 0.0;
			tile.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(190.0))
			{
				BeginTime = value,
				EasingFunction = easingFunction
			});
			lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(9.0 * MotionLevel, 0.0, TimeSpan.FromMilliseconds(230.0))
			{
				BeginTime = value,
				EasingFunction = easingFunction
			});
		}
		tile.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
		{
			dragStart = e.GetPosition(this);
			dragEntry = a;
		};
		tile.MouseMove += delegate(object s, MouseEventArgs e)
		{
			if (e.LeftButton == MouseButtonState.Pressed && dragEntry == a)
			{
				Point position = e.GetPosition(this);
				if (Math.Abs(position.X - dragStart.X) + Math.Abs(position.Y - dragStart.Y) > 10.0)
				{
					dragEntry = null;
					StartEntryDrag(tile, a);
				}
			}
		};
		tile.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
		{
			if (dragEntry == a)
			{
				dragEntry = null;
				if (editing)
				{
					if (!selected.Remove(a.Path))
					{
						selected.Add(a.Path);
					}
					Render();
					e.Handled = true;
				}
				else
				{
					C.Launch(a);
				}
			}
		};
		tile.Drop += delegate(object s, DragEventArgs e)
		{
			if (e.Data.GetDataPresent("GlassBoard.Entry"))
			{
				string path = (string)e.Data.GetData("GlassBoard.Entry");
				C.ReorderEntry(path, a);
				e.Handled = true;
			}
		};
		return tile;
	}

	private string[] DraggedEntries(IDataObject data)
	{
		if (data.GetDataPresent("GlassBoard.Entries"))
		{
			return (string[])data.GetData("GlassBoard.Entries");
		}
		if (data.GetDataPresent("GlassBoard.Entry"))
		{
			return new string[1] { (string)data.GetData("GlassBoard.Entry") };
		}
		return new string[0];
	}

	/// <summary>按名称切换到指定分组，供新建分组后立即跳转使用。</summary>
	public void OpenGroupByName(string name)
	{
		if (!string.IsNullOrWhiteSpace(name))
		{
			OpenGroup(name);
		}
	}

	private void OpenGroup(string name)
	{
		FinishSelection();
		currentGroup = name;
		page = 0;
		ClearSearch();
		SyncGroups();
		Render(animate: true);
	}

	/// <summary>分组增删改后强制重建分类条，避免旧签名导致界面不刷新。</summary>
	public void InvalidateFolders()
	{
		folderSignature = "";
	}

	private void RefreshFolders()
	{
		if (folderBar == null || (activeDrag && folderBar.Children.Count > 0))
		{
			return;
		}
		string text = currentGroup + "/" + LightTheme + "/" + C.Prefs.ShowUngrouped + "/" + string.Join("|", from g in FeatureRules.Filters(C.Prefs)
			select g + ":" + C.Prefs.Apps.Count((Entry a) => FeatureRules.InGroup(a, g)));
		if (folderSignature == text && folderBar.Children.Count > 0)
		{
			return;
		}
		folderSignature = text;
		folderBar.Children.Clear();
		foreach (string item in FeatureRules.Filters(C.Prefs))
		{
			string name = item;
			int num = ((name == "全部") ? C.Prefs.Apps.Count : C.Prefs.Apps.Count((Entry a) => FeatureRules.InGroup(a, name)));
			SolidColorBrush normal = ((name == currentGroup) ? new SolidColorBrush(Color.FromArgb(55, 130, 180, byte.MaxValue)) : new SolidColorBrush(Color.FromArgb(22, byte.MaxValue, byte.MaxValue, byte.MaxValue)));
			Border box = new Border
			{
				CornerRadius = new CornerRadius(12.0),
				Background = normal,
				Padding = new Thickness(10.0, 6.0, 10.0, 6.0),
				Margin = new Thickness(0.0, 0.0, 6.0, 6.0),
				Cursor = Cursors.Hand,
				AllowDrop = true,
				ToolTip = ((name == "全部") ? "查看全部入口" : "点击打开；将一个或多个入口拖到这里分组")
			};
			StackPanel stackPanel = new StackPanel
			{
				Orientation = Orientation.Horizontal
			};
			FrameworkElement frameworkElement = Polish.Icon((name == "全部") ? "grid" : "folder", base.TextBrush, 18.0);
			frameworkElement.Margin = new Thickness(0.0, 0.0, 6.0, 0.0);
			stackPanel.Children.Add(frameworkElement);
			stackPanel.Children.Add(new TextBlock
			{
				Text = name + "  " + num,
				Foreground = base.TextBrush,
				FontSize = 12.0,
				VerticalAlignment = VerticalAlignment.Center
			});
			box.Child = stackPanel;
			box.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
			{
				OpenGroup(name);
				e.Handled = true;
			};
			// 右键分类可直接改名、删除；「未分组」可以整体隐藏，只留「全部」。
			if (name == "未分组")
			{
				ContextMenu ungroupedMenu = new ContextMenu();
				MenuItem hideUngrouped = new MenuItem
				{
					Header = "隐藏「未分组」，只留「全部」"
				};
				hideUngrouped.Click += delegate
				{
					C.Prefs.ShowUngrouped = false;
					C.Save();
					OpenGroup("全部");
					C.Refresh();
					SetStatus("已隐藏「未分组」分类；可在设置里重新打开");
				};
				ungroupedMenu.Items.Add(hideUngrouped);
				box.ContextMenu = ungroupedMenu;
			}
			else if (name != "全部")
			{
				ContextMenu chip = new ContextMenu();
				MenuItem rename = new MenuItem
				{
					Header = "重命名分组「" + name + "」"
				};
				rename.Click += delegate
				{
					string text2 = FeatureUi.Ask(this, "分组改名", name);
					if (text2 != null && text2 != name)
					{
						try
						{
							BoardEditState before = C.BeginBoardEdit();
							FeatureRules.RenameGroup(C.Prefs, name, text2);
							C.EndBoardEdit(before, "重命名分组");
							C.Refresh();
							OpenGroup(text2);
						}
						catch (Exception ex)
						{
							System.Windows.MessageBox.Show(this, ex.Message, "GlassBoard");
						}
					}
				};
				chip.Items.Add(rename);
				MenuItem delete = new MenuItem
				{
					Header = "删除分组「" + name + "」"
				};
				delete.Click += delegate
				{
					if (System.Windows.MessageBox.Show(this, "删除分组「" + name + "」？入口保留并移到未分组。", "GlassBoard", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
					{
						BoardEditState before = C.BeginBoardEdit();
						FeatureRules.DeleteGroup(C.Prefs, name);
						C.EndBoardEdit(before, "删除分组");
						C.Refresh();
						OpenGroup("全部");
					}
				};
				chip.Items.Add(delete);
				box.ContextMenu = chip;
			}
			box.DragEnter += delegate(object sender, DragEventArgs e)
			{
				if (DraggedEntries(e.Data).Length != 0 || e.Data.GetDataPresent(DataFormats.FileDrop))
				{
					BeginGroupHover(name);
					box.Background = new SolidColorBrush(Color.FromArgb(90, 100, 160, 240));
					e.Effects = GroupDragEffect(e.Data, name);
					e.Handled = true;
				}
			};
			box.DragOver += delegate(object sender, DragEventArgs e)
			{
				if (DraggedEntries(e.Data).Length != 0 || e.Data.GetDataPresent(DataFormats.FileDrop))
				{
					BeginGroupHover(name);
				}
				e.Effects = GroupDragEffect(e.Data, name);
				e.Handled = true;
			};
			box.DragLeave += delegate
			{
				StopGroupHover(name);
				box.Background = normal;
			};
			box.Drop += delegate(object sender, DragEventArgs e)
			{
				e.Handled = true;
				if (!(name == "全部"))
				{
					string[] array = DraggedEntries(e.Data);
					if (array.Length != 0)
					{
						string from = (e.Data.GetDataPresent("GlassBoard.SourceGroup") ? ((string)e.Data.GetData("GlassBoard.SourceGroup")) : currentGroup);
						C.TransferEntries(array, from, name);
					}
					else if (e.Data.GetDataPresent(DataFormats.FileDrop))
					{
						C.Add((string[])e.Data.GetData(DataFormats.FileDrop), manual: true, name);
						OpenGroup(name);
						SetStatus("已添加到「" + name + "」");
					}
				}
			};
			folderBar.Children.Add(box);
		}
	}

	private void ExternalDrop(object s, DragEventArgs e)
	{
		if (e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			C.Add((string[])e.Data.GetData(DataFormats.FileDrop), manual: true, currentGroup);
			ClearSearch();
			e.Handled = true;
		}
	}

	public void UpdateRunning()
	{
		if (Tiles == null || !base.IsVisible)
		{
			return;
		}
		foreach (Border item in Tiles.Children.OfType<Border>())
		{
			Entry entry = item.Tag as Entry;
			Grid grid = item.Child as Grid;
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
}
