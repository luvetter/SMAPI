﻿using System;
using StardewModdingAPI.Framework;

namespace StardewModdingAPI.Events
{
    /// <summary>Events raised when the game changes state.</summary>
    public static class GameEvents
    {
        /*********
        ** Properties
        *********/
        /// <summary>Manages deprecation warnings.</summary>
        private static DeprecationManager DeprecationManager;


        /*********
        ** Events
        *********/
        /// <summary>Raised during launch after configuring XNA or MonoGame. The game window hasn't been opened by this point. Called after <see cref="Microsoft.Xna.Framework.Game.Initialize"/>.</summary>
        internal static event EventHandler InitializeInternal;

        /// <summary>Raised during launch after configuring Stardew Valley, loading it into memory, and opening the game window. The window is still blank by this point.</summary>
        internal static event EventHandler GameLoadedInternal;

        /// <summary>Raised during launch after configuring XNA or MonoGame. The game window hasn't been opened by this point. Called after <see cref="Microsoft.Xna.Framework.Game.Initialize"/>.</summary>
        [Obsolete("The " + nameof(Mod) + "." + nameof(Mod.Entry) + " method is now called after the " + nameof(GameEvents.Initialize) + " event, so any contained logic can be done directly in " + nameof(Mod.Entry) + ".")]
        public static event EventHandler Initialize;

        /// <summary>Raised before XNA loads or reloads graphics resources. Called during <see cref="Microsoft.Xna.Framework.Game.LoadContent"/>.</summary>
        [Obsolete("The " + nameof(Mod) + "." + nameof(Mod.Entry) + " method is now called after the " + nameof(GameEvents.LoadContent) + " event, so any contained logic can be done directly in " + nameof(Mod.Entry) + ".")]
        public static event EventHandler LoadContent;

        /// <summary>Raised during launch after configuring Stardew Valley, loading it into memory, and opening the game window. The window is still blank by this point.</summary>
        [Obsolete("The " + nameof(Mod) + "." + nameof(Mod.Entry) + " method is now called after the game loads, so any contained logic can be done directly in " + nameof(Mod.Entry) + ".")]
        public static event EventHandler GameLoaded;

        /// <summary>Raised during the first game update tick.</summary>
        [Obsolete("The " + nameof(Mod) + "." + nameof(Mod.Entry) + " method is now called after the game loads, so any contained logic can be done directly in " + nameof(Mod.Entry) + ".")]
        public static event EventHandler FirstUpdateTick;

        /// <summary>Raised when the game updates its state (≈60 times per second).</summary>
        public static event EventHandler UpdateTick;

        /// <summary>Raised every other tick (≈30 times per second).</summary>
        public static event EventHandler SecondUpdateTick;

        /// <summary>Raised every fourth tick (≈15 times per second).</summary>
        public static event EventHandler FourthUpdateTick;

        /// <summary>Raised every eighth tick (≈8 times per second).</summary>
        public static event EventHandler EighthUpdateTick;

        /// <summary>Raised every 15th tick (≈4 times per second).</summary>
        public static event EventHandler QuarterSecondTick;

        /// <summary>Raised every 30th tick (≈twice per second).</summary>
        public static event EventHandler HalfSecondTick;

        /// <summary>Raised every 60th tick (≈once per second).</summary>
        public static event EventHandler OneSecondTick;


        /*********
        ** Internal methods
        *********/
        /// <summary>Injects types required for backwards compatibility.</summary>
        /// <param name="deprecationManager">Manages deprecation warnings.</param>
        internal static void Shim(DeprecationManager deprecationManager)
        {
            GameEvents.DeprecationManager = deprecationManager;
        }

        /// <summary>Raise an <see cref="Initialize"/> event.</summary>
        /// <param name="monitor">Encapsulates logging and monitoring.</param>
        internal static void InvokeInitialize(IMonitor monitor)
        {
            // notify SMAPI
            monitor.SafelyRaisePlainEvent($"{nameof(GameEvents)}.{nameof(GameEvents.InitializeInternal)}", GameEvents.InitializeInternal?.GetInvocationList());

            // notify mods
            if (GameEvents.Initialize == null)
                return;
            string name = $"{nameof(GameEvents)}.{nameof(GameEvents.Initialize)}";
            Delegate[] handlers = GameEvents.Initialize.GetInvocationList();
            GameEvents.DeprecationManager.WarnForEvent(handlers, name, "1.10", DeprecationLevel.Info);
            monitor.SafelyRaisePlainEvent(name, handlers);
        }

        /// <summary>Raise a <see cref="LoadContent"/> event.</summary>
        /// <param name="monitor">Encapsulates logging and monitoring.</param>
        internal static void InvokeLoadContent(IMonitor monitor)
        {
            if (GameEvents.LoadContent == null)
                return;

            string name = $"{nameof(GameEvents)}.{nameof(GameEvents.LoadContent)}";
            Delegate[] handlers = GameEvents.LoadContent.GetInvocationList();

            GameEvents.DeprecationManager.WarnForEvent(handlers, name, "1.10", DeprecationLevel.Info);
            monitor.SafelyRaisePlainEvent(name, handlers);
        }

