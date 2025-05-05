using System.Text.Json;

namespace http_forwarder_app.Core
{
    public static class JsonUtils
    {
        private static JsonSerializerOptions CreateJsonSerializerOptions(bool indentedWrite = false)
        {
            return new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = indentedWrite };
        }

        public static T? Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, CreateJsonSerializerOptions());
        }

        public static string Serialize<T>(T item, bool indent)
        {
            return JsonSerializer.Serialize(item, CreateJsonSerializerOptions(indent));
        }
    }
}