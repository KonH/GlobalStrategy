using System;
using Newtonsoft.Json;
using ECS.Viewer;

namespace ECS.Viewer.Host {
	public sealed class EntityRefValueJsonConverter : JsonConverter<EntityRefValue> {
		public override EntityRefValue ReadJson(JsonReader reader, Type objectType, EntityRefValue? existingValue, bool hasExistingValue, JsonSerializer serializer) {
			throw new NotSupportedException();
		}

		public override void WriteJson(JsonWriter writer, EntityRefValue? value, JsonSerializer serializer) {
			if (value == null) {
				writer.WriteNull();
				return;
			}
			writer.WriteStartObject();
			writer.WritePropertyName("__entityRef");
			writer.WriteValue(value.EntityId);
			writer.WriteEndObject();
		}
	}
}
