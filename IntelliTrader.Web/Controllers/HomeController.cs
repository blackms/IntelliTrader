using IntelliTrader.Core;
using IntelliTrader.Web.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Primitives;
using System.Threading.Tasks;

namespace IntelliTrader.Web.Controllers
{
    [Authorize(Policy = AuthPolicies.ViewerOrAbove)]
    public class HomeController : Controller
    {
        private readonly ICoreService _coreService;
        private readonly ITradingService _tradingService;
        private readonly ISignalsService _signalsService;
        private readonly ILoggingService _loggingService;
        private readonly IHealthCheckService _healthCheckService;
        private readonly IPasswordService _passwordService;
        private readonly IConfigProvider _configProvider;
        private readonly IEnumerable<IConfigurableService> _configurableServices;
        private readonly IPortfolioRiskManager _portfolioRiskManager;
        private readonly UsersConfig _usersConfig;

        public HomeController(
            ICoreService coreService,
            ITradingService tradingService,
            ISignalsService signalsService,
            ILoggingService loggingService,
            IHealthCheckService healthCheckService,
            IPasswordService passwordService,
            IConfigProvider configProvider,
            IEnumerable<IConfigurableService> configurableServices,
            UsersConfig usersConfig,
            IPortfolioRiskManager portfolioRiskManager = null)
        {
            _coreService = coreService ?? throw new ArgumentNullException(nameof(coreService));
            _tradingService = tradingService ?? throw new ArgumentNullException(nameof(tradingService));
            _signalsService = signalsService ?? throw new ArgumentNullException(nameof(signalsService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
            _passwordService = passwordService ?? throw new ArgumentNullException(nameof(passwordService));
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
            _configurableServices = configurableServices ?? throw new ArgumentNullException(nameof(configurableServices));
            _usersConfig = usersConfig ?? throw new ArgumentNullException(nameof(usersConfig));
            _portfolioRiskManager = portfolioRiskManager; // Optional - may be null if not registered
        }

        /// <summary>
        /// Whether RBAC mode is active (users.json has users defined).
        /// When false, falls back to legacy single-password mode from core.json.
        /// </summary>
        private bool IsRbacEnabled => _usersConfig.Users != null && _usersConfig.Users.Count > 0;

        #region Authentication

        [AllowAnonymous]
        public async Task<IActionResult> Login()
        {
            if (_coreService.Config.PasswordProtected || IsRbacEnabled)
            {
                var model = new LoginViewModel
                {
                    RememberMe = true,
                    UsesRbac = IsRbacEnabled
                };
                return View(model);
            }
            else
            {
                return await PerformLogin("user", UserRoles.Admin, true);
            }
        }

        [HttpPost]
        [AllowAnonymous]
        [EnableRateLimiting("login")]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                if (IsRbacEnabled)
                {
                    // RBAC mode: validate username + password against users.json
                    if (string.IsNullOrWhiteSpace(model.Username))
                    {
                        ModelState.AddModelError("Username", "Username is required");
                        return View(model);
                    }

                    var user = _usersConfig.Users.Find(u =>
                        string.Equals(u.Username, model.Username, StringComparison.OrdinalIgnoreCase));

                    if (user == null || !_passwordService.VerifyPassword(model.Password, user.PasswordHash))
                    {
                        ModelState.AddModelError("Password", "Invalid username or password");
                        return View(model);
                    }

                    return await PerformLogin(user.Username, user.Role, model.RememberMe);
                }
                else
                {
                    // Legacy single-password mode: validate against core.json Password
                    var isValid = !_coreService.Config.PasswordProtected ||
                        _passwordService.VerifyPassword(model.Password, _coreService.Config.Password);
                    if (!isValid)
                    {
                        ModelState.AddModelError("Password", "Invalid Password");
                        return View(model);
                    }
                    else
                    {
                        // Log warning if using legacy MD5 hash - admin should migrate to BCrypt
                        if (_passwordService.IsLegacyHash(_coreService.Config.Password))
                        {
                            _loggingService.Warning(
                                "SECURITY WARNING: Password is using legacy MD5 hash. " +
                                "Please generate a new BCrypt hash using /GeneratePasswordHash endpoint and update core.json");
                        }
                        // Legacy mode: all authenticated users get Admin role for backward compatibility
                        return await PerformLogin("user", UserRoles.Admin, model.RememberMe);
                    }
                }
            }
            else
            {
                return View(model);
            }
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        private async Task<IActionResult> PerformLogin(string username, string role, bool persistent)
        {
            var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, username));
            identity.AddClaim(new Claim(ClaimTypes.Name, username));
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = persistent });

            if (Request.Query.TryGetValue("ReturnUrl", out StringValues url) && Url.IsLocalUrl(url))
            {
                return LocalRedirect(url);
            }
            else
            {
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// Generates a secure BCrypt hash for a password.
        /// Use this endpoint to create a hash for users.json or core.json configuration.
        ///
        /// SECURITY NOTE: This endpoint requires Admin role.
        ///
        /// USAGE:
        /// 1. POST /GeneratePasswordHash with {"password": "your-new-password"}
        /// 2. Copy the returned hash to users.json PasswordHash field (or core.json Password for legacy mode)
        /// 3. Restart the service
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AuthPolicies.AdminOnly)]
        public IActionResult GeneratePasswordHash([FromForm] string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return BadRequest("Password cannot be empty");
            }

            if (password.Length < 8)
            {
                return BadRequest("Password must be at least 8 characters long");
            }

            var hash = _passwordService.HashPassword(password);

            return Json(new
            {
                Hash = hash,
                Instructions = "Copy this hash to core.json 'Password' field and restart the service. " +
                              "The hash uses BCrypt with work factor 12 for secure password storage."
            });
        }

        /// <summary>
        /// Returns information about the current password hash type.
        /// Useful for administrators to check if migration to BCrypt is needed.
        /// </summary>
        [Authorize(Policy = AuthPolicies.AdminOnly)]
        public IActionResult PasswordStatus()
        {
            var currentHash = _coreService.Config.Password;
            var isLegacy = _passwordService.IsLegacyHash(currentHash);
            var isBCrypt = _passwordService.IsBCryptHash(currentHash);

            return Json(new
            {
                PasswordProtected = _coreService.Config.PasswordProtected,
                HashType = isBCrypt ? "BCrypt (secure)" : isLegacy ? "MD5 (legacy - migrate immediately)" : "Unknown",
                NeedsMigration = isLegacy,
                MigrationInstructions = isLegacy
                    ? "Use POST /GeneratePasswordHash to create a new BCrypt hash and update core.json"
                    : null
            });
        }

        #endregion Authentication

        public IActionResult Index()
        {
            return Dashboard();
        }

        public IActionResult Dashboard()
        {
            var model = new DashboardViewModel
            {
                InstanceName = _coreService.Config.InstanceName,
                Version = _coreService.Version
            };
            return View(nameof(Dashboard), model);
        }

        public IActionResult Market()
        {
            var model = new MarketViewModel
            {
                InstanceName = _coreService.Config.InstanceName,
                Version = _coreService.Version
            };
            return View(model);
        }

        public async Task<IActionResult> Stats()
        {
            var accountInitialBalance = _tradingService.Config.VirtualTrading ? _tradingService.Config.VirtualAccountInitialBalance : _tradingService.Config.AccountInitialBalance;
            var accountInitialBalanceDate = _tradingService.Config.VirtualTrading ? DateTimeOffset.Now.AddDays(-30) : _tradingService.Config.AccountInitialBalanceDate;

            decimal accountBalance = _tradingService.Account.GetBalance();
            var tradingPairs = _tradingService.Account.GetTradingPairs().ToList();

            // Fetch all prices in parallel for better performance
            var priceTasks = tradingPairs.Select(async tp => new
            {
                Price = await _tradingService.GetCurrentPriceAsync(tp.Pair),
                Amount = tp.TotalAmount
            });
            var priceResults = await Task.WhenAll(priceTasks);

            foreach (var result in priceResults)
            {
                accountBalance += result.Price * result.Amount;
            }

            var model = new StatsViewModel
            {
                InstanceName = _coreService.Config.InstanceName,
                Version = _coreService.Version,
                TimezoneOffset = _coreService.Config.TimezoneOffset,
                AccountInitialBalance = accountInitialBalance,
                AccountBalance = accountBalance,
                Market = _tradingService.Config.Market,
                Balances = new Dictionary<DateTimeOffset, decimal>(),
                Trades = GetTrades()
            };

            foreach (var kvp in model.Trades.OrderBy(k => k.Key))
            {
                var date = kvp.Key;
                var trades = kvp.Value;

                model.Balances[date] = accountInitialBalance;

                if (date > accountInitialBalanceDate.Date)
                {
                    for (int d = 1; d < (int)(date - accountInitialBalanceDate.Date).TotalDays; d++)
                    {
                        var prevDate = date.AddDays(-d);
                        if (model.Trades.ContainsKey(prevDate))
                        {
                            model.Balances[date] += model.Trades[prevDate].Where(t => !t.IsSwap).Sum(t => t.Profit);
                        }
                    }
                }
            }

            return View(model);
        }

        public IActionResult Trades(DateTimeOffset id)
        {
            var model = new TradesViewModel()
            {
                InstanceName = _coreService.Config.InstanceName,
                Version = _coreService.Version,
                TimezoneOffset = _coreService.Config.TimezoneOffset,
                Date = id,
                Trades = GetTrades(id).Values.FirstOrDefault() ?? new List<TradeResult>()
            };

            return View(model);
        }

        // Keys whose values must be redacted before sending config JSON to the browser.
        private static readonly HashSet<string> SensitiveKeyPatterns = new(StringComparer.OrdinalIgnoreCase)
        {
            "password", "token", "secret", "key", "apikey", "privatekey"
        };

        private const string RedactedPlaceholder = "********";

        [Authorize(Policy = AuthPolicies.AdminOnly)]
        public IActionResult Settings()
        {
            var rawConfigs = _configurableServices
                .Where(s => !s.GetType().Name.Contains(Constants.ServiceNames.BacktestingService))
                .OrderBy(s => s.ServiceName)
                .ToDictionary(s => s.ServiceName, s => _configProvider.GetSectionJson(s.ServiceName));

            // Redact sensitive values before sending to the browser
            var redactedConfigs = new Dictionary<string, string>();
            foreach (var kvp in rawConfigs)
            {
                redactedConfigs[kvp.Key] = RedactSensitiveJson(kvp.Value);
            }

            var model = new SettingsViewModel()
            {
                InstanceName = _coreService.Config.InstanceName,
                Version = _coreService.Version,
                BuyEnabled = _tradingService.Config.BuyEnabled,
                BuyDCAEnabled = _tradingService.Config.BuyDCAEnabled,
                SellEnabled = _tradingService.Config.SellEnabled,
                TradingSuspended = _tradingService.IsTradingSuspended,
                HealthCheckEnabled = _coreService.Config.HealthCheckEnabled,
                Configs = redactedConfigs
            };

            return View(model);
        }

        /// <summary>
        /// Parses a JSON string, replaces values of keys that match sensitive patterns
        /// with a redacted placeholder, and returns the sanitised JSON.
        /// Returns the original string unchanged if parsing fails.
        /// </summary>
        private static string RedactSensitiveJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return json;

            try
            {
                var node = JsonNode.Parse(json);
                if (node != null)
                {
                    RedactNode(node);
                    return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                }
            }
            catch
            {
                // If JSON is unparseable, return it as-is rather than crashing.
            }

            return json;
        }

        private static void RedactNode(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                var keys = obj.Select(p => p.Key).ToList();
                foreach (var key in keys)
                {
                    if (IsSensitiveKey(key))
                    {
                        obj[key] = RedactedPlaceholder;
                    }
                    else if (obj[key] is JsonNode child)
                    {
                        RedactNode(child);
                    }
                }
            }
            else if (node is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (item != null)
                    {
                        RedactNode(item);
                    }
                }
            }
        }

        private static bool IsSensitiveKey(string key)
        {
            foreach (var pattern in SensitiveKeyPatterns)
            {
                if (key.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public IActionResult Log()
        {
            var model = new LogViewModel()
            {
                InstanceName = _coreService.Config.InstanceName,
                Version = _coreService.Version,
                LogEntries = _loggingService.GetLogEntries().Reverse().Take(500)
            };

            return View(model);
        }

        public IActionResult Help()
        {
            var model = new HelpViewModel()
            {
                InstanceName = _coreService.Config.InstanceName,
                Version = _coreService.Version
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("config")]
        [Authorize(Policy = AuthPolicies.AdminOnly)]
        public IActionResult Settings(SettingsViewModel model)
        {
            _coreService.Config.HealthCheckEnabled = model.HealthCheckEnabled;
            _tradingService.Config.BuyEnabled = model.BuyEnabled;
            _tradingService.Config.BuyDCAEnabled = model.BuyDCAEnabled;
            _tradingService.Config.SellEnabled = model.SellEnabled;

            if (model.TradingSuspended)
            {
                _tradingService.SuspendTrading();
            }
            else
            {
                _tradingService.ResumeTrading();
            }
            return Settings();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("config")]
        [Authorize(Policy = AuthPolicies.AdminOnly)]
        public IActionResult SaveConfig([FromForm] ConfigUpdateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Validate JSON structure before saving
            if (!model.IsValidJson())
            {
                return BadRequest("Invalid JSON format in configuration definition");
            }

            _configProvider.SetSectionJson(model.Name, model.Definition);
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("trading")]
        [Authorize(Policy = AuthPolicies.TraderOrAbove)]
        public async Task<IActionResult> Sell([FromForm] SellInputModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            await _tradingService.SellAsync(new SellOptions(model.Pair)
            {
                Amount = model.Amount,
                ManualOrder = true
            }).ConfigureAwait(false);
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("trading")]
        [Authorize(Policy = AuthPolicies.TraderOrAbove)]
        public async Task<IActionResult> Buy([FromForm] BuyInputModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            await _tradingService.BuyAsync(new BuyOptions(model.Pair)
            {
                Amount = model.Amount,
                IgnoreExisting = true,
                ManualOrder = true
            }).ConfigureAwait(false);
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("trading")]
        [Authorize(Policy = AuthPolicies.TraderOrAbove)]
        public async Task<IActionResult> BuyDefault([FromForm] BuyDefaultInputModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            await _tradingService.BuyAsync(new BuyOptions(model.Pair)
            {
                MaxCost = _tradingService.GetPairConfig(model.Pair).BuyMaxCost,
                IgnoreExisting = true,
                ManualOrder = true,
                Metadata = new OrderMetadata
                {
                    BoughtGlobalRating = _signalsService.GetGlobalRating()
                }
            }).ConfigureAwait(false);
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("trading")]
        [Authorize(Policy = AuthPolicies.TraderOrAbove)]
        public async Task<IActionResult> Swap([FromForm] SwapInputModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            await _tradingService.SwapAsync(new SwapOptions(model.Pair, model.Swap, new OrderMetadata())
            {
                ManualOrder = true
            }).ConfigureAwait(false);
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AuthPolicies.AdminOnly)]
        public IActionResult RefreshAccount()
        {
            _tradingService.Account.Refresh();
            return new OkResult();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = AuthPolicies.AdminOnly)]
        public IActionResult RestartServices()
        {
            _coreService.Restart();
            return new OkResult();
        }

        private Dictionary<DateTimeOffset, List<TradeResult>> GetTrades(DateTimeOffset? date = null)
        {
            var logsPath = Path.Combine(Directory.GetCurrentDirectory(), "log");
            var tradeResultPattern = new Regex($"{nameof(TradeResult)} (?<data>\\{{.*\\}})", RegexOptions.Compiled);
            var trades = new Dictionary<DateTimeOffset, List<TradeResult>>();

            if (Directory.Exists(logsPath))
            {
                var logFiles = Directory.EnumerateFiles(logsPath, "*-trades.txt", SearchOption.TopDirectoryOnly);

                // When filtering by a specific date, skip log files that were last modified
                // before the requested date to avoid reading irrelevant files
                if (date != null)
                {
                    logFiles = logFiles.Where(f =>
                    {
                        var lastWrite = System.IO.File.GetLastWriteTimeUtc(f);
                        return lastWrite >= date.Value.UtcDateTime.Date;
                    });
                }

                foreach (var tradesLogFilePath in logFiles)
                {
                    IEnumerable<string> logLines = Utils.ReadAllLinesWriteSafe(tradesLogFilePath);
                    foreach (var logLine in logLines)
                    {
                        var match = tradeResultPattern.Match(logLine);
                        if (match.Success)
                        {
                            var data = match.Groups["data"].ToString();
                            var json = Utils.FixInvalidJson(data.Replace(nameof(OrderMetadata), ""));
                            TradeResult? tradeResult = JsonSerializer.Deserialize<TradeResult>(json);
                            if (tradeResult != null && tradeResult.IsSuccessful)
                            {
                                DateTimeOffset tradeDate = tradeResult.SellDate.ToOffset(TimeSpan.FromHours(_coreService.Config.TimezoneOffset)).Date;
                                if (date == null || date == tradeDate)
                                {
                                    if (!trades.ContainsKey(tradeDate))
                                    {
                                        trades.Add(tradeDate, new List<TradeResult>());
                                    }
                                    trades[tradeDate].Add(tradeResult);
                                }
                            }
                        }
                    }
                }
            }
            return trades;
        }
    }
}
