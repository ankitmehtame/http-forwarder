using System;
using System.Globalization;
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
            try
            {
                return JsonSerializer.Deserialize<T>(json, CreateJsonSerializerOptions());
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format(CultureInfo.InvariantCulture, "Unable to deserialize json for type {0} - {1}", typeof(T).FullName, json), ex);
            }
        }

        public static object? Deserialize(string json, Type type)
        {
            try
            {
                return JsonSerializer.Deserialize(json, type, CreateJsonSerializerOptions());
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format(CultureInfo.InvariantCulture, "Unable to deserialize json for type {0} - {1}", type.Name, json), ex);
            }
        }

        public static string Serialize<T>(T item, bool indent)
        {
            return JsonSerializer.Serialize(item, CreateJsonSerializerOptions(indent));
        }
    }
}