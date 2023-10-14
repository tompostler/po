using System.Text.Json;
using System.Text.Json.Serialization;

namespace po.Extensions
{
    public static class ObjectExtensions
    {
        private static readonly JsonSerializerOptions options;
        static ObjectExtensions()
        {
            options = new JsonSerializerOptions();
            options.Converters.Add(new JsonStringEnumConverter());
        }

        public static string ToJsonString(this object value) => JsonSerializer.Serialize(value, options);
    }
}
