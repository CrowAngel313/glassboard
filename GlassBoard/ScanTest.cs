using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace GlassBoard;

/// <summary>
/// 云端诊断用：模拟首次导入的完整扫描流程（扫描→Enrich→Primary→智能去重），
/// 输出统计与耗时，用于验证修改在 Windows 上的实际行为。
/// 用法：GlassBoard.exe --scan-test
/// </summary>
public static class ScanTest
{
	public static int Run()
	{
		try
		{
			Stopwatch sw = Stopwatch.StartNew();
			string[] roots = new string[]
			{
				Environment.GetFolderPath(Environment.SpecialFolder.Programs),
				Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
				Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
				Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
			};
			using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
			ScanResult result = ImportScanner.Scan(roots, true, cts.Token);
			Console.WriteLine("SCAN_PATHS:" + result.Paths.Count + " limited=" + result.Limited + " skipped=" + result.Skipped + " ms=" + sw.ElapsedMilliseconds);

			int folders = 0;
			int kept = 0;
			List<Entry> entries = new List<Entry>();
			foreach (string p in result.Paths)
			{
				try
				{
					Entry e = LaunchInfoService.Read(p);
					if (e.IsFolder)
					{
						folders++;
					}
					if (LaunchInfoService.Primary(e))
					{
						kept++;
						entries.Add(e);
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine("ENRICH_ERR:" + p + " :: " + ex.Message);
				}
			}
			Console.WriteLine("ENRICH_DONE:kept=" + kept + " folders=" + folders + " ms=" + sw.ElapsedMilliseconds);

			Dictionary<string, List<Entry>> groups = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
			foreach (Entry e in entries)
			{
				string k = StartupEntryRules.SmartMergeKey(e);
				if (k == null)
				{
					continue;
				}
				if (!groups.TryGetValue(k, out List<Entry> l))
				{
					l = new List<Entry>();
					groups[k] = l;
				}
				l.Add(e);
			}
			var dupGroups = groups.Values.Where((List<Entry> l) => l.Count > 1).Take(15).ToList();
			Console.WriteLine("DEDUP:dupGroups=" + groups.Values.Count((List<Entry> l) => l.Count > 1) + " mergedEntries=" + dupGroups.Sum((List<Entry> l) => l.Count - 1));
			foreach (List<Entry> g in dupGroups)
			{
				Console.WriteLine("  DUP: " + string.Join(" | ", g.Select((Entry e) => e.Name)));
			}

			// 图标路径冒烟测试：对前 30 个条目取图标（带驱动器保护，不应卡死）
			Stopwatch sw2 = Stopwatch.StartNew();
			int iconOk = 0;
			foreach (Entry e in entries.Take(30))
			{
				try
				{
					if (FeatureUi.Icon(e) != null)
					{
						iconOk++;
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine("ICON_ERR:" + e.Name + " :: " + ex.Message);
				}
				if (sw2.ElapsedMilliseconds > 60000)
				{
					Console.WriteLine("ICON_TIMEOUT:60s");
					break;
				}
			}
			Console.WriteLine("ICON_DONE:ok=" + iconOk + " ms=" + sw2.ElapsedMilliseconds);
			Console.WriteLine("SCANTEST_OK totalMs=" + sw.ElapsedMilliseconds);
			return 0;
		}
		catch (Exception ex)
		{
			Console.WriteLine("SCANTEST_FAIL:" + ex);
			return 1;
		}
	}
}
