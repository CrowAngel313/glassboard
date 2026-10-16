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

			// 图标渲染冒烟测试：对每一个条目实际取图标并逐个计时，
			// 揪出会导致 UI 线程卡死的慢图标（如网络路径）。每条目测试前先打印，
			// 若真卡死，日志最后一行即为凶手。
			Stopwatch sw2 = Stopwatch.StartNew();
			int iconOk = 0;
			int slow = 0;
			foreach (Entry e in entries)
			{
				Console.WriteLine("TESTING:" + e.Name + " :: " + e.Path);
				Console.Out.Flush();
				Stopwatch sw3 = Stopwatch.StartNew();
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
				sw3.Stop();
				if (sw3.ElapsedMilliseconds > 3000)
				{
					Console.WriteLine("SLOW_ICON:" + sw3.ElapsedMilliseconds + "ms :: " + e.Name + " :: " + e.Path);
					slow++;
				}
				if (sw2.ElapsedMilliseconds > 180000)
				{
					Console.WriteLine("ICON_TIMEOUT:180s");
					break;
				}
			}
			Console.WriteLine("ICON_DONE:ok=" + iconOk + " slow=" + slow + " total=" + entries.Count + " ms=" + sw2.ElapsedMilliseconds);
			if (slow > 0)
			{
				Console.WriteLine("SCANTEST_FAIL:slow icons detected");
				return 1;
			}
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
