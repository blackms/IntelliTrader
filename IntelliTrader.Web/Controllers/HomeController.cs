using IntelliTrader.Core;
using IntelliTrader.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Primitives;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;

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
        private readonly IEnumerable<IConfigurableService> _configurableServices;

        public HomeController(
            ICoreService coreService,
            ITradingService tradingService,
            ISignalsService signalsService,
            ILoggingService loggingService,
            IHealthCheckService healthCheckService,
            IEnumerable<IConfigurableService> configurableServices)
        {
            _coreService = coreService ?? throw new ArgumentNullException(nameof(coreService));
            _tradingService = tradingService ?? throw new ArgumentNullException(nameof(tradingService));
            _signalsService = signalsService ?? throw new ArgumentNullException(nameof(signalsService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
            _configurableServices = configurableServices ?? throw new ArgumentNullException(nameof(configurableServices));
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
                var isValid = !_coreService.Config.PasswordProtected || ComputeMD5Hash(model.Password).Equals(_coreService.Config.Password, StringComparison.InvariantCultureIgnoreCase);
                if (!isValid)
                {
                    ModelState.AddModelError("Password", "Invalid Password");
                    return View(model);
                }
                else
                {
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

        private string ComputeMD5Hash(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                throw new ArgumentNullException(nameof(input));
            }

            using (var md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.ASCII.GetBytes(input);
                byte[] hash = md5.ComputeHash(inputBytes);

                var sb = new StringBuilder();
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("X2"));
                }
                return sb.ToString();
            }
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

        public IActionResult Stats()
        {
            var accountInitialBalance = _tradingService.Config.VirtualTrading ? _tradingService.Config.VirtualAccountInitialBalance : _tradingService.Config.AccountInitialBalance;
            var accountInitialBalanceDate = _tradingService.Config.VirtualTrading ? DateTimeOffset.Now.AddDays(-30) : _tradingService.Config.AccountInitialBalanceDate;

            decimal accountBalance = _tradingService.Account.GetBalance();
            foreach (var tradingPair in _tradingService.Account.GetTradingPairs())
            {
                accountBalance += _tradingService.GetCurrentPrice(tradingPair.Pair) * tradingPair.TotalAmount;
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
                Configs = _configurableServices.Where(s => !s.GetType().Name.Contains(Constants.ServiceNames.BacktestingService)).OrderBy(s => s.ServiceName).ToDictionary(s => s.ServiceName, s => Application.ConfigProvider.GetSectionJson(s.ServiceName))
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
                LogEntries = _loggingService.GetLogEntries().Reverse().Take(5)
            };
            return Json(status);
        }

        public IActionResult SignalNames()
        {
            return Json(_signalsService.GetSignalNames());
        }

        [HttpPost]
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

        [HttpPost]
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
        public IActionResult SaveConfig()
        {
            string configName = Request.Form["name"].ToString();
            string configDefinition = Request.Form["definition"].ToString();

            if (!String.IsNullOrWhiteSpace(configName) && !String.IsNullOrWhiteSpace(configDefinition))
            {
                Application.ConfigProvider.SetSectionJson(configName, configDefinition);
                return new OkResult();
            }
            else
            {
                return new BadRequestResult();
            }
        }

        [HttpPost]
        public IActionResult Sell()
        {
            string pair = Request.Form["pair"].ToString();
            if (pair != null && decimal.TryParse(Request.Form["amount"], out decimal amount) && amount > 0)
            {
                _tradingService.Sell(new SellOptions(pair)
                {
                    Amount = amount,
                    ManualOrder = true
                });
                return new OkResult();
            }
            else
            {
                return new BadRequestResult();
            }
        }

        [HttpPost]
        public IActionResult Buy()
        {
            string pair = Request.Form["pair"].ToString();
            if (pair != null && decimal.TryParse(Request.Form["amount"], out decimal amount) && amount > 0)
            {
                _tradingService.Buy(new BuyOptions(pair)
                {
                    Amount = amount,
                    IgnoreExisting = true,
                    ManualOrder = true
                });
                return new OkResult();
            }
            else
            {
                return new BadRequestResult();
            }
        }

        [HttpPost]
        public IActionResult BuyDefault()
        {
            string pair = Request.Form["pair"].ToString();
            if (pair != null)
            {
                _tradingService.Buy(new BuyOptions(pair)
                {
                    MaxCost = _tradingService.GetPairConfig(pair).BuyMaxCost,
                    IgnoreExisting = true,
                    ManualOrder = true,
                    Metadata = new OrderMetadata
                    {
                        BoughtGlobalRating = _signalsService.GetGlobalRating()
                    }
                });
                return new OkResult();
            }
            else
            {
                return new BadRequestResult();
            }
        }

        [HttpPost]
        public IActionResult Swap()
        {
            string pair = Request.Form["pair"].ToString();
            string swap = Request.Form["swap"].ToString();
            if (pair != null && swap != null)
            {
                _tradingService.Swap(new SwapOptions(pair, swap, new OrderMetadata())
                {
                    ManualOrder = true
                });
                return new OkResult();
            }
            else
            {
                return new BadRequestResult();
            }
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
                            TradeResult tradeResult = JsonConvert.DeserializeObject<TradeResult>(json);
                            if (tradeResult.IsSuccessful)
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
