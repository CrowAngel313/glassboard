using System;
using System.Runtime.Serialization;

namespace GlassBoard;

[DataContract]
public class DockOptions
{
	[DataMember]
	public double IconSize = 44.0;

	[DataMember]
	public double Spacing = 6.0;

	[DataMember]
	public double Padding = 16.0;

	[DataMember]
	public double Radius = 28.0;

	[DataMember]
	public double OpacityPercent = 72.0;

	[DataMember]
	public double Zoom = 1.2;

	[DataMember]
	public double Offset;

	[DataMember]
	public double MotionLevel = 1.0;

	[DataMember]
	public int Alignment = 1;

	[DataMember]
	public int Accent;

	[DataMember]
	public int MaxPinned = 9;

	[DataMember]
	public int ComputerStyle;

	/// <summary>停靠边：0=底部，1=左侧，2=右侧。非底部时 Dock 竖向排列。</summary>
	[DataMember]
	public int Edge;

	/// <summary>底板样式：0=玻璃，1=纯透明（不显示任何底板）。</summary>
	[DataMember]
	public int Background = 1;

	[DataMember]
	public bool ShowComputer = true;

	[DataMember]
	public bool ShowLabels;

	[DataMember]
	public bool ShowSeparator = true;

	[DataMember]
	public bool AlwaysOnTop;

	[DataMember]
	public bool MotionEnabled = true;

	[DataMember]
	public bool AutoHide;

	[DataMember]
	public bool ShowRunning = true;

	[DataMember]
	public double HideDelay = 900.0;

	[DataMember]
	public double EdgeRange = 12.0;

	[DataMember]
	public double BlurRadius = 18.0;

	[DataMember]
	public double EdgeStrength = 1.0;

	[OnDeserializing]
	private void Defaults(StreamingContext context)
	{
		IconSize = 44.0;
		Spacing = 6.0;
		Padding = 16.0;
		Radius = 28.0;
		OpacityPercent = 72.0;
		Zoom = 1.2;
		Offset = 0.0;
		MotionLevel = 1.0;
		Alignment = 1;
		Accent = 0;
		MaxPinned = 9;
		ComputerStyle = 0;
		Edge = 0;
		Background = 1;
		ShowComputer = true;
		ShowLabels = false;
		ShowSeparator = true;
		AlwaysOnTop = false;
		MotionEnabled = true;
		AutoHide = false;
		ShowRunning = true;
		HideDelay = 900.0;
		EdgeRange = 12.0;
		BlurRadius = 18.0;
		EdgeStrength = 1.0;
	}

	public DockOptions Copy()
	{
		return (DockOptions)MemberwiseClone();
	}

	/// <summary>是否贴左侧或右侧竖向停靠。</summary>
	public bool Vertical => Edge != 0;

	/// <summary>是否不显示底板。</summary>
	public bool Transparent => Background == 1;

	public void Normalize()
	{
		IconSize = LayoutRules.Clamp(IconSize, 32.0, 64.0, 44.0);
		Spacing = LayoutRules.Clamp(Spacing, 0.0, 20.0, 6.0);
		Padding = LayoutRules.Clamp(Padding, 8.0, 28.0, 16.0);
		Radius = LayoutRules.Clamp(Radius, 0.0, 48.0, 28.0);
		OpacityPercent = LayoutRules.GlassOpacity(OpacityPercent);
		Zoom = LayoutRules.Clamp(Zoom, 1.0, 1.6, 1.2);
		Offset = LayoutRules.Clamp(Offset, -500.0, 500.0, 0.0);
		MotionLevel = LayoutRules.Clamp(MotionLevel, 0.5, 1.5, 1.0);
		HideDelay = LayoutRules.Clamp(HideDelay, 200.0, 3000.0, 900.0);
		EdgeRange = LayoutRules.Clamp(EdgeRange, 3.0, 48.0, 12.0);
		BlurRadius = LayoutRules.Clamp(BlurRadius, 0.0, 40.0, 18.0);
		EdgeStrength = LayoutRules.Clamp(EdgeStrength, 0.2, 2.0, 1.0);
		Alignment = Math.Max(0, Math.Min(2, Alignment));
		Accent = Math.Max(0, Math.Min(5, Accent));
		MaxPinned = Math.Max(3, Math.Min(20, MaxPinned));
		ComputerStyle = Math.Max(0, Math.Min(1, ComputerStyle));
		Edge = Math.Max(0, Math.Min(2, Edge));
		Background = Math.Max(0, Math.Min(1, Background));
	}

	/// <summary>沿 Dock 长边能放下的固定图标数量。availableExtent 为可用宽度（底部）或高度（侧边）。</summary>
	public int VisiblePinned(double availableExtent, int count)
	{
		int num = 2 + (ShowComputer ? 1 : 0);
		double num2 = IconSize + 16.0 + Spacing;
		int val = Math.Max(0, (int)Math.Floor((availableExtent - Ring - HoverPadding() * 2.0 - (double)(ShowSeparator ? 17 : 0)) / num2) - num);
		return Math.Min(count, Math.Min(MaxPinned, val));
	}

	public double HoverPadding()
	{
		return Math.Max(Padding, (IconSize + 16.0) * (Math.Min(1.6, 1.0 + (Zoom - 1.0) * MotionLevel) - 1.0) / 2.0 + 2.0);
	}

	/// <summary>玻璃底板四周的留白（含投影）。纯透明模式不需要，直接贴紧图标。</summary>
	public double Ring => (Transparent ? 0.0 : 28.0);

	/// <summary>垂直于 Dock 轴向的厚度：底部停靠时为高度，侧边停靠时为宽度。</summary>
	public double ThicknessFor()
	{
		double num = IconSize + 16.0 + (double)(ShowLabels ? 18 : 0);
		double num2 = Math.Min(1.6, 1.0 + (Zoom - 1.0) * MotionLevel);
		return Ring + num + Padding * 2.0 + Math.Max(0.0, num * (num2 - 1.0) * 0.65 + 5.0 * MotionLevel - Padding) * 2.0;
	}

	/// <summary>沿 Dock 轴向的长度：底部停靠时为宽度，侧边停靠时为高度。</summary>
	public double LengthFor(int pinnedCount)
	{
		return Ring + HoverPadding() * 2.0 + (double)(2 + (ShowComputer ? 1 : 0) + pinnedCount) * (IconSize + 16.0 + Spacing) + (double)(ShowSeparator ? 17 : 0);
	}

	public double WidthFor(int pinnedCount)
	{
		return Vertical ? ThicknessFor() : LengthFor(pinnedCount);
	}

	public double HeightFor(int pinnedCount)
	{
		return Vertical ? LengthFor(pinnedCount) : ThicknessFor();
	}
}
