using System;
using System.Collections.Generic;

namespace GS.Game.Bots {
	public interface IBotFeature {
		// Features only propose scored plays; Bot arbitrates and emits at most one play per cycle.
		string FeatureId { get; }
		void CollectProposals(IBotObservation observation, IList<BotPlayProposal> proposals, Random rng);
	}
}
