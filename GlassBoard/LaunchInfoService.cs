using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace GlassBoard;

public static class LaunchInfoService
{
	public static Entry Read(string path)
	{
		Entry obj = new Entry
		{
			Name = EntryPathRules.Name(path),
			Path = EntryPathRules.Normalize(path),
			IsFolder = Directory.Exists(path)
		};
		Enrich(obj);
		return obj;
	}

	public static void Enrich(Entry entry)
	{
		string text = entry.Path ?? "";
		string text2 = "";
		string text3 = Path.GetExtension(text).ToLowerInvariant();
		if (Directory.Exists(text))
		{
			entry.IsFolder = true;
			entry.LaunchTarget = text;
			entry.LaunchArguments = "";
			return;
		}
		if (text3 == ".lnk")
		{
			object obj = null;
			object obj2 = null;
			try
			{
				Type typeFromProgID = Type.GetTypeFromProgID("WScript.Shell");
				obj = Activator.CreateInstance(typeFromProgID);
				obj2 = typeFromProgID.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, obj, new object[1] { text });
				text = (string)obj2.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, obj2, null);
				text2 = (string)obj2.GetType().InvokeMember("Arguments", BindingFlags.GetProperty, null, obj2, null);
				// 快捷方式自带的图标位置。Edge/Chrome 的「应用化网页」快捷方式目标是无图标的
				// msedge_proxy.exe / chrome_proxy.exe，真正的图标只存在于这里，
				// 不读它就会显示成一张空白纸。
				entry.ShortcutIcon = ResolveIconLocation((string)obj2.GetType().InvokeMember("IconLocation", BindingFlags.GetProperty, null, obj2, null));
				// TargetPath 为空时，.lnk 内部可能只藏了一个网址（安装包塞的在线文档/主页）。
				if (string.IsNullOrWhiteSpace(text))
				{
					entry.ShortcutUrl = ReadEmbeddedUrl(entry.Path);
				}
			}
			catch
			{
			}
			finally
			{
				if (obj2 != null && Marshal.IsComObject(obj2))
				{
					Marshal.ReleaseComObject(obj2);
				}
				if (obj != null && Marshal.IsComObject(obj))
				{
					Marshal.ReleaseComObject(obj);
				}
			}
		}
		else if (text3 == ".url")
		{
			text = StartupEntryRules.ReadUrl(text);
		}
		if (StartupEntryRules.Auxiliary(text) && text2.IndexOf("--processStart", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			Match match = Regex.Match(text2, "--processStart\\s+(?:\"([^\"]+)\"|([^\\s]+))", RegexOptions.IgnoreCase);
			if (match.Success)
			{
				string text4 = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
				if (!StartupEntryRules.Auxiliary(text4))
				{
					text = Path.Combine(Path.GetDirectoryName(text) ?? "", text4);
				}
			}
		}
		entry.LaunchTarget = text;
		entry.LaunchArguments = text2;
		entry.IsFolder = entry.IsFolder || Directory.Exists(text) || IsUnreadyFolderTarget(text3, text);
		try
		{
			if (Path.GetExtension(text ?? "").Equals(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(text))
			{
				FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(text);
				entry.ProductInfo = versionInfo.ProductName + " " + versionInfo.FileDescription;
			}
		}
		catch
		{
		}
	}

	/// <summary>
	/// 目标盘未就绪时（如 G:/A: 盘未挂载），.lnk 指向文件夹的目标无扩展名，
	/// Directory.Exists 会返回 false。这里按"无扩展名"启发式仍认作文件夹，
	/// 保证桌面文件夹快捷方式一定显示、可分类。双击时若目标不存在会有友好提示。
	/// </summary>
	private static bool IsUnreadyFolderTarget(string entryExt, string target)
	{
		if (entryExt != ".lnk" || string.IsNullOrWhiteSpace(target))
		{
			return false;
		}
		if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || target.StartsWith("::{"))
		{
			return false;
		}
		return string.IsNullOrEmpty(Path.GetExtension(target));
	}

	public static bool Primary(Entry entry)
	{
		if (!StartupEntryRules.Candidate(entry.Path) || StartupEntryRules.Auxiliary(entry.LaunchTarget))
		{
			return false;
		}
		// 文件夹（含目标盘未就绪的）允许导入，自动归入"文件夹"分组
		if (entry.IsFolder)
		{
			return true;
		}
		string launchTarget = entry.LaunchTarget;
		if (string.IsNullOrWhiteSpace(launchTarget))
		{
			return true;
		}
		if (Directory.Exists(launchTarget))
		{
			return false;
		}
		if (Path.GetExtension(launchTarget).ToLowerInvariant().Length != 0 && !StartupEntryRules.GameUri(launchTarget))
		{
			return EntryRules.Importable(launchTarget);
		}
		return true;
	}

	/// <summary>
	/// 老配置回填：把每个条目的快捷方式自带图标补读一遍，只做一次。
	/// 返回是否真的做了改动。
	/// </summary>
	public static bool BackfillShortcutIcons(Preferences prefs)
	{
		if (prefs == null || prefs.ShortcutIconBackfilled)
		{
			return false;
		}
		foreach (Entry app in prefs.Apps)
		{
			if (app == null || string.IsNullOrWhiteSpace(app.Path))
			{
				continue;
			}
			if (!Path.GetExtension(app.Path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			// 已经有值的（含用户手改的）不覆盖
			if (!string.IsNullOrWhiteSpace(app.ShortcutIcon))
			{
				continue;
			}
			app.ShortcutIcon = ReadShortcutIcon(app.Path);
		}
		prefs.ShortcutIconBackfilled = true;
		return true;
	}

	/// <summary>读取 .lnk 的 IconLocation 并解析成可用的图标文件路径。</summary>
	private static string ReadShortcutIcon(string lnkPath)
	{
		object obj = null;
		object obj2 = null;
		try
		{
			Type typeFromProgID = Type.GetTypeFromProgID("WScript.Shell");
			obj = Activator.CreateInstance(typeFromProgID);
			obj2 = typeFromProgID.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, obj, new object[1] { lnkPath });
			return ResolveIconLocation((string)obj2.GetType().InvokeMember("IconLocation", BindingFlags.GetProperty, null, obj2, null));
		}
		catch
		{
			return null;
		}
		finally
		{
			if (obj2 != null && Marshal.IsComObject(obj2))
			{
				Marshal.ReleaseComObject(obj2);
			}
			if (obj != null && Marshal.IsComObject(obj))
			{
				Marshal.ReleaseComObject(obj);
			}
		}
	}

	/// <summary>
	/// 从 .lnk 原始内容里找内嵌的 http(s) 网址。
	/// .lnk 可能把 URL 按 ANSI 或 UTF-16 存放，也可能字符间插空字节，两种都要认。
	/// </summary>
	private static string ReadEmbeddedUrl(string lnkPath)
	{
		try
		{
			byte[] raw = File.ReadAllBytes(lnkPath);
			if (raw.Length == 0 || raw.Length > 1048576)
			{
				return null;
			}
			// 先按 ANSI 找，再按 UTF-16 找
			foreach (Encoding encoding in new Encoding[2] { Encoding.ASCII, Encoding.Unicode })
			{
				string text = encoding.GetString(raw);
				int start = text.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
				int start2 = text.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
				if (start < 0 || (start2 >= 0 && start2 < start))
				{
					start = start2;
				}
				if (start < 0)
				{
					continue;
				}
				StringBuilder stringBuilder = new StringBuilder();
				for (int i = start; i < text.Length && i < start + 2048; i++)
				{
					char c = text[i];
					if (c < ' ' || c > '~')
					{
						break;
					}
					stringBuilder.Append(c);
				}
				string url = stringBuilder.ToString().Trim();
				if (url.Length > 8)
				{
					return url;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	/// <summary>
	/// 解析快捷方式的 IconLocation（形如 "C:\a\b.ico,0" 或 "shell32.dll,-1"）。
	/// 返回可直接读文件的 .ico/.exe/.dll 路径；空或无效时返回 null。
	/// </summary>
	private static string ResolveIconLocation(string iconLocation)
	{
		if (string.IsNullOrWhiteSpace(iconLocation))
		{
			return null;
		}
		// 去掉末尾的 ",索引"；注意路径本身可能含逗号，所以只从最后一个逗号后判断是否为纯数字
		string text = iconLocation.Trim();
		int num = text.LastIndexOf(',');
		if (num > 0 && num < text.Length - 1)
		{
			string tail = text.Substring(num + 1).Trim();
			if (tail.Length > 0 && tail.All(char.IsDigit))
			{
				text = text.Substring(0, num).Trim();
			}
		}
		text = Environment.ExpandEnvironmentVariables(text.Trim('"').Trim());
		if (text.Length == 0 || !File.Exists(text))
		{
			return null;
		}
		string ext = Path.GetExtension(text).ToLowerInvariant();
		if (ext != ".ico" && ext != ".exe" && ext != ".dll" && ext != ".png" && ext != ".bmp")
		{
			return null;
		}
		return text;
	}
}