        /// <summary>Raise a <see cref="GameLoaded"/> event.</summary>
        /// <param name="monitor">Encapsulates monitoring and logging.</param>
        internal static void InvokeGameLoaded(IMonitor monitor)
        {
            // notify SMAPI
            monitor.SafelyRaisePlainEvent($"{nameof(GameEvents)}.{nameof(GameEvents.GameLoadedInternal)}", GameEvents.GameLoadedInternal?.GetInvocationList());

            // notify mods
            if (GameEvents.GameLoaded == null)
                return;

            string name = $"{nameof(GameEvents)}.{nameof(GameEvents.GameLoaded)}";
            Delegate[] handlers = GameEvents.GameLoaded.GetInvocationList();

            GameEvents.DeprecationManager.WarnForEvent(handlers, name, "1.12", DeprecationLevel.Info);
            monitor.SafelyRaisePlainEvent(name, handlers);
        }

        /// <summary>Raise a <see cref="FirstUpdateTick"/> event.</summary>
        /// <param name="monitor">Encapsulates monitoring and logging.</param>
        internal static void InvokeFirstUpdateTick(IMonitor monitor)
        {
            if (GameEvents.FirstUpdateTick == null)
                return;

            string name = $"{nameof(GameEvents)}.{nameof(GameEvents.FirstUpdateTick)}";
            Delegate[] handlers = GameEvents.FirstUpdateTick.GetInvocationList();

            GameEvents.DeprecationManager.WarnForEvent(handlers, name, "1.12", DeprecationLevel.Info);
            monitor.SafelyRaisePlainEvent(name, handlers);
        }

        /// <summary>Raise an <see cref="UpdateTick"/> event.</summary>
        /// <param name="monitor">Encapsulates logging and monitoring.</param>
        internal static void InvokeUpdateTick(IMonitor monitor)
        {
            monitor.SafelyRaisePlainEvent($"{nameof(GameEvents)}.{nameof(GameEvents.UpdateTick)}", GameEvents.UpdateTick?.GetInvocationList());
        }

        /// <summary>Raise a <see cref="SecondUpdateTick"/> event.</summary>
        /// <param name="monitor">Encapsulates monitoring and logging.</param>
        internal static void InvokeSecondUpdateTick(IMonitor monitor)
        {
            monitor.SafelyRaisePlainEvent($"{nameof(GameEvents)}.{nameof(GameEvents.SecondUpdateTick)}", GameEvents.SecondUpdateTick?.GetInvocationList());
        }

        /// <summary>Raise a <see cref="FourthUpdateTick"/> event.</summary>
        /// <param name="monitor">Encapsulates monitoring and logging.</param>
        internal static void InvokeFourthUpdateTick(IMonitor monitor)
        {
            monitor.SafelyRaisePlainEvent($"{nameof(GameEvents)}.{nameof(GameEvents.FourthUpdateTick)}", GameEvents.FourthUpdateTick?.GetInvocationList());
        }

        /// <summary>Raise a <see cref="EighthUpdateTick"/> event.</summary>
        /// <param name="monitor">Encapsulates monitoring and logging.</param>
        internal static void InvokeEighthUpdateTick(IMonitor monitor)
        {
            monitor.SafelyRaisePlainEvent($"{nameof(GameEvents)}.{nameof(GameEvents.EighthUpdateTick)}", GameEvents.EighthUpdateTick?.GetInvocationList());
        }

        /// <summary>Raise a <see cref="QuarterSecondTick"/> event.</summary>
        /// <param name="monitor">Encapsulates monitoring and logging.</param>
        internal static void InvokeQuarterSecondTick(IMonitor monitor)
        {
            monitor.SafelyRaisePlainEvent($"{nameof(GameEvents)}.{nameof(GameEvents.QuarterSecondTick)}", GameEvents.QuarterSecondTick?.GetInvocationList());
        }

        /// <summary>Raise a <see cref="HalfSecondTick"/> event.</summary>
        /// <param name="monitor">Encapsulates monitoring and logging.</param>
        internal static void InvokeHalfSecondTick(IMonitor monitor)
        {
            monitor.SafelyRaisePlainEvent($"{nameof(GameEvents)}.{nameof(GameEvents.HalfSecondTick)}", GameEvents.HalfSecondTick?.GetInvocationList());
        }

        /// <summary>Raise a <see cref="OneSecondTick"/> event.</summary>
        /// <param name="monitor">Encapsulates monitoring and logging.</param>
        internal static void InvokeOneSecondTick(IMonitor monitor)
        {
            monitor.SafelyRaisePlainEvent($"{nameof(GameEvents)}.{nameof(GameEvents.OneSecondTick)}", GameEvents.OneSecondTick?.GetInvocationList());
        }
    }
}
