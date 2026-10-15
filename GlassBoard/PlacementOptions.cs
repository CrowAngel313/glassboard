using System.Runtime.Serialization;

namespace GlassBoard;

[DataContract]
public class PlacementOptions
{
	[DataMember]
	public double? DockLeft;

	[DataMember]
	public double? DockTop;

	[DataMember]
	public double? PanelLeft;

	[DataMember]
	public double? PanelTop;

	[DataMember]
	public double? PanelWidth;

	[DataMember]
	public double? PanelHeight;

	[DataMember]
	public double? WidgetLeft;

	[DataMember]
	public double? WidgetTop;

	/// <summary>是否锁定 Dock 位置。默认不锁，用户可以随时拖动。</summary>
	[DataMember]
	public bool DockLocked;

	[DataMember]
	public bool PanelLocked;

	/// <summary>是否已把旧版默认的「锁定 Dock」迁移为可拖动，只做一次。</summary>
	[DataMember]
	public bool DockDragMigrated;

	[OnDeserializing]
	private void Defaults(StreamingContext context)
	{
		DockLocked = false;
		PanelLocked = false;
	}

	/// <summary>旧版默认锁定 Dock，导致用户无法拖动。升级时解锁一次，之后尊重用户自己的选择。</summary>
	public void MigrateDockDrag()
	{
		if (!DockDragMigrated)
		{
			DockLocked = false;
			DockDragMigrated = true;
		}
	}

	public PlacementOptions Copy()
	{
		return (PlacementOptions)MemberwiseClone();
	}

	public void Normalize()
	{
		MigrateDockDrag();
		DockLeft = Valid(DockLeft);
		DockTop = Valid(DockTop);
		PanelLeft = Valid(PanelLeft);
		PanelTop = Valid(PanelTop);
		WidgetLeft = Valid(WidgetLeft);
		WidgetTop = Valid(WidgetTop);
		PanelWidth = Valid(PanelWidth);
		PanelHeight = Valid(PanelHeight);
		if (PanelWidth < 320.0)
		{
			PanelWidth = null;
		}
		if (PanelHeight < 320.0)
		{
			PanelHeight = null;
		}
	}

	private static double? Valid(double? x)
	{
		if (!x.HasValue || (!double.IsNaN(x.Value) && !double.IsInfinity(x.Value)))
		{
			return x;
		}
		return null;
	}
}
