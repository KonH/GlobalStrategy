namespace GS.Game.Components {
	public interface IEffect { }

	public class ResourceChange : IEffect {
		public string OwnerId = "";
		public string ResourceId = "";
		public double Diff;
	}

	public class CharacterOpinionChange : IEffect {
		public string CountryId = "";
		public string CharacterId = "";
		public int Diff;
	}

	public class InfluenceAdded : IEffect {
		public string OrgId = "";
		public string CountryId = "";
		public int Amount;
	}
}
