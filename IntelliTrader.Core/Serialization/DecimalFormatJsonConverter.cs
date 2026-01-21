using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntelliTrader.Core
{
    /// <summary>
    /// JSON converter that rounds decimal values to a specified number of decimal places during serialization.
    /// </summary>
    public class DecimalFormatJsonConverter : JsonConverter<decimal>
    {
        private readonly int _numberOfDecimals;

        public DecimalFormatJsonConverter(int numberOfDecimals)
        {
            _numberOfDecimals = numberOfDecimals;
        }

        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Read the decimal as-is without rounding
            return reader.GetDecimal();
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        {
            var rounded = Math.Round(value, _numberOfDecimals, MidpointRounding.AwayFromZero);
            writer.WriteNumberValue(rounded);
        }
    }

    /// <summary>
    /// Attribute to apply DecimalFormatJsonConverter with a specified number of decimal places.
    /// Usage: [JsonConverter(typeof(DecimalFormatJsonConverterAttribute), 8)]
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class DecimalFormatJsonConverterAttribute : JsonConverterAttribute
    {
        private readonly int _numberOfDecimals;

        public DecimalFormatJsonConverterAttribute(int numberOfDecimals)
        {
            _numberOfDecimals = numberOfDecimals;
        }

        public override JsonConverter? CreateConverter(Type typeToConvert)
        {
            if (typeToConvert != typeof(decimal))
            {
                throw new InvalidOperationException($"DecimalFormatJsonConverter can only be applied to decimal properties, not {typeToConvert.Name}");
            }

            return new DecimalFormatJsonConverter(_numberOfDecimals);
        }
    }
}
