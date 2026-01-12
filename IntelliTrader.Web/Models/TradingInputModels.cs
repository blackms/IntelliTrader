using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace IntelliTrader.Web.Models
{
    /// <summary>
    /// Input model for saving configuration changes.
    /// </summary>
    public class ConfigUpdateModel
    {
        [Required(ErrorMessage = "Configuration name is required")]
        [RegularExpression(@"^(core|trading|signals|rules|web|notification)$",
            ErrorMessage = "Invalid configuration name. Must be one of: core, trading, signals, rules, web, notification")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Configuration definition is required")]
        public string Definition { get; set; }

        /// <summary>
        /// Validates that the Definition property contains valid JSON.
        /// </summary>
        public bool IsValidJson()
        {
            if (string.IsNullOrWhiteSpace(Definition))
                return false;

            try
            {
                JsonDocument.Parse(Definition);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Input model for sell operations.
    /// </summary>
    public class SellInputModel
    {
        [Required(ErrorMessage = "Trading pair is required")]
        [RegularExpression(@"^[A-Z0-9]{2,20}$",
            ErrorMessage = "Invalid trading pair format. Must be 2-20 uppercase alphanumeric characters (e.g., BTCUSDT)")]
        public string Pair { get; set; }

        [Required(ErrorMessage = "Amount is required")]
        [Range(0.00000001, 1000000000, ErrorMessage = "Amount must be between 0.00000001 and 1,000,000,000")]
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// Input model for buy operations.
    /// </summary>
    public class BuyInputModel
    {
        [Required(ErrorMessage = "Trading pair is required")]
        [RegularExpression(@"^[A-Z0-9]{2,20}$",
            ErrorMessage = "Invalid trading pair format. Must be 2-20 uppercase alphanumeric characters (e.g., BTCUSDT)")]
        public string Pair { get; set; }

        [Required(ErrorMessage = "Amount is required")]
        [Range(0.00000001, 1000000000, ErrorMessage = "Amount must be between 0.00000001 and 1,000,000,000")]
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// Input model for buy default operations (uses configured BuyMaxCost).
    /// </summary>
    public class BuyDefaultInputModel
    {
        [Required(ErrorMessage = "Trading pair is required")]
        [RegularExpression(@"^[A-Z0-9]{2,20}$",
            ErrorMessage = "Invalid trading pair format. Must be 2-20 uppercase alphanumeric characters (e.g., BTCUSDT)")]
        public string Pair { get; set; }
    }

    /// <summary>
    /// Input model for swap operations.
    /// </summary>
    public class SwapInputModel
    {
        [Required(ErrorMessage = "Source trading pair is required")]
        [RegularExpression(@"^[A-Z0-9]{2,20}$",
            ErrorMessage = "Invalid source pair format. Must be 2-20 uppercase alphanumeric characters (e.g., BTCUSDT)")]
        public string Pair { get; set; }

        [Required(ErrorMessage = "Target trading pair is required")]
        [RegularExpression(@"^[A-Z0-9]{2,20}$",
            ErrorMessage = "Invalid target pair format. Must be 2-20 uppercase alphanumeric characters (e.g., ETHUSDT)")]
        public string Swap { get; set; }
    }
}
