﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
#if SMAPI_FOR_WINDOWS
using System.Management;
using System.Windows.Forms;
#endif
using Newtonsoft.Json;
using StardewModdingAPI.AssemblyRewriters;
using StardewModdingAPI.Events;
using StardewModdingAPI.Framework;
using StardewModdingAPI.Framework.Logging;
using StardewModdingAPI.Framework.Models;
using StardewModdingAPI.Framework.ModLoading;
using StardewModdingAPI.Framework.Reflection;
using StardewModdingAPI.Framework.Serialisation;
using StardewValley;
using Monitor = StardewModdingAPI.Framework.Monitor;
using SObject = StardewValley.Object;

namespace StardewModdingAPI
{
    /// <summary>The main entry point for SMAPI, responsible for hooking into and launching the game.</summary>
    internal class Program : IDisposable
    {
        /*********
        ** Properties
        *********/
        /// <summary>The log file to which to write messages.</summary>
        private readonly LogFileManager LogFile;

        /// <summary>Manages console output interception.</summary>
        private readonly ConsoleInterceptionManager ConsoleManager = new ConsoleInterceptionManager();

        /// <summary>The core logger and monitor for SMAPI.</summary>
        private readonly Monitor Monitor;

        /// <summary>Tracks whether the game should exit immediately and any pending initialisation should be cancelled.</summary>
        private readonly CancellationTokenSource CancellationTokenSource = new CancellationTokenSource();

        /// <summary>Simplifies access to private game code.</summary>
        private readonly IReflectionHelper Reflection = new ReflectionHelper();

        /// <summary>The underlying game instance.</summary>
        private SGame GameInstance;

        /// <summary>The SMAPI configuration settings.</summary>
        /// <remarks>This is initialised after the game starts.</remarks>
        private SConfig Settings;

        /// <summary>Tracks the installed mods.</summary>
        /// <remarks>This is initialised after the game starts.</remarks>
        private ModRegistry ModRegistry;

        /// <summary>Manages deprecation warnings.</summary>
        /// <remarks>This is initialised after the game starts.</remarks>
        private DeprecationManager DeprecationManager;

        /// <summary>Manages console commands.</summary>
        /// <remarks>This is initialised after the game starts.</remarks>
        private CommandManager CommandManager;

        /// <summary>Whether the game is currently running.</summary>
        private bool IsGameRunning;

        /// <summary>Whether the program has been disposed.</summary>
        private bool IsDisposed;


        /*********
        ** Public methods
        *********/
        /// <summary>The main entry point which hooks into and launches the game.</summary>
        /// <param name="args">The command-line arguments.</param>
        public static void Main(string[] args)
        {
            // get flags from arguments
            bool writeToConsole = !args.Contains("--no-terminal");

            // get log path from arguments
            string logPath = null;
            {
                int pathIndex = Array.LastIndexOf(args, "--log-path") + 1;
                if (pathIndex >= 1 && args.Length >= pathIndex)
                {
                    logPath = args[pathIndex];
                    if (!Path.IsPathRooted(logPath))
                        logPath = Path.Combine(Constants.LogDir, logPath);
                }
            }
            if (string.IsNullOrWhiteSpace(logPath))
                logPath = Constants.DefaultLogPath;

            // load SMAPI
            using (Program program = new Program(writeToConsole, logPath))
                program.RunInteractively();
        }

        /// <summary>Construct an instance.</summary>
        /// <param name="writeToConsole">Whether to output log messages to the console.</param>
        /// <param name="logPath">The full file path to which to write log messages.</param>
        public Program(bool writeToConsole, string logPath)
        {
            this.LogFile = new LogFileManager(logPath);
            this.Monitor = new Monitor("SMAPI", this.ConsoleManager, this.LogFile, this.CancellationTokenSource) { WriteToConsole = writeToConsole };
        }

