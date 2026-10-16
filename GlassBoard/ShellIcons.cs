using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlassBoard;

/// <summary>
/// 真实 Windows 系统图标来源。
/// 原实现用 SHGetFileInfo 取 32x32 图标再放大到 64x64，是面板图标模糊的根因；
/// 这里改用系统 jumbo 图像列表（256x256）与 shell 命名空间图标，绝不放大低分辨率位图。
/// </summary>
public static class ShellIcons
{
	private const uint SHGFI_SYSICONINDEX = 0x4000u;

	private const uint SHGFI_PIDL = 8u;

	private const int ILD_TRANSPARENT = 1;

	private const string IID_IImageList_Value = "46EB5926-582E-4017-9FDF-E8998DAA0950";

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

	[ComImport]
	[Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IImageList
	{
		[PreserveSig]
		int Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);

		[PreserveSig]
		int ReplaceIcon(int i, IntPtr hicon, out int pi);

		[PreserveSig]
		int SetOverlayImage(int iImage, int iOverlay);

		[PreserveSig]
		int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);

		[PreserveSig]
		int AddMasked(IntPtr hbmImage, int crMask, out int pi);

		[PreserveSig]
		int Draw(IntPtr pimldp);

		[PreserveSig]
		int Remove(int i);

		[PreserveSig]
		int GetIcon(int i, int flags, out IntPtr picon);
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
	private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

	[DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW")]
	private static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, uint dwFileAttributes, out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

	[DllImport("shell32.dll")]
	private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int PrivateExtractIcons(string lpszFile, int nIconIndex, int cxIcon, int cyIcon, IntPtr[] phicon, int[] piconid, int nIcons, uint flags);

	[DllImport("user32.dll")]
	private static extern bool DestroyIcon(IntPtr hIcon);

	[DllImport("ole32.dll")]
	private static extern void CoTaskMemFree(IntPtr pv);

	/// <summary>取系统图标缓存里的高清图标：先试 256，再 48，最后 32，绝不放大低分辨率位图。</summary>
	private static ImageSource FromSysIndex(int index)
	{
		if (index < 0)
		{
			return null;
		}
		int[] levels = new int[3] { 4, 2, 0 };
		foreach (int level in levels)
		{
			Guid iid = new Guid(IID_IImageList_Value);
			IImageList list;
			if (SHGetImageList(level, ref iid, out list) != 0 || list == null)
			{
				continue;
			}
			IntPtr hIcon = IntPtr.Zero;
			int hr;
			try
			{
				hr = list.GetIcon(index, ILD_TRANSPARENT, out hIcon);
			}
			finally
			{
				Marshal.ReleaseComObject(list);
			}
			if (hr == 0 && hIcon != IntPtr.Zero)
			{
				ImageSource result = FromHIcon(hIcon);
				DestroyIcon(hIcon);
				if (result != null)
				{
					return result;
				}
			}
		}
		return null;
	}

	private static ImageSource FromHIcon(IntPtr hIcon)
	{
		if (hIcon == IntPtr.Zero)
		{
			return null;
		}
		try
		{
			BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
			bitmapSource.Freeze();
			return Trim(bitmapSource);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// 去掉图标四周的透明空白。
	/// 必要性：jumbo(256) 图像列表对「没有 256 版本」的应用会返回一个 256x256 的 HICON，
	/// 但实际只把 48x48 的内容画在左上角，其余全透明。不裁掉的话，缩放到面板尺寸后
	/// 图标只占左上角很小一块，看起来就像「图标显示错乱」。
	/// </summary>
	private static ImageSource Trim(BitmapSource source)
	{
		if (source == null || source.PixelWidth < 2 || source.PixelHeight < 2)
		{
			return source;
		}
		try
		{
			FormatConvertedBitmap converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0.0);
			int w = converted.PixelWidth;
			int h = converted.PixelHeight;
			int stride = w * 4;
			byte[] buffer = new byte[stride * h];
			converted.CopyPixels(buffer, stride, 0);
			int minX = w;
			int minY = h;
			int maxX = -1;
			int maxY = -1;
			for (int y = 0; y < h; y++)
			{
				int row = y * stride;
				for (int x = 0; x < w; x++)
				{
					if (buffer[row + x * 4 + 3] > 8)
					{
						if (x < minX)
						{
							minX = x;
						}
						if (x > maxX)
						{
							maxX = x;
						}
						if (y < minY)
						{
							minY = y;
						}
						if (y > maxY)
						{
							maxY = y;
						}
					}
				}
			}
			if (maxX < 0)
			{
				return source;
			}
			if (minX == 0 && minY == 0 && maxX == w - 1 && maxY == h - 1)
			{
				return source;
			}
			CroppedBitmap cropped = new CroppedBitmap(source, new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1));
			cropped.Freeze();
			return cropped;
		}
		catch
		{
			return source;
		}
	}

	/// <summary>文件、文件夹、快捷方式的高清图标。传入解析后的真实目标可避免快捷方式箭头角标。</summary>
	public static ImageSource ForPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		try
		{
			// 未就绪的驱动器（如未插入的 U 盘、未挂载的网络盘）上调用 SHGetFileInfo
			// 会阻塞 UI 线程数秒甚至数十秒（AppHang 事件 1002）。DriveInfo.IsReady 是快速检查，
			// 未就绪直接返回 null 走降级图标。
			// 网络路径（UNC，如 \\server\share）上调用 SHGetFileInfo 可能阻塞 UI 线程数十秒，
			// 且 DriveInfo 检查覆盖不到，直接返回 null 走降级图标。
			if (path.StartsWith(@"\\"))
			{
				return null;
			}
			try
			{
				string root = Path.GetPathRoot(path);
				if (!string.IsNullOrEmpty(root) && root.Length >= 2 && root[1] == ':')
				{
					if (!new DriveInfo(root).IsReady)
					{
						return null;
					}
				}
			}
			catch
			{
			}
			SHFILEINFO info;
			if (SHGetFileInfo(path, 0u, out info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), SHGFI_SYSICONINDEX) == IntPtr.Zero)
			{
				return null;
			}
			return FromSysIndex(info.iIcon);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>shell 命名空间对象（如 shell:MyComputerFolder）的真实系统图标。</summary>
	public static ImageSource ForShellName(string shellName)
	{
		if (string.IsNullOrWhiteSpace(shellName))
		{
			return null;
		}
		IntPtr pidl = IntPtr.Zero;
		try
		{
			uint attrs;
			if (SHParseDisplayName(shellName, IntPtr.Zero, out pidl, 0u, out attrs) != 0 || pidl == IntPtr.Zero)
			{
				return null;
			}
			SHFILEINFO info;
			if (SHGetFileInfoPidl(pidl, 0u, out info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), SHGFI_PIDL | SHGFI_SYSICONINDEX) == IntPtr.Zero)
			{
				return null;
			}
			return FromSysIndex(info.iIcon);
		}
		catch
		{
			return null;
		}
		finally
		{
			if (pidl != IntPtr.Zero)
			{
				CoTaskMemFree(pidl);
			}
		}
	}

