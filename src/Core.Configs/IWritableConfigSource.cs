namespace GS.Configs {
	public interface IWritableConfigSource<TConfig> : IReadOnlyConfigSource<TConfig> {
		void Save(TConfig config);
	}
}
