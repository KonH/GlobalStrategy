namespace GS.Configs {
	public interface IConfigSource<TConfig> {
		TConfig Load();
	}
}
