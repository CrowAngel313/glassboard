using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace GlassBoard;

[DataContract]
public class Preferences
{
	[DataMember]
	public List<Entry> Apps = new List<Entry>();

	[DataMember]
	public bool Light;

	[DataMember]
	public double DockGap = 12.0;

	[DataMember]
	public bool Imported;

	[DataMember]
	public int PanelSize = 1;

	[DataMember]
	public double BoardIconSize = 48.0;

	[DataMember]
	public double BoardIconGap = 5.0;

	[DataMember(EmitDefaultValue = false)]
	public double? OpacityPercent = 72.0;

	[DataMember(EmitDefaultValue = false)]
	public List<string> Removed = new List<string>();

	[DataMember(EmitDefaultValue = false)]
	public List<HotkeySetting> Hotkeys = HotkeyRules.Defaults();

	[DataMember(EmitDefaultValue = false)]
	public DockOptions Dock = new DockOptions();

	[DataMember(EmitDefaultValue = false)]
	public List<string> Groups = new List<string> { "创作", "办公", "游戏" };

	[DataMember(EmitDefaultValue = false)]
	public List<Workspace> Workspaces = new List<Workspace>();

	[DataMember(EmitDefaultValue = false)]
	public PlacementOptions Placement = new PlacementOptions();

	[DataMember(EmitDefaultValue = false)]
	public WidgetOptions Widgets = new WidgetOptions();

	[DataMember]
	public bool AutoGrouping = true;

	/// <summary>
	/// 是否显示「未分组」分类。关掉后只留「全部」，界面更清爽。
	/// 注意：不能加 EmitDefaultValue = false —— bool 的 CLR 默认值是 false，
	/// 加了之后「隐藏」这个状态会被序列化器省略掉，设置就存不住。
	/// </summary>
	[DataMember]
	public bool ShowUngrouped = true;

	/// <summary>
	/// 是否已回填过快捷方式自带的图标（只做一次）。
	/// 老配置里的条目没有 ShortcutIcon 字段，升级后要补读一遍，
	/// 否则 Edge/Chrome「应用化网页」那类快捷方式会一直显示成空白纸。
	/// </summary>
	[DataMember]
	public bool ShortcutIconBackfilled;

	[DataMember]
	public Dictionary<string, string> LearnedGroups = new Dictionary<string, string>();

	public bool HotkeyMigrationConflict;

	[OnDeserializing]
	private void Defaults(StreamingContext context)
	{
		AutoGrouping = true;
		BoardIconSize = 48.0;
		BoardIconGap = 5.0;
		// DataContract 反序列化不会执行字段初始化器，缺省值必须在这里兜底。
		ShowUngrouped = true;
	}

	public void Normalize()
	{
		if (LearnedGroups == null)
		{
			LearnedGroups = new Dictionary<string, string>();
		}
		if (Apps == null)
		{
			Apps = new List<Entry>();
		}
		Apps = Apps.Where((Entry a) => a != null && !string.IsNullOrWhiteSpace(a.Path)).ToList();
		foreach (Entry app in Apps)
		{
			if (string.IsNullOrWhiteSpace(app.Name))
			{
				app.Name = Path.GetFileNameWithoutExtension(app.Path);
			}
			FeatureRules.NormalizeMemberships(app);
		}
		Apps = EntryRules.NormalizeEntries(Apps);
		BoardIconSize = LayoutRules.Clamp(BoardIconSize, 28.0, 80.0, 48.0);
		BoardIconGap = LayoutRules.Clamp(BoardIconGap, 0.0, 20.0, 5.0);
		DockGap = LayoutRules.Clamp(DockGap, 0.0, 160.0, 12.0);
		PanelSize = Math.Max(0, Math.Min(2, PanelSize));
		OpacityPercent = LayoutRules.GlassOpacity(OpacityPercent.GetValueOrDefault(72.0));
		if (Removed == null)
		{
			Removed = new List<string>();
		}
		bool flag = Hotkeys != null && Hotkeys.All((HotkeySetting k) => k == null || k.ActionId != 7);
		Hotkeys = HotkeyRules.Normalize(Hotkeys);
		HotkeyMigrationConflict = flag && !Hotkeys[6].Enabled;
		if (Dock == null)
		{
			Dock = new DockOptions
			{
				OpacityPercent = OpacityPercent.Value
			};
		}
		Dock.Normalize();
		Groups = FeatureRules.Groups(Groups, Apps);
		if (Workspaces == null)
		{
			Workspaces = new List<Workspace>();
		}
		Workspaces = Workspaces.Where((Workspace x) => x != null && !string.IsNullOrWhiteSpace(x.Name)).ToList();
		foreach (Workspace workspace in Workspaces)
		{
			workspace.Normalize();
		}
		if (Placement == null)
		{
			Placement = new PlacementOptions();
		}
		Placement.Normalize();
		if (Widgets == null)
		{
			Widgets = new WidgetOptions();
		}
		Widgets.Normalize();
	}
}
