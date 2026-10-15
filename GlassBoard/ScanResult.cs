using System.Collections.Generic;

namespace GlassBoard;

public class ScanResult
{
	public List<string> Paths = new List<string>();

	public int Skipped;

	public bool Limited;

	/// <summary>被判定为非应用而过滤掉的个数（网页书签、批处理、打开文档的快捷方式）。</summary>
	public int Filtered;

	/// <summary>因指向同一程序而被去重的个数。</summary>
	public int Duplicates;
}
