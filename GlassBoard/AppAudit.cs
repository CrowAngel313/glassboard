using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GlassBoard;

/// <summary>
/// 诊断用：按「只要主程序」的规则重新分类当前配置里的所有入口，
/// 报告哪些会被过滤、哪些重复、最终保留多少。
/// 用法：GlassBoard.exe --audit-apps
/// </summary>
public static class AppAudit
{
	public static int Run()
	{
		Preferences prefs = Store.Load();
		LaunchInfoService.BackfillShortcutIcons(prefs);
		string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app-audit.txt");
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("=== 全部 " + prefs.Apps.Count + " 个入口的分类结果 ===");
		List<Entry> kept = new List<Entry>();
		List<string> dropped = new List<string>();
		foreach (Entry a in prefs.Apps)
		{
			// 老配置可能还没回填，这里现读一遍保证判定准确
			LaunchInfoService.Enrich(a);
			string reason = null;
			if (!StartupEntryRules.RealApplication(a))
			{
				reason = "非主程序";
				string ext = Path.GetExtension(a.LaunchTarget ?? "").ToLowerInvariant();
				if (ext == ".bat" || ext == ".cmd")
				{
					reason = "批处理 " + ext;
				}
				else if (ext == ".url")
				{
					reason = "网页书签";
				}
				else if (!string.IsNullOrWhiteSpace(a.LaunchArguments))
				{
					reason = "打开文档（" + Path.GetFileName(a.LaunchTarget) + " + 参数）";
				}
			}
			else if (StartupEntryRules.JunkBookmark(a))
			{
				reason = "广告/文档书签" + (string.IsNullOrWhiteSpace(a.ShortcutUrl) ? "" : " → " + a.ShortcutUrl);
			}
			if (reason == null)
			{
				kept.Add(a);
				sb.AppendLine("  KEEP   " + a.Name);
			}
			else
			{
				dropped.Add(a.Name);
				sb.AppendLine("  DROP   " + a.Name + "   [" + reason + "]");
			}
		}

		sb.AppendLine();
		sb.AppendLine("=== 去重（同一目标+参数只留一个）===");
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<Entry> final = new List<Entry>();
		foreach (Entry a in kept)
		{
			string key = StartupEntryRules.DedupKey(a);
			if (!seen.Add(key))
			{
				sb.AppendLine("  DUP    " + a.Name + "   [" + key + "]");
				continue;
			}
			final.Add(a);
		}

		sb.AppendLine();
		sb.AppendLine("过滤掉 " + dropped.Count + " 个，去重 " + (kept.Count - final.Count) + " 个");
		sb.AppendLine("最终保留 " + final.Count + " 个：");
		foreach (Entry a in final)
		{
			sb.AppendLine("  " + a.Name);
		}

		// 顺手报告书签字段识别情况，确认 .lnk 内嵌 URL 能读到
		sb.AppendLine();
		sb.AppendLine("=== 读到的快捷方式自带图标 / 内嵌网址 ===");
		foreach (Entry a in prefs.Apps)
		{
			if (!string.IsNullOrWhiteSpace(a.ShortcutIcon) || !string.IsNullOrWhiteSpace(a.ShortcutUrl))
			{
				sb.AppendLine("  " + a.Name + "  icon=" + (a.ShortcutIcon ?? "-") + "  url=" + (a.ShortcutUrl ?? "-"));
			}
		}

		File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
		return 0;
	}
}