        /// <summary>Launch SMAPI.</summary>
        public void RunInteractively()
        {
            // initialise SMAPI
            try
            {
                // init logging
                this.Monitor.Log($"SMAPI {Constants.ApiVersion} with Stardew Valley {Constants.GetGameDisplayVersion(Constants.GameVersion)} on {this.GetFriendlyPlatformName()}", LogLevel.Info);
                this.Monitor.Log($"Mods go here: {Constants.ModPath}");
                this.Monitor.Log("Preparing SMAPI...");

                // validate paths
                this.VerifyPath(Constants.ModPath);
                this.VerifyPath(Constants.LogDir);

                // validate game version
                if (Constants.GameVersion.IsOlderThan(Constants.MinimumGameVersion))
                {
                    this.Monitor.Log($"Oops! You're running Stardew Valley {Constants.GetGameDisplayVersion(Constants.GameVersion)}, but the oldest supported version is {Constants.GetGameDisplayVersion(Constants.MinimumGameVersion)}. Please update your game before using SMAPI. If you have the beta version on Steam, you may need to opt out to get the latest non-beta updates.", LogLevel.Error);
                    this.PressAnyKeyToExit();
                    return;
                }
                if (Constants.MaximumGameVersion != null && Constants.GameVersion.IsNewerThan(Constants.MaximumGameVersion))
                {
                    this.Monitor.Log($"Oops! You're running Stardew Valley {Constants.GetGameDisplayVersion(Constants.GameVersion)}, but this version of SMAPI is only compatible up to Stardew Valley {Constants.GetGameDisplayVersion(Constants.MaximumGameVersion)}. Please check for a newer version of SMAPI.", LogLevel.Error);
                    this.PressAnyKeyToExit();
                    return;
                }

                // add error handlers
#if SMAPI_FOR_WINDOWS
                Application.ThreadException += (sender, e) => this.Monitor.Log($"Critical thread exception: {e.Exception.GetLogSummary()}", LogLevel.Error);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
#endif
                AppDomain.CurrentDomain.UnhandledException += (sender, e) => this.Monitor.Log($"Critical app domain exception: {e.ExceptionObject}", LogLevel.Error);

                // override game
                this.GameInstance = new SGame(this.Monitor, this.Reflection);
                StardewValley.Program.gamePtr = this.GameInstance;

                // add exit handler
                new Thread(() =>
                {
                    this.CancellationTokenSource.Token.WaitHandle.WaitOne();
                    if (this.IsGameRunning)
                    {
                        try
                        {
                            File.WriteAllText(Constants.FatalCrashMarker, string.Empty);
                            File.Copy(Constants.DefaultLogPath, Constants.FatalCrashLog, overwrite: true);
                        }
                        catch (Exception ex)
                        {
                            this.Monitor.Log($"SMAPI failed trying to track the crash details: {ex.GetLogSummary()}");
                        }

                        this.GameInstance.Exit();
                    }
                }).Start();

                // hook into game events
#if SMAPI_FOR_WINDOWS
                ((Form)Control.FromHandle(this.GameInstance.Window.Handle)).FormClosing += (sender, args) => this.Dispose();
#endif
                this.GameInstance.Exiting += (sender, e) => this.Dispose();
                this.GameInstance.Window.ClientSizeChanged += (sender, e) => GraphicsEvents.InvokeResize(this.Monitor, sender, e);
                GameEvents.InitializeInternal += (sender, e) => this.InitialiseAfterGameStart();
                GameEvents.GameLoadedInternal += (sender, e) => this.CheckForUpdateAsync();

                // set window titles
                this.GameInstance.Window.Title = $"Stardew Valley {Constants.GetGameDisplayVersion(Constants.GameVersion)} - running SMAPI {Constants.ApiVersion}";
                Console.Title = $"SMAPI {Constants.ApiVersion} - running Stardew Valley {Constants.GetGameDisplayVersion(Constants.GameVersion)}";
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"SMAPI failed to initialise: {ex.GetLogSummary()}", LogLevel.Error);
                this.PressAnyKeyToExit();
                return;
            }

