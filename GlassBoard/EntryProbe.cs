using System;
using System.IO;
using System.Linq;
using System.Text;

namespace GlassBoard;

/// <summary>诊断用：打印指定名字的条目的完整字段，定位为什么没被去重。</summary>
public static class EntryProbe
{
	public static int Run(string keyword)
	{
		Preferences prefs = Store.Load();
		StringBuilder sb = new StringBuilder();
		foreach (Entry a in prefs.Apps)
		{
			if (a.Name == null || !a.Name.Contains(keyword))
			{
				continue;
			}
			LaunchInfoService.Enrich(a);
			sb.AppendLine("NAME   = " + a.Name);
			sb.AppendLine("  Path        = " + a.Path);
			sb.AppendLine("  LaunchTarget= " + a.LaunchTarget);
			sb.AppendLine("  Arguments   = " + a.LaunchArguments);
			sb.AppendLine("  IconPath    = " + a.IconPath);
			sb.AppendLine("  ShortcutIcon= " + a.ShortcutIcon);
			sb.AppendLine("  DedupKey    = " + StartupEntryRules.DedupKey(a));
			sb.AppendLine("  RealApp     = " + StartupEntryRules.RealApplication(a));
			sb.AppendLine("  Junk        = " + StartupEntryRules.JunkBookmark(a));
			sb.AppendLine();
		}
		File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "entry-probe.txt"), sb.ToString(), Encoding.UTF8);
		return 0;
	}
}
