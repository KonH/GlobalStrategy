using System;

namespace GS.Game.Bots {
	public interface IBotFeature {
		// This single interface plus a registry entry is the entire extension contract for part 3.
		string FeatureId { get; }
		void Tick(IBotObservation observation, IBotCommandSink sink, Random rng);
	}
}
