namespace GS.Configs {
	public interface IReadOnlyConfigSource<TConfig> {
		TConfig Load();
	}
}
