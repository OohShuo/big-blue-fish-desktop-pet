using System.Globalization;
using System.Text.Json;

namespace BigBlueFish.Agent;

public sealed record WeatherLocation(string Label, double Latitude, double Longitude);

public sealed record WeatherSnapshot(
    string Label, double Latitude, double Longitude, DateTimeOffset ObservedAt,
    double Temperature, double Apparent, double Humidity, int WeatherCode,
    double Precipitation, double WindSpeed, int DailyCode,
    double? Maximum, double? Minimum, int? PrecipitationProbability)
{
    public string Summary() =>
        $"{Label}：{WeatherService.Description(WeatherCode)}，{Temperature:0.#}°C（体感 {Apparent:0.#}°C），"
        + $"湿度 {Humidity:0}% ，风 {WindSpeed:0.#} km/h，降水 {Precipitation:0.#} mm"
        + (PrecipitationProbability is { } chance ? $"，今日降水概率 {chance}%" : "")
        + (Maximum is { } high && Minimum is { } low ? $"，今日 {low:0.#}–{high:0.#}°C" : "")
        + $"；观测时间 {ObservedAt:yyyy-MM-dd HH:mm zzz}";
}

/// <summary>Open-Meteo client with a small on-disk cache. No API key is used.</summary>
public sealed class WeatherService
{
    private readonly HttpClient http;
    private readonly string cachePath;
    private readonly object gate = new();

    public WeatherService(string root, HttpClient? client = null)
    {
        cachePath = Path.Combine(Path.GetFullPath(root), "weather.json");
        http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
    }

    public async Task<WeatherSnapshot> GetAsync(WeatherLocation location, CancellationToken token, bool force = false)
    {
        if (!force && TryReadCache(location) is { } cached && DateTimeOffset.UtcNow - cached.ObservedAt < TimeSpan.FromMinutes(10)) return cached;
        var snapshot = await FetchAsync(location, token);
        lock (gate) AtomicText.Write(cachePath, JsonSerializer.Serialize(snapshot));
        return snapshot;
    }

    public WeatherSnapshot? TryReadCache(WeatherLocation? location = null)
    {
        try
        {
            var cached = JsonSerializer.Deserialize<WeatherSnapshot>(File.ReadAllText(cachePath));
            if (cached == null) return null;
            if (location != null && (Math.Abs(cached.Latitude - location.Latitude) > .001 || Math.Abs(cached.Longitude - location.Longitude) > .001)) return null;
            return cached;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private async Task<WeatherSnapshot> FetchAsync(WeatherLocation location, CancellationToken token)
    {
        string url = "https://api.open-meteo.com/v1/forecast?latitude=" + location.Latitude.ToString("0.####", CultureInfo.InvariantCulture)
            + "&longitude=" + location.Longitude.ToString("0.####", CultureInfo.InvariantCulture)
            + "&current=temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,weather_code,wind_speed_10m"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=auto&forecast_days=1";
        using var response = await http.GetAsync(url, token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = json.RootElement;
        if (!root.TryGetProperty("current", out var current) || !current.TryGetProperty("time", out var time)
            || !DateTime.TryParse(time.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var localTime))
            throw new IOException("天气服务返回格式无法识别。");
        int offsetSeconds = root.TryGetProperty("utc_offset_seconds", out var offset) && offset.TryGetInt32(out var value) ? value : 0;
        var observed = new DateTimeOffset(localTime, TimeSpan.FromSeconds(offsetSeconds));
        var daily = root.TryGetProperty("daily", out var d) ? d : default;
        return new WeatherSnapshot(
            location.Label,
            location.Latitude,
            location.Longitude,
            observed,
            Double(current, "temperature_2m"),
            Double(current, "apparent_temperature"),
            Double(current, "relative_humidity_2m"),
            Integer(current, "weather_code"),
            Double(current, "precipitation"),
            Double(current, "wind_speed_10m"),
            FirstInteger(daily, "weather_code") ?? 0,
            FirstDouble(daily, "temperature_2m_max"),
            FirstDouble(daily, "temperature_2m_min"),
            FirstInteger(daily, "precipitation_probability_max") is int chance ? chance : null);
    }

    public async Task<WeatherLocation[]> SearchAsync(string query, CancellationToken token)
    {
        query = (query ?? "").Trim();
        if (query.Length < 2 || query.Length > 80) throw new ArgumentException("请输入2–80个字的城市名。");
        string url = "https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(query)
            + "&count=5&language=zh&format=json";
        using var response = await http.GetAsync(url, token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (!json.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return [];
        return results.EnumerateArray().Take(5).Select(item =>
        {
            string name = Text(item, "name"), admin = Text(item, "admin1"), country = Text(item, "country");
            string label = string.Join(" · ", new[] { name, admin, country }.Where(x => x.Length > 0));
            return new WeatherLocation(label, Double(item, "latitude"), Double(item, "longitude"));
        }).ToArray();
    }

    public static string? DescribeChange(WeatherSnapshot old, WeatherSnapshot current)
    {
        if (Math.Abs(old.Latitude - current.Latitude) > .001 || Math.Abs(old.Longitude - current.Longitude) > .001) return null;
        var changes = new List<string>();
        string oldKind = Kind(old.WeatherCode), newKind = Kind(current.WeatherCode);
        if (oldKind != newKind) changes.Add($"天气从{Description(old.WeatherCode)}变为{Description(current.WeatherCode)}");
        if (Math.Abs(old.Temperature - current.Temperature) >= 3) changes.Add($"气温从{old.Temperature:0.#}°C变为{current.Temperature:0.#}°C");
        if (old.PrecipitationProbability is { } before && current.PrecipitationProbability is { } after && Math.Abs(before - after) >= 30)
            changes.Add($"降水概率从{before}%变为{after}%");
        else if (old.Precipitation <= 0 && current.Precipitation > 0) changes.Add("开始出现降水");
        else if (old.Precipitation > 0 && current.Precipitation <= 0) changes.Add("降水停止");
        if (Math.Abs(old.WindSpeed - current.WindSpeed) >= 10) changes.Add($"风速从{old.WindSpeed:0.#}变为{current.WindSpeed:0.#} km/h");
        return changes.Count == 0 ? null : string.Join("；", changes);
    }

    public static string Description(int code) => code switch
    {
        0 => "晴", 1 => "大部晴朗", 2 => "多云", 3 => "阴", 45 or 48 => "雾",
        >= 51 and <= 57 => "毛毛雨", >= 61 and <= 67 => "雨", >= 71 and <= 77 => "雪",
        >= 80 and <= 82 => "阵雨", 85 or 86 => "阵雪", >= 95 => "雷暴", _ => "未知天气"
    };

    private static string Kind(int code) => code switch
    {
        0 or 1 => "clear", 2 or 3 => "cloud", 45 or 48 => "fog",
        >= 51 and <= 67 => "rain", >= 71 and <= 77 => "snow", >= 80 and <= 82 => "rain",
        85 or 86 => "snow", >= 95 => "storm", _ => "other"
    };

    private static string Text(JsonElement value, string key) => value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";
    private static double Double(JsonElement value, string key) => value.TryGetProperty(key, out var field) && field.TryGetDouble(out var number) ? number : 0;
    private static int Integer(JsonElement value, string key) => value.TryGetProperty(key, out var field) && field.TryGetInt32(out var number) ? number : 0;
    private static double? FirstDouble(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0 && list[0].TryGetDouble(out var number) ? number : null;
    private static int? FirstInteger(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0 && list[0].TryGetInt32(out var number) ? number : null;
}
