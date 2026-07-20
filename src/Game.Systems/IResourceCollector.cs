using ECS;

namespace GS.Game.Systems {
	public interface IResourceCollector {
		double Compute(string ownerId, double currentValue, IReadOnlyWorld world);
	}
}
