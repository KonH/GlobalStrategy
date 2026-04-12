namespace GS.Main {
	public interface ISnapshotSerializer {
		string Serialize(WorldSnapshot snapshot);
		WorldSnapshot Deserialize(string json);
	}
}
