using System;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlassBoard;

/// <summary>
/// 诊断用：逐个入口解析图标，报告是否拿到、拿到多大。
/// 用法：GlassBoard.exe --dump-icons
/// </summary>
public static class IconDump
{
	public static int Run()
	{
		Preferences prefs = Store.Load();
		LaunchInfoService.BackfillShortcutIcons(prefs);
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("name\tpixels\ttype\ticonPath\tlaunchTarget\tpath");
		int nullCount = 0;
		foreach (Entry a in prefs.Apps)
		{
			ImageSource src = FeatureUi.Icon(a);
			BitmapSource bs = src as BitmapSource;
			string px = ((bs == null) ? "-" : (bs.PixelWidth + "x" + bs.PixelHeight));
			string type = ((src == null) ? "NULL" : src.GetType().Name);
			if (src == null)
			{
				nullCount++;
			}
			sb.AppendLine(string.Join("\t", new string[6]
			{
				a.Name ?? "",
				px,
				type,
				a.IconPath ?? "",
				a.LaunchTarget ?? "",
				a.Path ?? ""
			}));
		}
		sb.AppendLine();
		sb.AppendLine("TOTAL=" + prefs.Apps.Count + "  NULL=" + nullCount);
		string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon-dump.txt");
		File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);

		// 把每个图标按面板尺寸编码成 PNG，用来判断「元数据正常但像素已失效」的情况。
		string pngDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon-dump-png");
		Directory.CreateDirectory(pngDir);
		string rawDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon-dump-raw");
		Directory.CreateDirectory(rawDir);
		int index = 0;
		foreach (Entry a in prefs.Apps)
		{
			index++;
			ImageSource src = FeatureUi.Icon(a);
			BitmapSource bs = src as BitmapSource;
			if (bs == null)
			{
				continue;
			}
			string safe = string.Join("_", (a.Name ?? "app").Split(Path.GetInvalidFileNameChars()));
			string stem = index.ToString("D3") + "_" + safe;
			try
			{
				// 原始像素（不做任何缩放）
				PngBitmapEncoder rawEnc = new PngBitmapEncoder();
				rawEnc.Frames.Add(BitmapFrame.Create(bs));
				using (FileStream rfs = File.Create(Path.Combine(rawDir, stem + ".png")))
				{
					rawEnc.Save(rfs);
				}

				// 报告原始像素里不透明内容的包围盒，判断图标是不是只画在一角
				FormatConvertedBitmap conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0.0);
				int w = conv.PixelWidth;
				int h = conv.PixelHeight;
				int stride = w * 4;
				byte[] buf = new byte[stride * h];
				conv.CopyPixels(buf, stride, 0);
				int minX = w, minY = h, maxX = -1, maxY = -1;
				for (int y = 0; y < h; y++)
				{
					int row = y * stride;
					for (int x = 0; x < w; x++)
					{
						if (buf[row + x * 4 + 3] > 8)
						{
							if (x < minX) minX = x;
							if (x > maxX) maxX = x;
							if (y < minY) minY = y;
							if (y > maxY) maxY = y;
						}
					}
				}
				string bbox = ((maxX < 0) ? "EMPTY" : ("(" + minX + "," + minY + ")-(" + maxX + "," + maxY + ")"));
				sb.AppendLine("BBOX " + stem + " src=" + w + "x" + h + " ink=" + bbox);

				// 按面板显示尺寸缩放，和真实渲染一致
				int size = 50;
				DrawingVisual visual = new DrawingVisual();
				using (DrawingContext dc = visual.RenderOpen())
				{
					dc.DrawImage(bs, new System.Windows.Rect(0, 0, size, size));
				}
				RenderTargetBitmap rtb = new RenderTargetBitmap(size, size, 96.0, 96.0, PixelFormats.Pbgra32);
				rtb.Render(visual);
				PngBitmapEncoder enc = new PngBitmapEncoder();
				enc.Frames.Add(BitmapFrame.Create(rtb));
				using FileStream fs = File.Create(Path.Combine(pngDir, stem + ".png"));
				enc.Save(fs);
			}
			catch (Exception ex)
			{
				sb.AppendLine("PNG FAIL " + a.Name + " :: " + ex.Message);
			}
		}
		File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
		return 0;
	}
}
