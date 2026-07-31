using System;
using System.Collections.Generic;
using GS.Game.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GS.Game.Configs {
	public class DiscoverCountryEffectParams : ActionEffectDefinition {
		public double MinCountryChance { get; set; }
	}

	public class ControlChangeEffectParams : ActionEffectDefinition {
		public int Amount { get; set; }
	}

	public class OpinionModifierEffectParams : ActionEffectDefinition {
		public string SourceId { get; set; } = "";
		public int InitialValue { get; set; }
		public int DecayPerMonth { get; set; }
	}

	public class SetCountryRelationEffectParams : ActionEffectDefinition {
		public RelationKind Kind { get; set; }
	}

	public class ClearCountryRelationEffectParams : ActionEffectDefinition {
		public RelationKind Kind { get; set; }
	}

	public class EnemyControlDrainEffectParams : ActionEffectDefinition {
		public int Amount { get; set; }
	}

	public class ResolveWarEffectParams : ActionEffectDefinition {
		public WarOutcome Outcome { get; set; }
	}

	public class CountryResourceModifierEffectParams : ActionEffectDefinition {
		public string ResourceId { get; set; } = "";
		public double InitialValue { get; set; }
		public double DecayPerMonth { get; set; }
	}

	public class OrgResourceGrantEffectParams : ActionEffectDefinition {
		public string ResourceId { get; set; } = "";
		public double Amount { get; set; }
	}

	public class DeclareWarEffectParams : ActionEffectDefinition {
	}

	// Converter sits on the Effects LIST property, not on ActionEffectDefinition itself.
	// Putting [JsonConverter] on the base class causes infinite recursion because the
	// attribute is inherited by all subclasses and Newtonsoft re-enters the converter
	// whenever it deserializes any subclass instance.
	class ActionEffectDefinitionListConverter : JsonConverter<List<ActionEffectDefinition>> {
		public override List<ActionEffectDefinition>? ReadJson(JsonReader reader, Type objectType, List<ActionEffectDefinition>? existingValue, bool hasExistingValue, JsonSerializer serializer) {
			var array = JArray.Load(reader);
			var result = new List<ActionEffectDefinition>();
			foreach (var token in array) {
				var obj = (JObject)token;
				string effectType = obj["effectType"]?.Value<string>() ?? "";
				ActionEffectDefinition item;
				switch (effectType) {
					case "DiscoverCountry": item = obj.ToObject<DiscoverCountryEffectParams>(serializer)!; break;
					case "ControlChange": item = obj.ToObject<ControlChangeEffectParams>(serializer)!; break;
					case "OpinionModifier": item = obj.ToObject<OpinionModifierEffectParams>(serializer)!; break;
					case "SetCountryRelation": item = obj.ToObject<SetCountryRelationEffectParams>(serializer)!; break;
					case "ClearCountryRelation": item = obj.ToObject<ClearCountryRelationEffectParams>(serializer)!; break;
					case "EnemyControlDrain": item = obj.ToObject<EnemyControlDrainEffectParams>(serializer)!; break;
					case "ResolveWar": item = obj.ToObject<ResolveWarEffectParams>(serializer)!; break;
					case "CountryResourceModifier": item = obj.ToObject<CountryResourceModifierEffectParams>(serializer)!; break;
					case "OrgResourceGrant": item = obj.ToObject<OrgResourceGrantEffectParams>(serializer)!; break;
					case "DeclareWar": item = obj.ToObject<DeclareWarEffectParams>(serializer)!; break;
					default:                item = obj.ToObject<ActionEffectDefinition>(serializer)!;      break;
				}
				result.Add(item);
			}
			return result;
		}

		public override void WriteJson(JsonWriter writer, List<ActionEffectDefinition>? value, JsonSerializer serializer) {
			throw new NotImplementedException();
		}
	}

	public class ActionEffectDefinition {
		public string EffectId    { get; set; } = "";
		public string EffectType  { get; set; } = "";
		public string NameKey     { get; set; } = "";
		public string DescKey     { get; set; } = "";
	}

	public class EffectConfig {
		[JsonConverter(typeof(ActionEffectDefinitionListConverter))]
		public List<ActionEffectDefinition> Effects { get; set; } = new();

		public ActionEffectDefinition? Find(string effectId) {
			foreach (var e in Effects) {
				if (e.EffectId == effectId) { return e; }
			}
			return null;
		}
	}
}
