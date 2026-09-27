using System.Net.Http.Json;
using System.Text.Json;

namespace EriiPet.Core.Models;

/// <summary>天气服务 — Open-Meteo 免费 API（无需 Key）</summary>
public static class WeatherService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>获取当前天气摘要</summary>
    public static async Task<WeatherInfo?> GetCurrentAsync(double lat, double lon)
    {
        try
        {
            var url = $"https://api.open-meteo.com/v1/forecast" +
                $"?latitude={lat}&longitude={lon}" +
                $"&current=temperature_2m,weather_code,relative_humidity_2m" +
                $"&timezone=Asia%2FShanghai";

            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            var current = doc.RootElement.GetProperty("current");
            var temp = current.GetProperty("temperature_2m").GetSingle();
            var code = current.GetProperty("weather_code").GetInt32();
            var humidity = current.GetProperty("relative_humidity_2m").GetInt32();

            return new WeatherInfo
            {
                Temperature = temp,
                WeatherCode = code,
                Humidity = humidity,
                Description = CodeToDescription(code),
                Emoji = CodeToEmoji(code),
            };
        }
        catch { return null; }
    }

    /// <summary>根据 IP 获取大致位置（免费，无需 Key）</summary>
    public static async Task<(double lat, double lon)?> GetLocationByIpAsync()
    {
        try
        {
            var json = await _http.GetStringAsync("http://ip-api.com/json/?fields=lat,lon");
            using var doc = JsonDocument.Parse(json);
            return (doc.RootElement.GetProperty("lat").GetDouble(),
                    doc.RootElement.GetProperty("lon").GetDouble());
        }
        catch { return null; }
    }

    private static string CodeToDescription(int code) => code switch
    {
        0 => "晴天", 1 or 2 or 3 => "多云",
        45 or 48 => "雾", 51 or 53 or 55 => "小雨",
        61 or 63 or 65 => "雨", 71 or 73 or 75 => "雪",
        80 or 81 or 82 => "阵雨", 95 or 96 or 99 => "雷暴",
        _ => "天气"
    };

    private static string CodeToEmoji(int code) => code switch
    {
        0 => "☀️", 1 or 2 or 3 => "⛅",
        45 or 48 => "🌫️", 51 or 53 or 55 => "🌧️",
        61 or 63 or 65 => "🌧️", 71 or 73 or 75 => "❄️",
        80 or 81 or 82 => "🌦️", 95 or 96 or 99 => "⛈️",
        _ => "🌈"
    };
}

public class WeatherInfo
{
    public float Temperature { get; set; }
    public int WeatherCode { get; set; }
    public int Humidity { get; set; }
    public string Description { get; set; } = "";
    public string Emoji { get; set; } = "";
}
