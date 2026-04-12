using System.Collections.Generic;

namespace IntelliTrader.Web.Models
{
    /// <summary>
    /// Represents a user with role-based access control.
    /// Users are defined in config/users.json.
    /// </summary>
    public class UserConfig
    {
        /// <summary>
        /// Unique username for authentication.
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// BCrypt-hashed password. Generate using POST /GeneratePasswordHash.
        /// </summary>
        public string PasswordHash { get; set; }

        /// <summary>
        /// Role determining access level: Admin, Trader, or Viewer.
        /// </summary>
        public string Role { get; set; }
    }

    /// <summary>
    /// Root configuration object for users.json.
    /// </summary>
    public class UsersConfig
    {
        public List<UserConfig> Users { get; set; } = new List<UserConfig>();
    }

    /// <summary>
    /// Defines the available roles for RBAC.
    /// </summary>
    public static class UserRoles
    {
        public const string Admin = "Admin";
        public const string Trader = "Trader";
        public const string Viewer = "Viewer";

        /// <summary>
        /// All valid role names.
        /// </summary>
        public static readonly string[] All = { Admin, Trader, Viewer };
    }

    /// <summary>
    /// Defines authorization policy names used throughout the application.
    /// </summary>
    public static class AuthPolicies
    {
        public const string AdminOnly = "AdminOnly";
        public const string TraderOrAbove = "TraderOrAbove";
        public const string ViewerOrAbove = "ViewerOrAbove";
    }
}
