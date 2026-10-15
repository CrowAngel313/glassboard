using System;
using System.IO;
using System.Linq;

namespace GlassBoard;

public static class StartupEntryRules
{
	public static string Leaf(string path)
	{
		path = (path ?? "").Replace('\\', '/');
		int num = path.LastIndexOf('/');
		if (num < 0)
		{
			return path;
		}
		return path.Substring(num + 1);
	}

	public static string Stem(string path)
	{
		string text = Leaf(path);
		int num = text.LastIndexOf('.');
		if (num <= 0)
		{
			return text;
		}
		return text.Substring(0, num);
	}

	public static bool Auxiliary(string path)
	{
		string name = AutoGroupRules.Canonical(Stem(path));
		if (name.StartsWith("unins") || name.StartsWith("uninstall") || name.Contains("卸载") || name.EndsWith("updater") || name.Contains("crashreport") || name.Contains("crashpad"))
		{
			return true;
		}
		return new string[28]
		{
			"update", "updater", "autoupdate", "setup", "install", "installer", "crashreporter", "crashreportclient", "crashpadhandler", "cefsubprocess",
			"qtwebengineprocess", "unitycrashhandler64", "unitycrashhandler32", "werfault", "vc_redist", "vcredist", "dxsetup", "dxwebsetup", "dxdiag", "readme",
			"license", "help", "说明", "帮助", "repair", "修复", "reportbug", "feedback"
		}.Any((string x) => name == AutoGroupRules.Canonical(x) || name.StartsWith(AutoGroupRules.Canonical(x) + "64") || name.StartsWith(AutoGroupRules.Canonical(x) + "32"));
	}

	public static bool GameUri(string value)
	{
		value = (value ?? "").Trim().ToLowerInvariant();
		if (!value.StartsWith("steam://rungameid/") && !value.StartsWith("steam://run/") && !value.StartsWith("com.epicgames.launcher://apps/") && !value.StartsWith("uplay://launch/") && !value.StartsWith("origin://launchgame/"))
		{
			return value.StartsWith("battlenet://");
		}
		return true;
	}

	public static bool Candidate(string path)
	{
		if (!EntryRules.Importable(path) || Auxiliary(path))
		{
			return false;
		}
		if (!(Path.GetExtension(path).ToLowerInvariant() != ".url"))
		{
			return GameUri(ReadUrl(path));
		}
		return true;
	}

	/// <summary>
	/// 浏览器「应用化网页」的代理进程。这些站点快捷方式共用同一个
	/// msedge_proxy.exe / chrome_proxy.exe，去重时必须带上各自图标区分，
	/// 否则 BOOTH / X / YouTube 会被当成同一个程序合成一个。
	/// </summary>
	public static bool ProxyHost(string target)
	{
		if (string.IsNullOrWhiteSpace(target))
		{
			return false;
		}
		string leaf = AutoGroupRules.Canonical(Stem(target));
		return leaf == AutoGroupRules.Canonical("msedge_proxy") || leaf == AutoGroupRules.Canonical("chrome_proxy") || leaf == AutoGroupRules.Canonical("firefox");
	}

