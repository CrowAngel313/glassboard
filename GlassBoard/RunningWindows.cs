using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GlassBoard;

public static class RunningWindows
{
	private delegate bool Callback(IntPtr window, IntPtr value);

	private static readonly Dictionary<string, List<IntPtr>> windows = new Dictionary<string, List<IntPtr>>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, string> targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(Callback callback, IntPtr value);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(IntPtr window);

	[DllImport("user32.dll")]
	private static extern bool IsIconic(IntPtr window);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr window, out uint id);

	[DllImport("user32.dll")]
	private static extern bool ShowWindowAsync(IntPtr window, int command);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	public static void Scan()
	{
		windows.Clear();
		Dictionary<uint, string> byPid = new Dictionary<uint, string>();
		int own = Process.GetCurrentProcess().Id;
		EnumWindows(delegate(IntPtr h, IntPtr d)
		{
			if (!IsWindowVisible(h))
			{
				return true;
			}
			GetWindowThreadProcessId(h, out var id);
			if (id == own)
			{
				return true;
			}
			if (!byPid.TryGetValue(id, out var value))
			{
				try
				{
					using Process process = Process.GetProcessById((int)id);
					value = process.MainModule.FileName;
				}
				catch
				{
					value = null;
				}
				byPid[id] = value;
			}
			if (value != null)
			{
				if (!windows.ContainsKey(value))
				{
					windows[value] = new List<IntPtr>();
				}
				windows[value].Add(h);
			}
			return true;
		}, IntPtr.Zero);
	}

	private static string Target(string path)
	{
		if (targets.ContainsKey(path))
		{
			return targets[path];
		}
		string text = path;
		if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
		{
			object obj = null;
			object obj2 = null;
			try
			{
				Type typeFromProgID = Type.GetTypeFromProgID("WScript.Shell");
				obj = Activator.CreateInstance(typeFromProgID);
				obj2 = typeFromProgID.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, obj, new object[1] { path });
				text = (string)obj2.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, obj2, null);
			}
			catch
			{
				text = null;
			}
			finally
			{
				if (obj2 != null && Marshal.IsComObject(obj2))
				{
					Marshal.ReleaseComObject(obj2);
				}
				if (obj != null && Marshal.IsComObject(obj))
				{
					Marshal.ReleaseComObject(obj);
				}
			}
		}
		targets[path] = text;
		return text;
	}

	public static bool IsRunning(Entry entry)
	{
		if (entry.IsFolder || Directory.Exists(entry.Path))
		{
			return false;
		}
		string text = Target(entry.Path);
		if (!string.IsNullOrWhiteSpace(text))
		{
			return windows.ContainsKey(text);
		}
		return false;
	}

	public static bool Activate(Entry entry)
	{
		Scan();
		string text = Target(entry.Path);
		if (string.IsNullOrWhiteSpace(text) || !windows.ContainsKey(text))
		{
			return false;
		}
		List<IntPtr> list = windows[text];
		IntPtr foregroundWindow = GetForegroundWindow();
		int num = list.IndexOf(foregroundWindow);
		IntPtr intPtr = list[(num + 1) % list.Count];
		if (IsIconic(intPtr))
		{
			ShowWindowAsync(intPtr, 9);
		}
		Native.SetForegroundWindow(intPtr);
		return true;
	}
}
