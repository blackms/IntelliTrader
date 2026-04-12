using System.ComponentModel.DataAnnotations;

namespace IntelliTrader.Web.Models
{
    public class LoginViewModel : BaseViewModel
    {
        /// <summary>
        /// Username for RBAC login. Optional for legacy single-password mode.
        /// </summary>
        public string Username { get; set; }

        [Required, DataType(DataType.Password)]
        public string Password { get; set; }

        public bool RememberMe { get; set; }

        /// <summary>
        /// Indicates whether the system uses RBAC (users.json) or legacy single-password mode.
        /// Set by the controller to control view rendering.
        /// </summary>
        public bool UsesRbac { get; set; }
    }
}
