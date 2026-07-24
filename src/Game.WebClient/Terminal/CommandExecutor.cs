using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GS.Main;

namespace GS.Game.WebClient.Terminal {
	// Never throws for user-input mistakes (unknown command, malformed argument, missing
	// required argument, bad value) - those are expected terminal input, not bugs, so they
	// come back as ExecutionResult.Failure for Terminal.razor to display as a friendly line.
	public readonly record struct ExecutionResult(bool Success, string Message) {
		public static ExecutionResult Ok(string message) => new(true, message);
		public static ExecutionResult Failure(string message) => new(false, message);
	}

	public class CommandExecutor {
		static readonly MethodInfo PushMethodDefinition =
			typeof(IWriteOnlyCommandAccessor).GetMethod(nameof(IWriteOnlyCommandAccessor.Push))!;

		readonly CommandRegistry _registry;
		readonly TerminalParser _parser;

		public CommandExecutor(CommandRegistry registry) {
			_registry = registry;
			_parser = new TerminalParser();
		}

		public ExecutionResult Execute(string input, IWriteOnlyCommandAccessor accessor) {
			var parsed = _parser.Parse(input);
			if (!parsed.Success) {
				return ExecutionResult.Failure(parsed.Error!);
			}

			var descriptor = _registry.Find(parsed.CommandName);
			if (descriptor == null) {
				string available = string.Join(", ", _registry.Commands.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal));
				return ExecutionResult.Failure($"Unknown command '{parsed.CommandName}'. Available commands: {available}");
			}

			// Argument names are matched case-insensitively - the debug terminal accepts
			// "orgId=..." for a public field named "OrgId" without requiring the caller to
			// know the exact declared casing.
			var argumentsByName = new Dictionary<string, string>(parsed.Arguments, StringComparer.OrdinalIgnoreCase);

			foreach (var key in argumentsByName.Keys) {
				if (!descriptor.Parameters.Any(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))) {
					return ExecutionResult.Failure($"Unknown argument '{key}' for command '{descriptor.Name}'.");
				}
			}

			foreach (var parameter in descriptor.Parameters) {
				if (parameter.IsRequired && !argumentsByName.ContainsKey(parameter.Name)) {
					return ExecutionResult.Failure($"Missing required argument '{parameter.Name}' for command '{descriptor.Name}'.");
				}
			}

			object boxedInstance = Activator.CreateInstance(descriptor.Type)!;
			foreach (var parameter in descriptor.Parameters) {
				if (argumentsByName.TryGetValue(parameter.Name, out var raw)) {
					if (!ValueCoercion.TryCoerce(raw, parameter.Type, out var value, out var error)) {
						return ExecutionResult.Failure($"Invalid value for argument '{parameter.Name}' of command '{descriptor.Name}': {error}");
					}
					parameter.SetValue(boxedInstance, value);
				} else {
					parameter.SetValue(boxedInstance, parameter.DefaultValue);
				}
			}

			try {
				var closedPush = PushMethodDefinition.MakeGenericMethod(descriptor.Type);
				closedPush.Invoke(accessor, new[] { boxedInstance });
			} catch (TargetInvocationException ex) when (ex.InnerException != null) {
				return ExecutionResult.Failure($"Command '{descriptor.Name}' failed: {ex.InnerException.Message}");
			}

			return ExecutionResult.Ok($"OK: {descriptor.Name}");
		}
	}
}