	/// <summary>系统模块内的图标资源，例如 shell32.dll 的索引。</summary>
	public static ImageSource ForModule(string module, int index)
	{
		if (string.IsNullOrWhiteSpace(module))
		{
			return null;
		}
		try
		{
			IntPtr[] handles = new IntPtr[1];
			int[] ids = new int[1];
			if (PrivateExtractIcons(module, index, 256, 256, handles, ids, 1, 0u) <= 0 || handles[0] == IntPtr.Zero)
			{
				return null;
			}
			ImageSource result = FromHIcon(handles[0]);
			DestroyIcon(handles[0]);
			return result;
		}
		catch
		{
			return null;
		}
	}

	private static readonly BoundedCache<string, ImageSource> builtIn = new BoundedCache<string, ImageSource>(16, StringComparer.Ordinal);

	/// <summary>
	/// Dock 内置按钮的真实 Windows 图标，取代原先手绘的矢量图形。
	/// apps=应用面板，computer=此电脑，settings=系统设置，more/add 无系统对应图标时返回 null 由调用方降级为文字。
	/// </summary>
	public static ImageSource BuiltIn(string kind)
	{
		if (builtIn.TryGet(kind, out var value))
		{
			return value;
		}
		ImageSource imageSource = null;
		string system = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
		switch (kind)
		{
		case "apps":
			imageSource = ForModule(Environment.SystemDirectory + "\\imageres.dll", 249);
			break;
		case "computer":
			imageSource = ForShellName("shell:MyComputerFolder");
			break;
		case "settings":
			imageSource = ForPath(system + "\\ImmersiveControlPanel\\SystemSettings.exe");
			break;
		case "folder":
			imageSource = ForShellName("shell:Programs");
			break;
		case "downloads":
			imageSource = ForShellName("shell:Downloads");
			break;
		}
		if (imageSource != null)
		{
			builtIn.Put(kind, imageSource);
		}
		return imageSource;
	}

	/// <summary>
	/// 动漫娘按钮图标（嵌入资源）。which: "board"=面板开/关（粉发）, "settings"=设置（蓝发）。
	/// 失败时返回 null，调用方降级为原有系统图标。
	/// </summary>
	public static ImageSource Miya(string which)
	{
		try
		{
			string resName = (which == "settings") ? "miya_settings.png" : "miya_board.png";
			using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName);
			if (s == null)
			{
				return null;
			}
			BitmapImage bi = new BitmapImage();
			bi.BeginInit();
			bi.CacheOption = BitmapCacheOption.OnLoad;
			bi.StreamSource = s;
			bi.EndInit();
			bi.Freeze();
			return bi;
		}
		catch
		{
			return null;
		}
	}
}
