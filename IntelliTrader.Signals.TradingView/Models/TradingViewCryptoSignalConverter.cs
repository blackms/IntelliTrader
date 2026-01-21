using IntelliTrader.Signals.Base;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntelliTrader.Signals.TradingView
{
    /// <summary>
    /// JSON converter for Signal objects from TradingView API responses.
    /// Converts JSON arrays in format [pair, price, priceChange, volume, rating, volatility] to Signal objects.
    /// Note: This converter is currently unused as parsing is done inline in TradingViewCryptoSignalPollingTimedTask.
    /// </summary>
    internal class TradingViewCryptoSignalConverter : JsonConverter<Signal>
    {
        public override Signal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                return null;
            }

            var signal = new Signal();
            var index = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    break;
                }

                switch (index)
                {
                    case 0:
                        signal.Pair = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                        break;
                    case 1:
                        signal.Price = reader.TokenType == JsonTokenType.Number ? reader.GetDecimal() : null;
                        break;
                    case 2:
                        signal.PriceChange = reader.TokenType == JsonTokenType.Number ? reader.GetDecimal() : null;
                        break;
                    case 3:
                        signal.Volume = reader.TokenType == JsonTokenType.Number ? reader.GetInt64() : null;
                        break;
                    case 4:
                        signal.Rating = reader.TokenType == JsonTokenType.Number ? reader.GetDouble() : null;
                        break;
                    case 5:
                        signal.Volatility = reader.TokenType == JsonTokenType.Number ? reader.GetDouble() : null;
                        break;
                }
                index++;
            }

            return signal;
        }

        public override void Write(Utf8JsonWriter writer, Signal value, JsonSerializerOptions options)
        {
            throw new NotSupportedException("Serialization of Signal to array format is not supported.");
        }
    }
}
