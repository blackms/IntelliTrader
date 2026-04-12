using Autofac;
using ExchangeSharp;
using IntelliTrader.Core;
using IntelliTrader.Core.Security;
using System;
using System.Collections.Generic;
using System.IO;

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
                else if (parsedArgs.ContainsKey("encrypt-config") && parsedArgs.ContainsKey("password"))
                {
                    EncryptConfigFiles(parsedArgs["password"]);
                }
                else if (parsedArgs.ContainsKey("decrypt-config") && parsedArgs.ContainsKey("password"))
                {
                    DecryptConfigFiles(parsedArgs["password"]);
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

        private static void EncryptConfigFiles(string password)
        {
            var configDir = Path.Combine(Directory.GetCurrentDirectory(), "config");
            if (!Directory.Exists(configDir))
            {
                Console.WriteLine($"Config directory not found: {configDir}");
                return;
            }

            foreach (var file in Directory.GetFiles(configDir, "*.json"))
            {
                string content = File.ReadAllText(file);
                if (ConfigEncryption.IsEncrypted(content))
                {
                    Console.WriteLine($"  Skipped (already encrypted): {Path.GetFileName(file)}");
                    continue;
                }

                string encrypted = ConfigEncryption.Encrypt(content, password);
                File.WriteAllText(file, encrypted);
                Console.WriteLine($"  Encrypted: {Path.GetFileName(file)}");
            }

            Console.WriteLine();
            Console.WriteLine("All config files encrypted. Set INTELLITRADER_MASTER_PASSWORD to the same password at runtime.");
        }

        private static void DecryptConfigFiles(string password)
        {
            var configDir = Path.Combine(Directory.GetCurrentDirectory(), "config");
            if (!Directory.Exists(configDir))
            {
                Console.WriteLine($"Config directory not found: {configDir}");
                return;
            }

            foreach (var file in Directory.GetFiles(configDir, "*.json"))
            {
                string content = File.ReadAllText(file);
                if (!ConfigEncryption.IsEncrypted(content))
                {
                    Console.WriteLine($"  Skipped (not encrypted): {Path.GetFileName(file)}");
                    continue;
                }

                try
                {
                    string decrypted = ConfigEncryption.Decrypt(content, password);
                    File.WriteAllText(file, decrypted);
                    Console.WriteLine($"  Decrypted: {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  FAILED to decrypt {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("Config files decrypted.");
        }

        private static void PrintUsage()
        {
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine();
            Console.WriteLine("  Encrypt API keys:");
            Console.WriteLine("    dotnet IntelliTrader.dll --encrypt --path=<output_path> --publickey=<key> --privatekey=<secret>");
            Console.WriteLine();
            Console.WriteLine("  Encrypt config files:");
            Console.WriteLine("    dotnet IntelliTrader.dll --encrypt-config --password=<master_password>");
            Console.WriteLine();
            Console.WriteLine("  Decrypt config files:");
            Console.WriteLine("    dotnet IntelliTrader.dll --decrypt-config --password=<master_password>");
            Console.WriteLine();
            Console.WriteLine("  At runtime, set INTELLITRADER_MASTER_PASSWORD env var to auto-decrypt encrypted configs.");
            Console.WriteLine();
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
