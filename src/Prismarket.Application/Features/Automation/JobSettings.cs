using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Prismarket.Application.Features.Automation;

/// <summary>Typed accessor over the JSON settings of an automation, falling back to catalog defaults.</summary>
public sealed class JobSettings
{
    private readonly JsonObject _values;
    private readonly IReadOnlyDictionary<string, object> _defaults;

    public JobSettings(string? json, IEnumerable<SettingDefinition> definitions)
    {
        _values = (string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) as JsonObject) ?? new JsonObject();
        _defaults = definitions.ToDictionary(d => d.Key, d => d.DefaultValue);
    }

    public static JobSettings Empty { get; } = new("{}", []);

    public int GetInt(string key) => Get(key, n => n.GetValueKind() == JsonValueKind.String
        ? int.Parse(n.GetValue<string>(), CultureInfo.InvariantCulture)
        : n.GetValue<int>(), Convert.ToInt32);

    public decimal GetDecimal(string key) => Get(key, n => n.GetValueKind() == JsonValueKind.String
        ? decimal.Parse(n.GetValue<string>(), CultureInfo.InvariantCulture)
        : n.GetValue<decimal>(), Convert.ToDecimal);

    public bool GetBool(string key) => Get(key, n => n.GetValueKind() switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => bool.Parse(n.GetValue<string>())
    }, Convert.ToBoolean);

    public string GetString(string key) => Get(key, n => n.ToString(), v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "");

    public IReadOnlyList<string> GetList(string key) =>
        GetString(key).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Effective values (stored value or default) for the admin UI.</summary>
    public Dictionary<string, object?> ToDictionary() =>
        _defaults.Keys.Union(_values.Select(v => v.Key))
            .ToDictionary(k => k, k => _values.TryGetPropertyValue(k, out var node) && node is not null
                ? JsonSerializer.Deserialize<object>(node.ToJsonString())
                : _defaults.GetValueOrDefault(k));

    private T Get<T>(string key, Func<JsonNode, T> fromJson, Func<object, T> fromDefault)
    {
        if (_values.TryGetPropertyValue(key, out var node) && node is not null)
        {
            try { return fromJson(node); }
            catch (Exception e) when (e is FormatException or InvalidOperationException) { /* fall back */ }
        }
        if (_defaults.TryGetValue(key, out var def)) return fromDefault(def);
        throw new KeyNotFoundException($"Setting '{key}' is not defined.");
    }
}
