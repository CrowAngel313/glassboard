using System.Reflection;
using Microsoft.Win32;

namespace GlassBoard;

public static class StartupService
{
	private const string Key = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string Approved = "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run";

	private const string Name = "GlassBoard";

	public static bool Enabled
	{
		get
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
			using RegistryKey registryKey2 = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run");
			return StartupRules.Enabled((registryKey == null) ? null : (registryKey.GetValue("GlassBoard") as string), (registryKey2 == null) ? null : (registryKey2.GetValue("GlassBoard") as byte[]));
		}
	}

	public static string Target
	{
		get
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
			return (registryKey == null) ? null : (registryKey.GetValue("GlassBoard") as string);
		}
	}

	public static void Set(bool enabled)
	{
		using (RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
		{
			if (enabled)
			{
				registryKey.SetValue("GlassBoard", StartupRules.Command(Assembly.GetExecutingAssembly().Location), RegistryValueKind.String);
			}
			else
			{
				registryKey.DeleteValue("GlassBoard", throwOnMissingValue: false);
			}
		}
		using RegistryKey registryKey2 = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run", writable: true);
		registryKey2?.DeleteValue("GlassBoard", throwOnMissingValue: false);
	}
}
