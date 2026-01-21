using IntelliTrader.Core;
using IntelliTrader.Web.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Primitives;
using System.Threading.Tasks;

namespace IntelliTrader.Web.Controllers
{
    [Authorize]
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

        public HomeController(
            ICoreService coreService,
            ITradingService tradingService,
            ISignalsService signalsService,
            ILoggingService loggingService,
            IHealthCheckService healthCheckService,
            IPasswordService passwordService,
            IConfigProvider configProvider,
            IEnumerable<IConfigurableService> configurableServices,
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
            _portfolioRiskManager = portfolioRiskManager; // Optional - may be null if not registered
        }

        #region Authentication

        [AllowAnonymous]
        public async Task<IActionResult> Login()
        {
            if (_coreService.Config.PasswordProtected)
            {
                var model = new LoginViewModel
                {
                    RememberMe = true
                };
                return View(model);
            }
            else
            {
                return await PerformLogin(true);
            }
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
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
                    return await PerformLogin(model.RememberMe);
                }
            }
            else
            {
                return View(model);
            }
        }

        [AllowAnonymous]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        private async Task<IActionResult> PerformLogin(bool persistent)
        {
            var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
            var name = "user";
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, name));
            identity.AddClaim(new Claim(ClaimTypes.Name, name));
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = persistent });

            if (Request.Query.TryGetValue("ReturnUrl", out StringValues url))
            {
                return RedirectToAction(url);
            }
            else
            {
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// Generates a secure BCrypt hash for a password.
        /// Use this endpoint to create a hash for core.json configuration.
        ///
        /// SECURITY NOTE: This endpoint requires authentication. Only authenticated
        /// administrators can generate password hashes.
        ///
        /// USAGE:
        /// 1. POST /GeneratePasswordHash with {"password": "your-new-password"}
        /// 2. Copy the returned hash to core.json Password field
        /// 3. Restart the service
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
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

        public IActionResult Settings()
        {
            var model = new SettingsViewModel()
            {
                InstanceName = _coreService.Config.InstanceName,
                Version = _coreService.Version,
                BuyEnabled = _tradingService.Config.BuyEnabled,
                BuyDCAEnabled = _tradingService.Config.BuyDCAEnabled,
                SellEnabled = _tradingService.Config.SellEnabled,
                TradingSuspended = _tradingService.IsTradingSuspended,
                HealthCheckEnabled = _coreService.Config.HealthCheckEnabled,
                Configs = _configurableServices.Where(s => !s.GetType().Name.Contains(Constants.ServiceNames.BacktestingService)).OrderBy(s => s.ServiceName).ToDictionary(s => s.ServiceName, s => _configProvider.GetSectionJson(s.ServiceName))
            };

            return View(model);
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



        /// <summary>
        /// Get current trading status.
        /// </summary>
        /// <remarks>
        /// DEPRECATED: Use GET /api/status instead.
        /// This endpoint is maintained for backward compatibility but will be removed in a future version.
        /// </remarks>
        [Obsolete("Use GET /api/status instead. This endpoint will be removed in a future version.")]
        public IActionResult Status()
        {
            var status = new
            {
                Balance = _tradingService.Account.GetBalance(),
                GlobalRating = _signalsService.GetGlobalRating()?.ToString("0.000") ?? "N/A",
                TrailingBuys = _tradingService.GetTrailingBuys(),
                TrailingSells = _tradingService.GetTrailingSells(),
                TrailingSignals = _signalsService.GetTrailingSignals(),
                TradingSuspended = _tradingService.IsTradingSuspended,
                HealthChecks = _healthCheckService.GetHealthChecks().OrderBy(c => c.Name),
                LogEntries = _loggingService.GetLogEntries().Reverse().Take(5),
                // Portfolio risk management metrics
                RiskManagement = _portfolioRiskManager != null && _tradingService.Config.RiskManagement?.Enabled == true
                    ? new
                    {
                        PortfolioHeat = _portfolioRiskManager.GetCurrentHeat(),
                        MaxPortfolioHeat = _tradingService.Config.RiskManagement.MaxPortfolioHeat,
                        CurrentDrawdown = _portfolioRiskManager.GetCurrentDrawdown(),
                        MaxDrawdownPercent = _tradingService.Config.RiskManagement.MaxDrawdownPercent,
                        DailyProfitLoss = _portfolioRiskManager.GetDailyProfitLoss(),
                        DailyLossLimitPercent = _tradingService.Config.RiskManagement.DailyLossLimitPercent,
                        CircuitBreakerTriggered = _portfolioRiskManager.IsCircuitBreakerTriggered(),
                        DailyLossLimitReached = _portfolioRiskManager.IsDailyLossLimitReached()
                    }
                    : null
            };
            return Json(status);
        }

        /// <summary>
        /// Get all available signal names.
        /// </summary>
        /// <remarks>
        /// DEPRECATED: Use GET /api/signal-names instead.
        /// This endpoint is maintained for backward compatibility but will be removed in a future version.
        /// </remarks>
        [Obsolete("Use GET /api/signal-names instead. This endpoint will be removed in a future version.")]
        public IActionResult SignalNames()
        {
            return Json(_signalsService.GetSignalNames());
        }

        /// <summary>
        /// Get all active trading pairs with their current status.
        /// </summary>
        /// <remarks>
        /// DEPRECATED: Use POST /api/trading-pairs instead.
        /// This endpoint is maintained for backward compatibility but will be removed in a future version.
        /// </remarks>
        [HttpPost]
        [Obsolete("Use POST /api/trading-pairs instead. This endpoint will be removed in a future version.")]
        public IActionResult TradingPairs()
        {
            var tradingPairs = from tradingPair in _tradingService.Account.GetTradingPairs()
                               let pairConfig = _tradingService.GetPairConfig(tradingPair.Pair)
                               select new
                               {
                                   Name = tradingPair.Pair,
                                   DCA = tradingPair.DCALevel,
                                   TradingViewName = $"{_tradingService.Config.Exchange.ToUpperInvariant()}:{tradingPair.Pair}",
                                   Margin = tradingPair.CurrentMargin.ToString("0.00"),
                                   Target = pairConfig.SellMargin.ToString("0.00"),
                                   CurrentPrice = tradingPair.CurrentPrice.ToString("0.00000000"),
                                   BoughtPrice = tradingPair.AveragePricePaid.ToString("0.00000000"),
                                   Cost = tradingPair.AverageCostPaid.ToString("0.00000000"),
                                   CurrentCost = tradingPair.CurrentCost.ToString("0.00000000"),
                                   Amount = tradingPair.TotalAmount.ToString("0.########"),
                                   OrderDates = tradingPair.OrderDates.Select(d => d.ToOffset(TimeSpan.FromHours(_coreService.Config.TimezoneOffset)).ToString("yyyy-MM-dd HH:mm:ss")),
                                   OrderIds = tradingPair.OrderIds,
                                   Age = tradingPair.CurrentAge.ToString("0.00"),
                                   CurrentRating = tradingPair.Metadata.CurrentRating?.ToString("0.000") ?? "N/A",
                                   BoughtRating = tradingPair.Metadata.BoughtRating?.ToString("0.000") ?? "N/A",
                                   SignalRule = tradingPair.Metadata.SignalRule ?? "N/A",
                                   SwapPair = tradingPair.Metadata.SwapPair,
                                   TradingRules = pairConfig.Rules,
                                   IsTrailingSell = _tradingService.GetTrailingSells().Contains(tradingPair.Pair),
                                   IsTrailingBuy = _tradingService.GetTrailingBuys().Contains(tradingPair.Pair),
                                   LastBuyMargin = tradingPair.Metadata.LastBuyMargin?.ToString("0.00") ?? "N/A",
                                   Config = pairConfig
                               };

            return Json(tradingPairs);
        }

        /// <summary>
        /// Get all market pairs with their signal data.
        /// </summary>
        /// <remarks>
        /// DEPRECATED: Use POST /api/market-pairs or POST /api/market-pairs/filtered instead.
        /// This endpoint is maintained for backward compatibility but will be removed in a future version.
        /// </remarks>
        [HttpPost]
        [Obsolete("Use POST /api/market-pairs or POST /api/market-pairs/filtered instead. This endpoint will be removed in a future version.")]
        public IActionResult MarketPairs(List<string> signalsFilter)
        {
            var allSignals = _signalsService.GetAllSignals();
            if (allSignals != null)
            {
                if (signalsFilter.Count > 0)
                {
                    allSignals = allSignals.Where(s => signalsFilter.Contains(s.Name));
                }

                var groupedSignals = allSignals.GroupBy(s => s.Pair).ToDictionary(g => g.Key, g => g.AsEnumerable());

                var marketPairs = from signalGroup in groupedSignals
                                  let pair = signalGroup.Key
                                  let pairConfig = _tradingService.GetPairConfig(pair)
                                  select new
                                  {
                                      Name = pair,
                                      TradingViewName = $"{_tradingService.Config.Exchange.ToUpperInvariant()}:{pair}",
                                      VolumeList = signalGroup.Value.Select(s => new { s.Name, s.Volume }),
                                      VolumeChangeList = signalGroup.Value.Select(s => new { s.Name, s.VolumeChange }),
                                      PriceList = signalGroup.Value.Select(s => new { s.Name, s.Price }),
                                      PriceChangeList = signalGroup.Value.Select(s => new { s.Name, s.PriceChange }),
                                      RatingList = signalGroup.Value.Select(s => new { s.Name, s.Rating }),
                                      RatingChangeList = signalGroup.Value.Select(s => new { s.Name, s.RatingChange }),
                                      VolatilityList = signalGroup.Value.Select(s => new { s.Name, s.Volatility }),
                                      SignalRules = _signalsService.GetTrailingInfo(pair)?.Select(ti => ti.Rule.Name) ?? new string[0],
                                      HasTradingPair = _tradingService.Account.HasTradingPair(pair),
                                      Config = pairConfig
                                  };

                return Json(marketPairs);
            }
            else
            {
                return Json(null);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
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
        public IActionResult Sell([FromForm] SellInputModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _tradingService.Sell(new SellOptions(model.Pair)
            {
                Amount = model.Amount,
                ManualOrder = true
            });
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Buy([FromForm] BuyInputModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _tradingService.Buy(new BuyOptions(model.Pair)
            {
                Amount = model.Amount,
                IgnoreExisting = true,
                ManualOrder = true
            });
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult BuyDefault([FromForm] BuyDefaultInputModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _tradingService.Buy(new BuyOptions(model.Pair)
            {
                MaxCost = _tradingService.GetPairConfig(model.Pair).BuyMaxCost,
                IgnoreExisting = true,
                ManualOrder = true,
                Metadata = new OrderMetadata
                {
                    BoughtGlobalRating = _signalsService.GetGlobalRating()
                }
            });
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Swap([FromForm] SwapInputModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _tradingService.Swap(new SwapOptions(model.Pair, model.Swap, new OrderMetadata())
            {
                ManualOrder = true
            });
            return Ok();
        }

        public IActionResult RefreshAccount()
        {
            _tradingService.Account.Refresh();
            return new OkResult();
        }

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
                foreach (var tradesLogFilePath in Directory.EnumerateFiles(logsPath, "*-trades.txt", SearchOption.TopDirectoryOnly))
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
