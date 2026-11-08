using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Terraria.ModLoader;

namespace TimeStop
{
	// Keeps time stops out of cutscenes. A time stop can never start while any cutscene is running, and one that
	// is running ends the normal way (the glass breaks) the moment a cutscene begins, so the cutscene plays on
	// untouched.
	//
	// "Cutscene" means any of these, checked every tick:
	//   - Wrath of the Gods has a cutscene in its cutscene list
	//   - Luminance (the library Wrath of the Gods builds on) has an active cutscene
	//   - Wrath of the Gods or Luminance is panning the camera for a cinematic moment (boss arrivals, deaths)
	//
	// Every signal is found by name when the game loads. If a mod update renames one, that one signal simply
	// switches off (and the log says so) instead of breaking anything.
	public class CutsceneCompat : ModSystem
	{
		private enum Check
		{
			NotEmpty,     // a collection with at least one entry
			NotNull,      // an object that is set
			AboveZero,    // a number above a small threshold
		}

		private sealed class Signal
		{
			public string Description;
			public MemberInfo Member;
			public object Instance;   // for members on a ModSystem instance; null for static members
			public Check Check;
		}

		private static readonly List<Signal> signals = new();

		public override void PostSetupContent() {
			signals.Clear();
			Find("NoxusBoss", "activeCutscenes", Check.NotEmpty, "Wrath of the Gods cutscene list");
			Find("NoxusBoss", "CameraPanInterpolant", Check.AboveZero, "Wrath of the Gods camera pan");
			Find("Luminance", "ActiveCutscene", Check.NotNull, "Luminance active cutscene");
			Find("Luminance", "CameraPanInterpolant", Check.AboveZero, "Luminance camera pan");
		}

		public override void Unload() {
			signals.Clear();
		}

		private void Find(string modName, string memberName, Check check, string description) {
			if (!ModLoader.TryGetMod(modName, out Mod other) || other.Code == null)
				return;

			const BindingFlags all = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			try {
				foreach (Type type in LoadableTypes(other.Code)) {
					MemberInfo member = (MemberInfo)type.GetProperty(memberName, all) ?? type.GetField(memberName, all);
					if (member == null)
						continue;

					bool isStatic = member is FieldInfo field ? field.IsStatic : ((PropertyInfo)member).GetMethod?.IsStatic == true;
					object instance = null;
					if (!isStatic) {
						// Instance members are only usable on a loaded ModSystem, which has exactly one instance.
						if (!typeof(ModSystem).IsAssignableFrom(type))
							continue;
						instance = GetModSystem(type);
						if (instance == null)
							continue;
					}

					signals.Add(new Signal { Description = description, Member = member, Instance = instance, Check = check });
					return;
				}
			}
			catch (Exception e) {
				Mod.Logger.Warn($"Could not look up {description}; that cutscene check is off", e);
				return;
			}

			Mod.Logger.Info($"{description} not found; that cutscene check is off");
		}

		// Some mods have types that depend on other optional mods. When those are off, GetTypes throws but
		// still hands back every type that did load.
		private static IEnumerable<Type> LoadableTypes(Assembly assembly) {
			Type[] types;
			try {
				types = assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException partial) {
				types = partial.Types;
			}

			foreach (Type type in types) {
				if (type != null)
					yield return type;
			}
		}

		private static object GetModSystem(Type type) {
			try {
				MethodInfo getInstance = typeof(ModContent).GetMethod(nameof(ModContent.GetInstance))?.MakeGenericMethod(type);
				return getInstance?.Invoke(null, null);
			}
			catch {
				return null;
			}
		}

		public static bool CutsceneActive() {
			foreach (Signal signal in signals) {
				try {
					object value = signal.Member is FieldInfo field ? field.GetValue(signal.Instance) : ((PropertyInfo)signal.Member).GetValue(signal.Instance);
					if (IsOn(value, signal.Check))
						return true;
				}
				catch {
					// Never let a lookup problem break the game.
				}
			}

			return false;
		}

		private static bool IsOn(object value, Check check) {
			switch (check) {
				case Check.NotNull:
					return value != null;

				case Check.AboveZero:
					return value switch {
						float f => f > 0.01f,
						double d => d > 0.01,
						int i => i > 0,
						_ => false,
					};

				case Check.NotEmpty:
					if (value is ICollection collection)
						return collection.Count > 0;
					if (value is IEnumerable enumerable) {
						foreach (object _ in enumerable)
							return true;
					}
					return false;
			}

			return false;
		}
	}
}