            // show details if game crashed during last session
            if (File.Exists(Constants.FatalCrashMarker))
            {
                this.Monitor.Log("The game crashed last time you played. That can be due to bugs in the game, but if it happens repeatedly you can ask for help here: http://community.playstarbound.com/threads/108375/.", LogLevel.Error);
                this.Monitor.Log($"If you ask for help, make sure to attach this file: {Constants.FatalCrashLog}", LogLevel.Error);
                this.Monitor.Log("Press any key to delete the crash data and continue playing.", LogLevel.Info);
                Console.ReadKey();
                File.Delete(Constants.FatalCrashLog);
                File.Delete(Constants.FatalCrashMarker);
            }

            // start game
            this.Monitor.Log("Starting game...");
            try
            {
                this.IsGameRunning = true;
                this.GameInstance.Run();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"The game failed unexpectedly: {ex.GetLogSummary()}", LogLevel.Error);
                this.PressAnyKeyToExit();
            }
            finally
            {
                this.Dispose();
            }
        }

        /// <summary>Get a monitor for legacy code which doesn't have one passed in.</summary>
        [Obsolete("This method should only be used when needed for backwards compatibility.")]
        internal IMonitor GetLegacyMonitorForMod()
        {
            string modName = this.ModRegistry.GetModFromStack() ?? "unknown";
            return this.GetSecondaryMonitor(modName);
        }

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        public void Dispose()
        {
            this.Monitor.Log("Disposing...", LogLevel.Trace);

            // skip if already disposed
            if (this.IsDisposed)
                return;
            this.IsDisposed = true;

            // dispose mod data
            foreach (IModMetadata mod in this.ModRegistry.GetMods())
            {
                try
                {
                    (mod.Mod as IDisposable)?.Dispose();
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"The {mod.DisplayName} mod failed during disposal: {ex.GetLogSummary()}.", LogLevel.Warn);
                }
            }

            // dispose core components
            this.IsGameRunning = false;
            this.LogFile?.Dispose();
            this.ConsoleManager?.Dispose();
            this.CancellationTokenSource?.Dispose();
            this.GameInstance?.Dispose();
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Initialise SMAPI and mods after the game starts.</summary>
        private void InitialiseAfterGameStart()
        {
            // load settings
            this.Settings = JsonConvert.DeserializeObject<SConfig>(File.ReadAllText(Constants.ApiConfigPath));
            this.GameInstance.VerboseLogging = this.Settings.VerboseLogging;

            // load core components
            this.ModRegistry = new ModRegistry();
            this.DeprecationManager = new DeprecationManager(this.Monitor, this.ModRegistry);
            this.CommandManager = new CommandManager();

            // inject compatibility shims
#pragma warning disable 618
            Command.Shim(this.CommandManager, this.DeprecationManager, this.ModRegistry);
            Config.Shim(this.DeprecationManager);
            InternalExtensions.Shim(this.ModRegistry);
            Log.Shim(this.DeprecationManager, this.GetSecondaryMonitor("legacy mod"), this.ModRegistry);
            Mod.Shim(this.DeprecationManager);
            ContentEvents.Shim(this.ModRegistry, this.Monitor);
            GameEvents.Shim(this.DeprecationManager);
            PlayerEvents.Shim(this.DeprecationManager);
            TimeEvents.Shim(this.DeprecationManager);
#pragma warning restore 618

            // redirect direct console output
            {
                Monitor monitor = this.GetSecondaryMonitor("Console.Out");
                if (monitor.WriteToConsole)
                    this.ConsoleManager.OnMessageIntercepted += message => this.HandleConsoleMessage(monitor, message);
            }

            // add headers
            if (this.Settings.DeveloperMode)
            {
                this.Monitor.ShowTraceInConsole = true;
                this.Monitor.Log($"You configured SMAPI to run in developer mode. The console may be much more verbose. You can disable developer mode by installing the non-developer version of SMAPI, or by editing {Constants.ApiConfigPath}.", LogLevel.Info);
            }
            if (!this.Settings.CheckForUpdates)
                this.Monitor.Log($"You configured SMAPI to not check for updates. Running an old version of SMAPI is not recommended. You can enable update checks by reinstalling SMAPI or editing {Constants.ApiConfigPath}.", LogLevel.Warn);
            if (!this.Monitor.WriteToConsole)
                this.Monitor.Log("Writing to the terminal is disabled because the --no-terminal argument was received. This usually means launching the terminal failed.", LogLevel.Warn);
            if (this.Settings.VerboseLogging)
                this.Monitor.Log("Verbose logging enabled.", LogLevel.Trace);

            // validate XNB integrity
            if (!this.ValidateContentIntegrity())
                this.Monitor.Log("SMAPI found problems in the game's XNB files which may cause errors or crashes while you're playing. Consider uninstalling XNB mods or reinstalling the game.", LogLevel.Warn);

            // load mods
            int modsLoaded;
            {
                this.Monitor.Log("Loading mod metadata...");
                ModResolver resolver = new ModResolver();

                // load manifests
                IModMetadata[] mods = resolver.ReadManifests(Constants.ModPath, new JsonHelper(), this.Settings.ModCompatibility).ToArray();
                resolver.ValidateManifests(mods, Constants.ApiVersion);

                // check for deprecated metadata
                IList<Action> deprecationWarnings = new List<Action>();
                foreach (IModMetadata mod in mods.Where(m => m.Status != ModMetadataStatus.Failed))
                {
                    // missing fields that will be required in SMAPI 2.0
                    {
                        List<string> missingFields = new List<string>(3);

                        if (string.IsNullOrWhiteSpace(mod.Manifest.Name))
                            missingFields.Add(nameof(IManifest.Name));
                        if (mod.Manifest.Version == null || mod.Manifest.Version.ToString() == "0.0")
                            missingFields.Add(nameof(IManifest.Version));
                        if (string.IsNullOrWhiteSpace(mod.Manifest.UniqueID))
                            missingFields.Add(nameof(IManifest.UniqueID));

                        if (missingFields.Any())
                            deprecationWarnings.Add(() => this.Monitor.Log($"{mod.DisplayName} is missing some manifest fields ({string.Join(", ", missingFields)}) which will be required in an upcoming SMAPI version.", LogLevel.Warn));
                    }

                    // per-save directories
                    if ((mod.Manifest as Manifest)?.PerSaveConfigs == true)
                    {
                        deprecationWarnings.Add(() => this.DeprecationManager.Warn(mod.DisplayName, $"{nameof(Manifest)}.{nameof(Manifest.PerSaveConfigs)}", "1.0", DeprecationLevel.Info));
                        try
                        {
                            string psDir = Path.Combine(mod.DirectoryPath, "psconfigs");
                            Directory.CreateDirectory(psDir);
                            if (!Directory.Exists(psDir))
                                mod.SetStatus(ModMetadataStatus.Failed, "it requires per-save configuration files ('psconfigs') which couldn't be created for some reason.");
                        }
                        catch (Exception ex)
                        {
                            mod.SetStatus(ModMetadataStatus.Failed, $"it requires per-save configuration files ('psconfigs') which couldn't be created: {ex.GetLogSummary()}");
                        }
                    }
                }

#if EXPERIMENTAL
                // process dependencies
                mods = resolver.ProcessDependencies(mods).ToArray();
#endif

                // load mods
                modsLoaded = this.LoadMods(mods, new JsonHelper(), (SContentManager)Game1.content, deprecationWarnings);
                foreach (Action warning in deprecationWarnings)
                    warning();
            }
            if (this.Monitor.IsExiting)
            {
                this.Monitor.Log("SMAPI shutting down: aborting initialisation.", LogLevel.Warn);
                return;
            }

            // update window titles
            this.GameInstance.Window.Title = $"Stardew Valley {Constants.GetGameDisplayVersion(Constants.GameVersion)} - running SMAPI {Constants.ApiVersion} with {modsLoaded} mods";
            Console.Title = $"SMAPI {Constants.ApiVersion} - running Stardew Valley {Constants.GetGameDisplayVersion(Constants.GameVersion)} with {modsLoaded} mods";

            // start SMAPI console
            new Thread(this.RunConsoleLoop).Start();
        }

        /// <summary>Run a loop handling console input.</summary>
        [SuppressMessage("ReSharper", "FunctionNeverReturns", Justification = "The thread is aborted when the game exits.")]
        private void RunConsoleLoop()
        {
            // prepare help command
            this.Monitor.Log("Starting console...");
            this.Monitor.Log("Type 'help' for help, or 'help <cmd>' for a command's usage", LogLevel.Info);
            this.CommandManager.Add("SMAPI", "help", "Lists all commands | 'help <cmd>' returns command description", this.HandleHelpCommand);

            // start handling command line input
            Thread inputThread = new Thread(() =>
            {
                while (true)
                {
                    string input = Console.ReadLine();
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(input) && !this.CommandManager.Trigger(input))
                            this.Monitor.Log("Unknown command; type 'help' for a list of available commands.", LogLevel.Error);
                    }
                    catch (Exception ex)
                    {
                        this.Monitor.Log($"The handler registered for that command failed:\n{ex.GetLogSummary()}", LogLevel.Error);
                    }
                }
            });
            inputThread.Start();

            // keep console thread alive while the game is running
            while (this.IsGameRunning && !this.Monitor.IsExiting)
                Thread.Sleep(1000 / 10);
            if (inputThread.ThreadState == ThreadState.Running)
                inputThread.Abort();
        }

        /// <summary>Look for common issues with the game's XNB content, and log warnings if anything looks broken or outdated.</summary>
        /// <returns>Returns whether all integrity checks passed.</returns>
        private bool ValidateContentIntegrity()
        {
            this.Monitor.Log("Detecting common issues...");
            bool issuesFound = false;


            // object format (commonly broken by outdated files)
            {
                void LogIssue(int id, string issue) => this.Monitor.Log($"Detected issue: item #{id} in Content\\Data\\ObjectInformation is invalid ({issue}).", LogLevel.Warn);
                foreach (KeyValuePair<int, string> entry in Game1.objectInformation)
                {
                    // must not be empty
                    if (string.IsNullOrWhiteSpace(entry.Value))
                    {
                        LogIssue(entry.Key, "entry is empty");
                        issuesFound = true;
                        continue;
                    }

                    // require core fields
                    string[] fields = entry.Value.Split('/');
                    if (fields.Length < SObject.objectInfoDescriptionIndex + 1)
                    {
                        LogIssue(entry.Key, "too few fields for an object");
                        issuesFound = true;
                        continue;
                    }

                    // check min length for specific types
                    switch (fields[SObject.objectInfoTypeIndex].Split(new[] { ' ' }, 2)[0])
                    {
                        case "Cooking":
                            if (fields.Length < SObject.objectInfoBuffDurationIndex + 1)
                            {
                                LogIssue(entry.Key, "too few fields for a cooking item");
                                issuesFound = true;
                            }
                            break;
                    }
                }
            }

            return !issuesFound;
        }

        /// <summary>Asynchronously check for a new version of SMAPI, and print a message to the console if an update is available.</summary>
        private void CheckForUpdateAsync()
        {
            if (!this.Settings.CheckForUpdates)
                return;

            new Thread(() =>
            {
                try
                {
                    GitRelease release = UpdateHelper.GetLatestVersionAsync(Constants.GitHubRepository).Result;
                    ISemanticVersion latestVersion = new SemanticVersion(release.Tag);
                    if (latestVersion.IsNewerThan(Constants.ApiVersion))
                        this.Monitor.Log($"You can update SMAPI from version {Constants.ApiVersion} to {latestVersion}", LogLevel.Alert);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Couldn't check for a new version of SMAPI. This won't affect your game, but you may not be notified of new versions if this keeps happening.\n{ex.GetLogSummary()}");
                }
            }).Start();
        }

        /// <summary>Create a directory path if it doesn't exist.</summary>
        /// <param name="path">The directory path.</param>
        private void VerifyPath(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't create a path: {path}\n\n{ex.GetLogSummary()}", LogLevel.Error);
            }
        }

        /// <summary>Load and hook up the given mods.</summary>
        /// <param name="mods">The mods to load.</param>
        /// <param name="jsonHelper">The JSON helper with which to read mods' JSON files.</param>
        /// <param name="contentManager">The content manager to use for mod content.</param>
        /// <param name="deprecationWarnings">A list to populate with any deprecation warnings.</param>
        /// <returns>Returns the number of mods successfully loaded.</returns>
        private int LoadMods(IModMetadata[] mods, JsonHelper jsonHelper, SContentManager contentManager, IList<Action> deprecationWarnings)
        {
            this.Monitor.Log("Loading mods...");
            void LogSkip(IModMetadata mod, string reasonPhrase, LogLevel level = LogLevel.Error) => this.Monitor.Log($"Skipped {mod.DisplayName} because {reasonPhrase}", level);

            // load mod assemblies
            int modsLoaded = 0;
            AssemblyLoader modAssemblyLoader = new AssemblyLoader(Constants.TargetPlatform, this.Monitor);
            AppDomain.CurrentDomain.AssemblyResolve += (sender, e) => modAssemblyLoader.ResolveAssembly(e.Name);
            foreach (IModMetadata metadata in mods)
            {
                // validate status
                if (metadata.Status == ModMetadataStatus.Failed)
                {
                    LogSkip(metadata, metadata.Error);
                    continue;
                }

                // get basic info
                IManifest manifest = metadata.Manifest;
                string assemblyPath = Path.Combine(metadata.DirectoryPath, metadata.Manifest.EntryDll);

                // preprocess & load mod assembly
                Assembly modAssembly;
                try
                {
                    modAssembly = modAssemblyLoader.Load(assemblyPath, assumeCompatible: metadata.Compatibility?.Compatibility == ModCompatibilityType.AssumeCompatible);
                }
                catch (IncompatibleInstructionException ex)
                {
                    LogSkip(metadata, $"it's not compatible with the latest version of the game (detected {ex.NounPhrase}). Please check for a newer version of the mod (you have v{manifest.Version}).");
                    continue;
                }
                catch (Exception ex)
                {
                    LogSkip(metadata, $"its DLL '{manifest.EntryDll}' couldn't be loaded:\n{ex.GetLogSummary()}");
                    continue;
                }

                // validate assembly
                try
                {
                    int modEntries = modAssembly.DefinedTypes.Count(type => typeof(Mod).IsAssignableFrom(type) && !type.IsAbstract);
                    if (modEntries == 0)
                    {
                        LogSkip(metadata, $"its DLL has no '{nameof(Mod)}' subclass.");
                        continue;
                    }
                    if (modEntries > 1)
                    {
                        LogSkip(metadata, $"its DLL contains multiple '{nameof(Mod)}' subclasses.");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    LogSkip(metadata, $"its DLL couldn't be loaded:\n{ex.GetLogSummary()}");
                    continue;
                }

                // initialise mod
                try
                {
                    // get implementation
                    TypeInfo modEntryType = modAssembly.DefinedTypes.First(type => typeof(Mod).IsAssignableFrom(type) && !type.IsAbstract);
                    Mod mod = (Mod)modAssembly.CreateInstance(modEntryType.ToString());
                    if (mod == null)
                    {
                        LogSkip(metadata, "its entry class couldn't be instantiated.");
                        continue;
                    }

                    // inject data
                    mod.ModManifest = manifest;
                    mod.Helper = new ModHelper(metadata.DisplayName, manifest, metadata.DirectoryPath, jsonHelper, this.ModRegistry, this.CommandManager, contentManager, this.Reflection);
                    mod.Monitor = this.GetSecondaryMonitor(metadata.DisplayName);
                    mod.PathOnDisk = metadata.DirectoryPath;

                    // track mod
                    metadata.SetMod(mod);
                    this.ModRegistry.Add(metadata);
                    modsLoaded += 1;
                    this.Monitor.Log($"Loaded {metadata.DisplayName} by {manifest.Author}, v{manifest.Version} | {manifest.Description}", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    LogSkip(metadata, $"initialisation failed:\n{ex.GetLogSummary()}");
                }
            }

            // initialise loaded mods
            foreach (IModMetadata metadata in this.ModRegistry.GetMods())
            {
                try
                {
                    IMod mod = metadata.Mod;

                    // call entry methods
                    (mod as Mod)?.Entry(); // deprecated since 1.0
                    mod.Entry(mod.Helper);

                    // raise deprecation warning for old Entry() methods
                    if (this.DeprecationManager.IsVirtualMethodImplemented(mod.GetType(), typeof(Mod), nameof(Mod.Entry), new[] { typeof(object[]) }))
                        deprecationWarnings.Add(() => this.DeprecationManager.Warn(metadata.DisplayName, $"{nameof(Mod)}.{nameof(Mod.Entry)}(object[]) instead of {nameof(Mod)}.{nameof(Mod.Entry)}({nameof(IModHelper)})", "1.0", DeprecationLevel.Info));
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"The {metadata.DisplayName} mod failed on entry initialisation. It will still be loaded, but may not function correctly.\n{ex.GetLogSummary()}", LogLevel.Warn);
                }
            }

            // print result
            this.Monitor.Log($"Loaded {modsLoaded} mods.");
            return modsLoaded;
        }

        /// <summary>The method called when the user submits the help command in the console.</summary>
        /// <param name="name">The command name.</param>
        /// <param name="arguments">The command arguments.</param>
        private void HandleHelpCommand(string name, string[] arguments)
        {
            if (arguments.Any())
            {
                Framework.Command result = this.CommandManager.Get(arguments[0]);
                if (result == null)
                    this.Monitor.Log("There's no command with that name.", LogLevel.Error);
                else
                    this.Monitor.Log($"{result.Name}: {result.Documentation}\n(Added by {result.ModName}.)", LogLevel.Info);
            }
            else
            {
                this.Monitor.Log("The following commands are registered: " + string.Join(", ", this.CommandManager.GetAll().Select(p => p.Name)) + ".", LogLevel.Info);
                this.Monitor.Log("For more information about a command, type 'help command_name'.", LogLevel.Info);
            }
        }

        /// <summary>Redirect messages logged directly to the console to the given monitor.</summary>
        /// <param name="monitor">The monitor with which to log messages.</param>
        /// <param name="message">The message to log.</param>
        private void HandleConsoleMessage(IMonitor monitor, string message)
        {
            LogLevel level = message.Contains("Exception") ? LogLevel.Error : LogLevel.Trace; // intercept potential exceptions
            monitor.Log(message, level);
        }

        /// <summary>Show a 'press any key to exit' message, and exit when they press a key.</summary>
        private void PressAnyKeyToExit()
        {
            this.Monitor.Log("Game has ended. Press any key to exit.", LogLevel.Info);
            Thread.Sleep(100);
            Console.ReadKey();
            Environment.Exit(0);
        }

        /// <summary>Get a monitor instance derived from SMAPI's current settings.</summary>
        /// <param name="name">The name of the module which will log messages with this instance.</param>
        private Monitor GetSecondaryMonitor(string name)
        {
            return new Monitor(name, this.ConsoleManager, this.LogFile, this.CancellationTokenSource) { WriteToConsole = this.Monitor.WriteToConsole, ShowTraceInConsole = this.Settings.DeveloperMode };
        }

        /// <summary>Get a human-readable name for the current platform.</summary>
        [SuppressMessage("ReSharper", "EmptyGeneralCatchClause", Justification = "Error suppressed deliberately to fallback to default behaviour.")]
        private string GetFriendlyPlatformName()
        {
#if SMAPI_FOR_WINDOWS
            try
            {
                return new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem")
                    .Get()
                    .Cast<ManagementObject>()
                    .Select(entry => entry.GetPropertyValue("Caption").ToString())
                    .FirstOrDefault();
            }
            catch { }
#endif
            return Environment.OSVersion.ToString();
        }
    }
}
