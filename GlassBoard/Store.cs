using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;

namespace GlassBoard;

public static class Store
{
	public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassBoard");

	public static readonly string FileName = Path.Combine(Folder, "layout.json");

	public static Preferences Load()
	{
		Directory.CreateDirectory(Folder);
		if (!File.Exists(FileName))
		{
			return new Preferences();
		}
		try
		{
			using FileStream stream = File.OpenRead(FileName);
			Preferences obj = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject((Stream)stream);
			obj.Normalize();
			return obj;
		}
		catch (Exception ex)
		{
			Log(ex);
			try
			{
				File.Copy(FileName, FileName + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: false);
			}
			catch
			{
			}
			return new Preferences();
		}
	}

	public static void Save(Preferences p)
	{
		Directory.CreateDirectory(Folder);
		string text = FileName + ".tmp";
		using (FileStream stream = File.Create(text))
		{
			new DataContractJsonSerializer(typeof(Preferences)).WriteObject((Stream)stream, (object)p);
		}
		if (File.Exists(FileName))
		{
			File.Replace(text, FileName, null);
		}
		else
		{
			File.Move(text, FileName);
		}
	}

	public static void Trace(string message)
	{
		try
		{
			Directory.CreateDirectory(Folder);
			string text = Path.Combine(Folder, "session.log");
			if (File.Exists(text) && new FileInfo(text).Length > 1048576)
			{
				File.Move(text, text + "." + DateTime.Now.Ticks);
			}
			File.AppendAllText(text, DateTime.Now.ToString("s") + " [" + Assembly.GetExecutingAssembly().GetName().Version?.ToString() + "] " + message + Environment.NewLine);
		}
		catch
		{
		}
	}

	public static void Log(Exception ex)
	{
		try
		{
			Directory.CreateDirectory(Folder);
			File.AppendAllText(Path.Combine(Folder, "error.log"), DateTime.Now.ToString("s") + " [" + Assembly.GetExecutingAssembly().GetName().Version?.ToString() + "] " + ex?.ToString() + Environment.NewLine);
		}
		catch
		{
		}
	}
}
