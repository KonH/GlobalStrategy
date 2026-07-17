using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using GS.Game.Configs;

namespace GS.Configs.IO {
	// System.Text.Json equivalent of EffectConfig's Newtonsoft-based converter. FileConfig<T>
	// deserializes with System.Text.Json, which silently ignores Newtonsoft's [JsonConverter]
	// attribute on EffectConfig.Effects - without this, every effect loads as the base
	// ActionEffectDefinition type and `is DiscoverCountryEffectParams`/`is ControlChangeEffectParams`
	// checks (e.g. in BotObservation.ClassifyCard) are always false for any config loaded this way.
	public sealed class ActionEffectDefinitionListConverter : JsonConverter<List<ActionEffectDefinition>> {
		public override List<ActionEffectDefinition> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
			var result = new List<ActionEffectDefinition>();
			using var doc = JsonDocument.ParseValue(ref reader);
			foreach (var element in doc.RootElement.EnumerateArray()) {
				string effectType = element.TryGetProperty("effectType", out var et) ? et.GetString() ?? "" : "";
				ActionEffectDefinition item = effectType switch {
					"DiscoverCountry" => element.Deserialize<DiscoverCountryEffectParams>(options)!,
					"ControlChange" => element.Deserialize<ControlChangeEffectParams>(options)!,
					"OpinionModifier" => element.Deserialize<OpinionModifierEffectParams>(options)!,
					_ => element.Deserialize<ActionEffectDefinition>(options)!
				};
				result.Add(item);
			}
			return result;
		}

		public override void Write(Utf8JsonWriter writer, List<ActionEffectDefinition> value, JsonSerializerOptions options) {
			throw new NotImplementedException();
		}
	}
}
