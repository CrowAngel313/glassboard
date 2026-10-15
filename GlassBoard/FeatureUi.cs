using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlassBoard;

public static class FeatureUi
{
	public static string PickFolder(Window window, string description)
	{
		using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
		{
			Description = description,
			ShowNewFolderButton = false
		};
		NativeWindow nativeWindow = new NativeWindow();
		nativeWindow.AssignHandle(new WindowInteropHelper(window).EnsureHandle());
		try
		{
			return (folderBrowserDialog.ShowDialog(nativeWindow) == DialogResult.OK) ? folderBrowserDialog.SelectedPath : null;
		}
		finally
		{
			nativeWindow.ReleaseHandle();
		}
	}

	public static HotkeySetting Record(Window owner, int action)
	{
		Window w = Dialog("录制快捷键", owner, 480.0, 230.0);
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(24.0)
		};
		w.Content = stackPanel;
		stackPanel.Children.Add(new TextBlock
		{
			Text = "按下一组快捷键",
			FontSize = 22.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 16.0)
		});
		TextBlock hint = new TextBlock
		{
			Text = "需要 Ctrl / Alt / Shift，搭配字母、数字、功能键等。\nEsc 取消；占用情况会在保存时检查。",
			TextWrapping = TextWrapping.Wrap
		};
		stackPanel.Children.Add(hint);
		HotkeySetting captured = null;
		w.PreviewKeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e)
		{
			e.Handled = true;
			Key key = ((e.Key == Key.System) ? e.SystemKey : e.Key);
			if (key == Key.Escape)
			{
				w.Close();
			}
			else if (captured == null && key != Key.LeftCtrl && key != Key.RightCtrl && key != Key.LeftAlt && key != Key.RightAlt && key != Key.LeftShift && key != Key.RightShift)
			{
				uint modifiers = (uint)Keyboard.Modifiers;
				uint num = (uint)KeyInterop.VirtualKeyFromKey(key);
				if (!HotkeyRules.ModifierValues.Contains(modifiers) || !HotkeyRules.Keys().ContainsValue(num))
				{
					hint.Text = "不支持这组按键，请使用列表支持的组合。";
				}
				else
				{
					captured = new HotkeySetting
					{
						ActionId = action,
						Modifiers = modifiers,
						Key = num,
						Enabled = true
					};
					hint.Text = "已录制：" + HotkeyRules.Label(captured) + "\n释放按键完成录制。";
				}
			}
		};
		w.PreviewKeyUp += delegate(object s, System.Windows.Input.KeyEventArgs e)
		{
			e.Handled = true;
			if (captured != null && Keyboard.Modifiers == ModifierKeys.None && !Keyboard.IsKeyDown(KeyInterop.KeyFromVirtualKey((int)captured.Key)))
			{
				w.DialogResult = true;
			}
		};
		w.ShowDialog();
		if (!w.DialogResult.GetValueOrDefault())
		{
			return null;
		}
		return captured;
	}

	public static Window Dialog(string title, Window owner, double width = 650.0, double height = 560.0)
	{
		Window obj = new Window
		{
			Title = "GlassBoard · " + title,
			Owner = owner,
			Width = Math.Min(width, SystemParameters.WorkArea.Width - 40.0),
			Height = Math.Min(height, SystemParameters.WorkArea.Height - 50.0),
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = new SolidColorBrush(Color.FromRgb(30, 33, 42)),
			Foreground = Brushes.White,
			Topmost = true
		};
		Polish.Apply(obj, light: false);
		return obj;
	}

	public static System.Windows.Controls.Button Button(string text, Action action)
	{
		System.Windows.Controls.Button button = new System.Windows.Controls.Button();
		button.Content = text;
		button.Padding = new Thickness(12.0, 7.0, 12.0, 7.0);
		button.Margin = new Thickness(0.0, 0.0, 8.0, 8.0);
		button.Click += delegate
		{
			action();
		};
		return button;
	}

	public static string Ask(Window owner, string title, string value = "")
	{
		Window w = Dialog(title, owner, 440.0, 180.0);
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(22.0)
		};
		w.Content = stackPanel;
		System.Windows.Controls.TextBox text = new System.Windows.Controls.TextBox
		{
			Text = value,
			MinHeight = 38.0,
			FontSize = 14.0
		};
		stackPanel.Children.Add(text);
		string result = null;
		System.Windows.Controls.Button apply = Button("确定", delegate
		{
			if (!string.IsNullOrWhiteSpace(text.Text))
			{
				result = text.Text.Trim();
				w.DialogResult = true;
			}
		});
		apply.Margin = new Thickness(0.0, 18.0, 0.0, 0.0);
		stackPanel.Children.Add(apply);
		w.Loaded += delegate
		{
			text.Focus();
			text.SelectAll();
		};
		text.KeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == Key.Return)
			{
				apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
			}
		};
		w.ShowDialog();
		return result;
	}

	public static string StoreIcon(string path)
	{
		Directory.CreateDirectory(Path.Combine(Store.Folder, "icons"));
		BitmapImage bitmapImage = new BitmapImage();
		bitmapImage.BeginInit();
		bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
		bitmapImage.UriSource = new Uri(path);
		bitmapImage.DecodePixelWidth = 256;
		bitmapImage.EndInit();
		bitmapImage.Freeze();
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(BitmapFrame.Create(bitmapImage));
		string text = Path.Combine(Store.Folder, "icons", Guid.NewGuid().ToString("N") + ".png");
		using FileStream stream = File.Create(text);
		pngBitmapEncoder.Save(stream);
		return text;
	}

	public static ImageSource Icon(Entry entry)
	{
		// 文件夹（含目标盘未就绪的）直接返回系统文件夹图标：
		// 对慢速/未就绪驱动器做 SHGetFileInfo 会卡住 UI 线程数秒，是 AppHang 的主因。
		if (entry.IsFolder)
		{
			ImageSource folderIcon = ShellIcons.ForShellName("shell:Programs");
			if (folderIcon != null)
			{
				return folderIcon;
			}
		}
		if (!string.IsNullOrWhiteSpace(entry.IconPath) && File.Exists(entry.IconPath))
		{
			try
			{
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.UriSource = new Uri(entry.IconPath);
				bitmapImage.DecodePixelWidth = 256;
				bitmapImage.EndInit();
				bitmapImage.Freeze();
				return bitmapImage;
			}
			catch
			{
			}
		}
		// 快捷方式优先取真实目标的图标，避免系统箭头角标并拿到 256px 高清图。
		ImageSource imageSource = EntryIcon(entry);
		if (imageSource != null)
		{
			return imageSource;
		}
		if (entry.IsFolder || Directory.Exists(entry.Path))
		{
			// 真实文件夹图标（与资源管理器一致），取代原先手绘的黄色方块。
			ImageSource folder = ShellIcons.ForShellName("shell:Programs");
			if (folder != null)
			{
				return folder;
			}
		}
		return null;
	}

	/// <summary>按入口取高清图标：用户手改的 &gt; 快捷方式自带的 &gt; 真实启动目标 &gt; 入口本身；带缓存。</summary>
	private static ImageSource EntryIcon(Entry entry)
	{
		// 1) 快捷方式自带的 IconLocation。
		//    必须排在启动目标之前：Edge/Chrome 的「应用化网页」快捷方式目标是
		//    没有图标资源的 msedge_proxy.exe / chrome_proxy.exe，
		//    先取目标就会拿到一张空白纸，把用户自己设的图标盖掉。
		if (!string.IsNullOrWhiteSpace(entry.ShortcutIcon))
		{
			ImageSource shortcutIcon = LoadIconFile(entry.ShortcutIcon);
			if (shortcutIcon != null)
			{
				return shortcutIcon;
			}
		}
		string text = null;
		string launchTarget = entry.LaunchTarget;
		if (!string.IsNullOrWhiteSpace(launchTarget) && (File.Exists(launchTarget) || Directory.Exists(launchTarget)))
		{
			text = launchTarget;
		}
		ImageSource imageSource = ((text == null) ? null : Native.Icon(text));
		if (imageSource == null)
		{
			imageSource = Native.Icon(entry.Path);
		}
		return imageSource;
	}

	/// <summary>
	/// 读图标来源（.ico / .exe / .dll）。
	/// .ico 必须直接用 WPF 解码：走 SHGetFileInfo 只会拿到「.ico 文件的类型图标」
	/// （一张白纸），而不是文件里真正的图像。
	/// </summary>
	private static ImageSource LoadIconFile(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return null;
		}
		string ext = Path.GetExtension(path).ToLowerInvariant();
		if (ext == ".ico" || ext == ".png" || ext == ".bmp")
		{
			try
			{
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.UriSource = new Uri(path);
				bitmapImage.DecodePixelWidth = 256;
				bitmapImage.EndInit();
				bitmapImage.Freeze();
				return bitmapImage;
			}
			catch
			{
				return null;
			}
		}
		// .exe / .dll：走 shell 取资源图标
		return Native.Icon(path);
	}
}
