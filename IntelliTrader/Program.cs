using Autofac;
using ExchangeSharp;
using IntelliTrader.Core;
using System;
using System.Collections.Generic;

namespace IntelliTrader
{
    class Program
    {
        private static IContainer _container;
        private static IApplicationBootstrapper _bootstrapper;

        static void Main(string[] args)
        {
            var parsedArgs = ParseCommandLineArgs(args);
            if (parsedArgs.Count == 0)
            {
                PrintWelcomeMessage();
                StartCoreService();
            }
            else
            {
                if (parsedArgs.ContainsKey("encrypt") && parsedArgs.ContainsKey("path") &&
                    parsedArgs.ContainsKey("publickey") && parsedArgs.ContainsKey("privatekey"))
                {
                    EncryptKeys(parsedArgs);
                }
                else
                {
                    PrintUsage();
                }
            }
        }

        internal static IContainer Container => _container ?? (_container = BuildAndConfigureContainer());

        private static IContainer BuildAndConfigureContainer()
        {
            // Use the new bootstrapper for proper DI-based initialization
            _bootstrapper = new ApplicationBootstrapper();
            var container = _bootstrapper.BuildContainer();

            // Wire up deferred logging for ConfigProvider now that the container is built
            var configProvider = container.Resolve<IConfigProvider>();
            configProvider.SetLoggingServiceFactory(() => container.Resolve<ILoggingService>());

            return container;
        }

        private static void StartCoreService()
        {
            var coreService = Container.Resolve<ICoreService>();
            coreService.Start();

            if (IsHeadless())
            {
                // Container / detached / pipe-launched mode: stdin is
                // not a TTY, so the legacy Console.ReadLine() returned
                // immediately on EOF and the process exited right after
                // startup. Block until SIGINT (Ctrl+C) or SIGTERM
                // (`docker stop`) instead so the bot keeps running.
                var shutdown = new System.Threading.ManualResetEventSlim(false);
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    shutdown.Set();
                };
                AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdown.Set();
                shutdown.Wait();
            }
            else
            {
                Console.ReadLine();
            }

            coreService.Stop();
        }

        private static bool IsHeadless()
        {
            if (Console.IsInputRedirected)
            {
                return true;
            }

            var envOverride = Environment.GetEnvironmentVariable("INTELLITRADER_HEADLESS");
            return string.Equals(envOverride, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(envOverride, "1", StringComparison.Ordinal);
        }

        private static void PrintWelcomeMessage()
        {
            var foregroundColorBackup = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Welcome to IntelliTrader, The Intelligent Cryptocurrency Trading Bot.");
            Console.WriteLine("Always use Enter/Return key to exit the program to avoid corrupting the data.");
            Console.ForegroundColor = foregroundColorBackup;
        }

        private static void EncryptKeys(Dictionary<string, string> args)
        {
            var path = args["path"];
            var publicKey = args["publickey"];
            var privateKey = args["privatekey"];

            CryptoUtility.SaveUnprotectedStringsToFile(path, new string[] { publicKey, privateKey });
            Console.WriteLine("All done! Press any key to exit...");
            Console.ReadKey();
        }

        private static void PrintUsage()
        {
            Console.WriteLine();
            Console.WriteLine("Usage: dotnet IntelliTrader.dll --encrypt --path=<output_path> --publickey=<public_key> --privatekey=<private_key>");
            Console.WriteLine("The encrypted file is only valid for the current user and only on the computer it is created on.");
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static Dictionary<string, string> ParseCommandLineArgs(string[] args)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string a in args)
            {
                int idx = a.IndexOf('=');
                string key = (idx < 0 ? a.TrimStart('-') : a.Substring(0, idx)).ToLowerInvariant().TrimStart('-');
                string value = (idx < 0 ? string.Empty : a.Substring(idx + 1));
                dict[key] = value;
            }
            return dict;
        }
    }
}
