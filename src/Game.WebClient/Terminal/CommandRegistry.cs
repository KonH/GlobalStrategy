using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GS.Game.Commands;

namespace GS.Game.WebClient.Terminal {
	// Describes one settable member (public field, or public-settable record positional
	// property) of a discovered ICommand type. IsRequired is false only for a record
	// positional property whose primary-constructor parameter carries a default value
	// (e.g. SaveGameCommand(bool IsAutoSave = false)) - plain structs with public fields
	// have no such concept in C#, so every field-based parameter is always required.
	public class CommandParameter {
		public string Name { get; }
		public Type Type { get; }
		public bool IsRequired { get; }
		public object? DefaultValue { get; }

		// The ParamSuggestionAttribute (CountryId/OrgId/OneOf/etc.) declared on this
		// parameter's field or property, if any - drives which value-suggestion provider
		// SuggestionValueResolver picks. Null means no attribute-driven suggestions (enum
		// auto-suggest or an empty list is decided by the resolver from Type instead).
		public ParamSuggestionAttribute? Suggestion { get; }

		readonly FieldInfo? _field;
		readonly PropertyInfo? _property;

		public CommandParameter(FieldInfo field) {
			Name = field.Name;
			Type = field.FieldType;
			IsRequired = true;
			DefaultValue = null;
			Suggestion = field.GetCustomAttribute<ParamSuggestionAttribute>();
			_field = field;
		}

		public CommandParameter(PropertyInfo property, bool isRequired, object? defaultValue) {
			Name = property.Name;
			Type = property.PropertyType;
			IsRequired = isRequired;
			DefaultValue = defaultValue;
			Suggestion = property.GetCustomAttribute<ParamSuggestionAttribute>();
			_property = property;
		}

		// Mutates the boxed struct instance in place - the caller keeps reusing the same
		// boxed reference (SetValue on a boxed value type writes into that box, it does
		// not silently operate on a throwaway copy) until it is finally passed to Push.
		public void SetValue(object boxedInstance, object? value) {
			if (_field != null) {
				_field.SetValue(boxedInstance, value);
			} else {
				_property!.SetValue(boxedInstance, value);
			}
		}
	}

	// Describes one discovered ICommand type: its terminal-facing name and settable
	// parameters (public fields, or public-settable record positional properties).
	public class CommandDescriptor {
		public string Name { get; }
		public Type Type { get; }
		public IReadOnlyList<CommandParameter> Parameters { get; }

		public CommandDescriptor(string name, Type type, IReadOnlyList<CommandParameter> parameters) {
			Name = name;
			Type = type;
			Parameters = parameters;
		}
	}

	// Reflects over the same closed ICommand set src/Game.SourceGenerators/CommandGenerator.cs
	// enumerates at compile time to generate CommandAccessor - so "new command = just a new
	// build" holds for the terminal too, with zero web client code changes.
	public class CommandRegistry {
		const string CommandSuffix = "Command";

		readonly Dictionary<string, CommandDescriptor> _commandsByName;

		public CommandRegistry() : this(typeof(ICommand).Assembly) {
		}

		public CommandRegistry(Assembly assembly) {
			// Case-insensitive lookup - the terminal is a debug convenience, not a strict shell.
			_commandsByName = Discover(assembly).ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
		}

		public IReadOnlyCollection<CommandDescriptor> Commands => _commandsByName.Values;

		public CommandDescriptor? Find(string name) {
			return _commandsByName.TryGetValue(name, out var descriptor) ? descriptor : null;
		}

		static IEnumerable<CommandDescriptor> Discover(Assembly assembly) {
			var commandTypes = assembly.GetTypes()
				.Where(t => typeof(ICommand).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
			foreach (var type in commandTypes) {
				yield return new CommandDescriptor(StripCommandSuffix(type.Name), type, DiscoverParameters(type));
			}
		}

		static string StripCommandSuffix(string typeName) {
			return typeName.EndsWith(CommandSuffix, StringComparison.Ordinal)
				? typeName.Substring(0, typeName.Length - CommandSuffix.Length)
				: typeName;
		}

		static IReadOnlyList<CommandParameter> DiscoverParameters(Type type) {
			var parameters = new List<CommandParameter>();

			foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
				parameters.Add(new CommandParameter(field));
			}

			// Record positional properties correspond 1:1 with the primary constructor's
			// parameters - that is the only place default-value information lives (reflecting
			// on the property alone cannot tell "required" from "optional").
			var primaryConstructorParameters = FindPrimaryConstructorParameters(type);
			foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
				if (!property.CanWrite) {
					continue;
				}
				var ctorParameter = primaryConstructorParameters
					.FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.Ordinal));
				bool isRequired = ctorParameter == null || !ctorParameter.HasDefaultValue;
				object? defaultValue = ctorParameter?.HasDefaultValue == true ? ctorParameter.DefaultValue : null;
				parameters.Add(new CommandParameter(property, isRequired, defaultValue));
			}

			return parameters;
		}

		static ParameterInfo[] FindPrimaryConstructorParameters(Type type) {
			// The primary constructor of a record struct is the one whose parameter count
			// matches the number of positional properties; picking the constructor with the
			// most parameters is a reliable proxy since record structs also synthesize a
			// parameterless constructor for structs, and plain structs have no declared ctor
			// with defaults relevant here at all.
			var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
			var best = constructors.OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
			return best?.GetParameters() ?? Array.Empty<ParameterInfo>();
		}
	}
}
