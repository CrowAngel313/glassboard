using System;
using System.Threading.Tasks;

namespace GlassBoard;

public sealed class WeatherEngine
{
	private readonly Controller C;

	private bool busy;

	private bool closed;

	private int serial;

	private string city = "";

	private DateTime next = DateTime.MinValue;

	public WeatherSnapshot Snapshot;

	public string Message = "设置城市后获取天气";

	public event Action Changed;

	public WeatherEngine(Controller c)
	{
		C = c;
	}

	public void Update(bool force = false)
	{
		if (!closed && C.Prefs.Widgets.Weather)
		{
			string text = C.Prefs.Widgets.City;
			if (text != city)
			{
				city = text;
				Snapshot = null;
				next = DateTime.MinValue;
				serial++;
			}
			if (!busy && (force || DateTime.UtcNow >= next))
			{
				Fetch();
			}
		}
	}

	private async void Fetch()
	{
		if (busy || closed)
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(city))
		{
			Message = "在组件设置中填写天气城市";
			next = DateTime.UtcNow.AddMinutes(30.0);
			Notify();
			return;
		}
		int version = serial;
		string requested = city;
		busy = true;
		Message = "正在查询天气…";
		Notify();
		try
		{
			WeatherSnapshot snapshot = await Task.Run(() => WeatherClient.Fetch(requested));
			if (!closed && version == serial && !(requested != city))
			{
				Snapshot = snapshot;
				Message = "更新于 " + DateTime.Now.ToString("HH:mm");
			}
		}
		catch (Exception ex)
		{
			if (!closed && version == serial)
			{
				Snapshot = null;
				Message = "天气暂不可用，点击刷新重试";
				Store.Log(ex);
			}
		}
		finally
		{
			busy = false;
			next = DateTime.UtcNow.AddMinutes(30.0);
			if (!closed)
			{
				Notify();
				if (requested != city)
				{
					Fetch();
				}
			}
		}
	}

	private void Notify()
	{
		if (!closed && this.Changed != null)
		{
			this.Changed();
		}
	}

	public void Stop()
	{
		closed = true;
		serial++;
		this.Changed = null;
	}
}
