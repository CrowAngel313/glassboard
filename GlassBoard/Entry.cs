using System.Collections.Generic;
using System.Runtime.Serialization;

namespace GlassBoard;

[DataContract]
public class Entry
{
	[DataMember]
	public string Name;

	[DataMember]
	public string Path;

	[DataMember]
	public bool Pinned;

	[DataMember(EmitDefaultValue = false)]
	public bool IsFolder;

	[DataMember(EmitDefaultValue = false)]
	public string Group;

	[DataMember(EmitDefaultValue = false)]
	public List<string> ExtraGroups;

	[DataMember(EmitDefaultValue = false)]
	public string IconPath;

	/// <summary>
	/// 快捷方式自带的 IconLocation（只读，来自 .lnk 本身）。
	/// 与用户手改的 IconPath 分开存：IconPath 优先级更高，这个是系统给的兜底。
	/// Edge/Chrome 的「应用化网页」快捷方式目标是没有图标的 msedge_proxy.exe /
	/// chrome_proxy.exe，真正的图标只在这里，不读就会显示成一张空白纸。
	/// </summary>
	[DataMember(EmitDefaultValue = false)]
	public string ShortcutIcon;

	/// <summary>
	/// 快捷方式内部指向的网址（只读）。
	/// 有些安装包把「在线文档」「厂商主页」做成 .lnk，但 TargetPath 是空的，
	/// 网址藏在 .lnk 内容里。这类是书签而不是程序，靠这个字段识别。
	/// </summary>
	[DataMember(EmitDefaultValue = false)]
	public string ShortcutUrl;

	[DataMember(EmitDefaultValue = false)]
	public string LaunchTarget;

	[DataMember(EmitDefaultValue = false)]
	public string LaunchArguments;

	[DataMember(EmitDefaultValue = false)]
	public string ProductInfo;

	/// <summary>
	/// 是否用户手动添加。手动添加的不参与智能去重合并。
	/// </summary>
	[DataMember(EmitDefaultValue = false)]
	public bool Manual;
}
