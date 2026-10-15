using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Json;

namespace GlassBoard;

public static class WeatherClient
{
	public static T Read<T>(Stream stream)
	{
		return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
	}

	private static T Get<T>(string url)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		HttpWebRequest obj = (HttpWebRequest)WebRequest.Create(url);
		obj.Timeout = 8000;
		obj.ReadWriteTimeout = 8000;
		obj.UserAgent = "GlassBoard/0.6";
		using WebResponse webResponse = obj.GetResponse();
		using Stream stream = webResponse.GetResponseStream();
		return Read<T>(stream);
	}

	public static WeatherSnapshot Fetch(string city)
	{
		if (string.IsNullOrWhiteSpace(city))
		{
			throw new ArgumentException("请先在设置中填写天气城市");
		}
		GeoReply geoReply = Get<GeoReply>("https://geocoding-api.open-meteo.com/v1/search?count=1&language=zh&format=json&name=" + Uri.EscapeDataString(city));
		if (geoReply.Results == null || geoReply.Results.Count == 0)
		{
			throw new InvalidOperationException("未找到城市，可尝试英文名称");
		}
		GeoPlace geoPlace = geoReply.Results[0];
		string text = geoPlace.Latitude.ToString(CultureInfo.InvariantCulture);
		string text2 = geoPlace.Longitude.ToString(CultureInfo.InvariantCulture);
		WeatherReply weatherReply = Get<WeatherReply>("https://api.open-meteo.com/v1/forecast?latitude=" + text + "&longitude=" + text2 + "&current=temperature_2m,weather_code,wind_speed_10m&timezone=auto");
		if (weatherReply.Current == null)
		{
			throw new InvalidOperationException("天气服务没有返回当前数据");
		}
		return new WeatherSnapshot
		{
			Place = geoPlace,
			Current = weatherReply.Current
		};
	}
}