	/// <summary>
	/// 智能去重键：同一目录 + 名称首词相同 → 视为同一软件的不同入口
	/// （如 Git Bash / Git CMD / Git GUI，Cheat Engine 多个版本）。
	/// 文件夹、手动添加的条目返回 null，不参与合并。
	/// </summary>
	public static string SmartMergeKey(Entry entry)
	{
		if (entry == null || entry.IsFolder || entry.Manual)
		{
			return null;
		}
		string target = entry.LaunchTarget ?? entry.Path ?? "";
		string dir = "";
		try
		{
			dir = Path.GetDirectoryName(target) ?? "";
		}
		catch
		{
		}
		if (string.IsNullOrWhiteSpace(dir))
		{
			return null;
		}
		string name = (entry.Name ?? "").Trim();
		if (name.Length == 0)
		{
			return null;
		}
		string token = name.Split(new char[] { ' ', '\t', '_', '-' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
		token = AutoGroupRules.Canonical(token);
		if (token.Length < 2)
		{
			return null;
		}
		return dir.ToLowerInvariant() + "|" + token;
	}

	/// <summary>
	/// 只保留「真正能启动的程序」。
	/// 排除：打开文档/帮助的快捷方式、网页书签、批处理脚本、卸载程序、文件夹目标。
	/// 判据用解析后的启动目标，而不是快捷方式本身的后缀。
	/// </summary>
	public static bool RealApplication(Entry entry)
	{
		if (entry == null)
		{
			return false;
		}
		// 文件夹是合法条目（显示在"文件夹"分组），清理时不误删
		if (entry.IsFolder)
		{
			return true;
		}
		string target = entry.LaunchTarget ?? "";
		string ext = Path.GetExtension(target).ToLowerInvariant();
		// 目标为空：多半是「此电脑」「运行」这类 shell 内置项，属于可调动的系统程序，保留
		if (string.IsNullOrWhiteSpace(target))
		{
			return true;
		}
		// 目标是文件夹：作为文件夹条目保留（自动归入"文件夹"分组）
		if (Directory.Exists(target))
		{
			return true;
		}
		// 批处理/脚本不是主程序
		if (ext == ".bat" || ext == ".cmd" || ext == ".vbs" || ext == ".js" || ext == ".ps1")
		{
			return false;
		}
		// 网页书签 / 直接指向网址
		if (ext == ".url" || ext == ".html" || ext == ".htm" || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		// 帮助文档（.chm 等）和文档文件
		if (ext == ".chm" || ext == ".hlp" || ext == ".pdf" || ext == ".txt" || ext == ".md" || ext == ".doc" || ext == ".docx" || ext == ".rtf")
		{
			return false;
		}
		// 卸载程序：不是「可以调动的程序」
		if (IsUninstaller(target))
		{
			return false;
		}
		// 「用记事本/浏览器等打开某个文件」的快捷方式：目标是通用宿主程序，
		// 但带了一个指向文档的参数。这类不是应用本身。
		if (!string.IsNullOrWhiteSpace(entry.LaunchArguments) && OpensDocument(entry))
		{
			return false;
		}
		return true;
	}

	/// <summary>卸载程序识别：文件名或所在目录带 unins / uninstall / 卸载 / remover 等特征。</summary>
	private static bool IsUninstaller(string target)
	{
		if (string.IsNullOrWhiteSpace(target))
		{
			return false;
		}
		string leaf = AutoGroupRules.Canonical(Stem(target));
		if (leaf.StartsWith("unins") || leaf.StartsWith("uninstall") || leaf.StartsWith("unwise") || leaf.Contains("卸载") || leaf.Contains("uninst") || leaf.EndsWith("remover") || leaf.EndsWith("uninstaller"))
		{
			return true;
		}
		// msiexec /x {GUID} 也是卸载
		if (leaf == AutoGroupRules.Canonical("msiexec"))
		{
			return true;
		}
		return false;
	}

	/// <summary>通用宿主程序（记事本、写字板、浏览器等）带上文档参数时，判定为「打开文件」而非应用。</summary>
	private static bool OpensDocument(Entry entry)
	{
		string target = entry.LaunchTarget ?? "";
		string leaf = AutoGroupRules.Canonical(Stem(target));
		if (!new string[8] { "notepad", "wordpad", "mspaint", "explorer", "iexplore", "chrome", "msedge", "firefox" }
			.Any((string x) => leaf == AutoGroupRules.Canonical(x)))
		{
			return false;
		}
		string doc = FirstDocumentArgument(entry.LaunchArguments);
		if (doc == null)
		{
			return false;
		}
		string ext = Path.GetExtension(doc).ToLowerInvariant();
		return new string[15]
		{
			".txt", ".json", ".ini", ".cfg", ".conf", ".log", ".md", ".xml", ".yaml", ".yml", ".csv", ".html", ".htm", ".url", ".pdf"
		}.Contains(ext);
	}

	/// <summary>
	/// 从参数里取第一个「像文档路径」的部分。
	/// 参数可能是 "C:\Program Files\Cheat Engine\celua.txt" 这种带空格且带引号的路径，
	/// 所以不能简单地按空格切分。
	/// </summary>
	private static string FirstDocumentArgument(string arguments)
	{
		string text = (arguments ?? "").Trim();
		if (text.Length == 0)
		{
			return null;
		}
		// 引号包裹：直接取引号内整段
		if (text[0] == '"')
		{
			int num = text.IndexOf('"', 1);
			if (num > 1)
			{
				return Environment.ExpandEnvironmentVariables(text.Substring(1, num - 1).Trim());
			}
		}
		// 未加引号：整段就是一个路径时直接返回；否则取第一段
		if (File.Exists(Environment.ExpandEnvironmentVariables(text)))
		{
			return Environment.ExpandEnvironmentVariables(text);
		}
		return Environment.ExpandEnvironmentVariables(text.Split(new char[1] { ' ' })[0].Trim('"'));
	}

	/// <summary>
	/// 广告/推广书签。安装包经常往开始菜单塞厂商推广页，
	/// 例如「嗨格式数据恢复大师」的 Homepage 指向 huifu.hgs.cn，
	/// Python 的「Online Documentation」指向 docs.python.org。
	/// </summary>
	public static bool JunkBookmark(Entry entry)
	{
		if (entry == null)
		{
			return false;
		}
		string name = AutoGroupRules.Canonical(Stem(entry.Path ?? ""));
		if (name == "homepage" || name == "home page" || name == "官方网站" || name == "官网" || name == "产品主页" || name == "在线文档" || name == "online documentation")
		{
			return true;
		}
		string target = entry.LaunchTarget ?? "";
		if (Path.GetExtension(target).ToLowerInvariant() == ".url" && !GameUri(ReadUrl(target)))
		{
			return true;
		}
		// TargetPath 为空但 .lnk 内部藏着网址：书签而不是程序
		if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(entry.ShortcutUrl) && !GameUri(entry.ShortcutUrl))
		{
			return true;
		}
		return false;
	}

	/// <summary>
	/// 去重用的键。取「启动目标 + 参数」，这样：
	/// - 同一个程序在开始菜单和桌面各有一个快捷方式 → 键相同，合并；
	/// - BOOTH / X / YouTube 共用 chrome_proxy.exe 但 --app-id 不同 → 键不同，都保留。
	/// 注意：路径必须规范化后再比较，否则 D:\ 与 d:\ 会被当成两个不同程序。
	/// </summary>
	public static string DedupKey(Entry entry)
	{
		if (entry == null)
		{
			return "";
		}
		string target = (entry.LaunchTarget ?? "").Trim();
		string args = (entry.LaunchArguments ?? "").Trim();
		if (target.Length == 0)
		{
			// 没有目标（系统内置项/书签）时用入口路径兜底
			return NormalizeKey(entry.Path ?? "");
		}
		return NormalizeKey(target) + "|" + args;
	}

	/// <summary>统一大小写和分隔符，避免同一路径因写法不同而被当成两个。</summary>
	private static string NormalizeKey(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return "";
		}
		try
		{
			return Path.GetFullPath(path).TrimEnd('\\').ToLowerInvariant();
		}
		catch
		{
			return path.Trim().TrimEnd('\\').ToLowerInvariant();
		}
	}

	public static string ReadUrl(string path)
	{
		try
		{
			FileInfo fileInfo = new FileInfo(path);
			if (!fileInfo.Exists || fileInfo.Length > 32768)
			{
				return "";
			}
			foreach (string item in File.ReadLines(path))
			{
				string text = item.Trim();
				if (text.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
				{
					return text.Substring(4).Trim();
				}
			}
		}
		catch
		{
		}
		return "";
	}
}
