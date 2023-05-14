using System.Text;
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

        public static string ToBase64JsonString(this object value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value.ToJsonString()));
        }

        public static string ToJsonString(this object value)
        {
            return JsonSerializer.Serialize(value, options);
        }
    }
}
