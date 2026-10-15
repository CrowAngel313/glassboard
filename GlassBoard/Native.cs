using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlassBoard;

public static class Native
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct SHFILEINFO
	{
		public IntPtr hIcon;

		public int iIcon;

		public uint attributes;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string displayName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
		public string typeName;
	}

	public const int RestoreMessage = 32815;

	private static readonly BoundedCache<string, ImageSource> icons = new BoundedCache<string, ImageSource>(192, StringComparer.OrdinalIgnoreCase);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr FindWindow(string cls, string title);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool PostMessage(IntPtr h, int message, IntPtr w, IntPtr l);

	public static bool RequestRestore()
	{
		IntPtr intPtr = FindWindow(null, "GlassBoard Dock");
		if (intPtr != IntPtr.Zero)
		{
			return PostMessage(intPtr, 32815, IntPtr.Zero, IntPtr.Zero);
		}
		return false;
	}

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint key);

	[DllImport("user32.dll")]
	public static extern bool UnregisterHotKey(IntPtr h, int id);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool SetWindowDisplayAffinity(IntPtr h, uint value);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool GetWindowDisplayAffinity(IntPtr h, out uint value);

	[DllImport("user32.dll")]
	public static extern bool SetForegroundWindow(IntPtr h);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr SHGetFileInfo(string path, uint attr, out SHFILEINFO info, uint cb, uint flags);

	[DllImport("user32.dll")]
	private static extern bool DestroyIcon(IntPtr h);

	public static ImageSource Icon(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		if (icons.TryGet(path, out var value))
		{
			return value;
		}
		// 使用系统 jumbo 图像列表取 256x256 高清图标。
		// 原实现取 32x32 再拉伸到 64x64，是大面板图标模糊的根因。
		BitmapSource bitmapSource = ShellIcons.ForPath(path) as BitmapSource;
		if (bitmapSource == null)
		{
			bitmapSource = LegacyIcon(path);
		}
		if (bitmapSource == null)
		{
			return null;
		}
		bitmapSource.Freeze();
		icons.Put(path, bitmapSource);
		return bitmapSource;
	}

	/// <summary>旧路径：SHGetFileInfo 的 32x32 图标，仅在 jumbo 取图失败时兜底。</summary>
	private static BitmapSource LegacyIcon(string path)
	{
		SHGetFileInfo(path, 0u, out var info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), 256u);
		if (info.hIcon == IntPtr.Zero)
		{
			return null;
		}
		try
		{
			BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
			bitmapSource.Freeze();
			return bitmapSource;
		}
		catch
		{
			return null;
		}
		finally
		{
			DestroyIcon(info.hIcon);
		}
	}

	public static void Computer()
	{
		Process.Start(new ProcessStartInfo("explorer.exe", "shell:MyComputerFolder")
		{
			UseShellExecute = true
		});
	}
}
