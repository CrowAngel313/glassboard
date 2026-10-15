using System;
using System.Runtime.InteropServices;

namespace GlassBoard;

public static class SystemMedia
{
	private struct MOUSEINPUT
	{
		public int X;

		public int Y;

		public uint Data;

		public uint Flags;

		public uint Time;

		public IntPtr Extra;
	}

	private struct KEYBDINPUT
	{
		public ushort Key;

		public ushort Scan;

		public uint Flags;

		public uint Time;

		public IntPtr Extra;
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct UNION
	{
		[FieldOffset(0)]
		public MOUSEINPUT Mouse;

		[FieldOffset(0)]
		public KEYBDINPUT Keyboard;
	}

	private struct INPUT
	{
		public uint Type;

		public UNION Value;
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint SendInput(uint count, INPUT[] input, int size);

	public static void Key(ushort key)
	{
		INPUT iNPUT = default(INPUT);
		iNPUT.Type = 1u;
		iNPUT.Value = new UNION
		{
			Keyboard = new KEYBDINPUT
			{
				Key = key
			}
		};
		INPUT iNPUT2 = iNPUT;
		iNPUT = default(INPUT);
		iNPUT.Type = 1u;
		iNPUT.Value = new UNION
		{
			Keyboard = new KEYBDINPUT
			{
				Key = key,
				Flags = 2u
			}
		};
		INPUT iNPUT3 = iNPUT;
		if (SendInput(2u, new INPUT[2] { iNPUT2, iNPUT3 }, Marshal.SizeOf(typeof(INPUT))) != 2)
		{
			throw new InvalidOperationException("无法发送系统媒体键");
		}
	}
}
