using System;
using System.Collections.Generic;
using ECS;

namespace GS.Game.Systems {
	public interface ICompletionCondition {
		bool IsMet(CompletionConditionContext context);
	}

	public sealed class CompletionConditionContext {
		public IReadOnlyWorld World { get; }
		public string OrganizationId { get; }
		public IReadOnlyCollection<string> AvailableCountryIds { get; }
		public int MaxControlPool { get; }

		public CompletionConditionContext(
			IReadOnlyWorld world,
			string organizationId,
			IEnumerable<string> availableCountryIds,
			int maxControlPool) {
			World = world ?? throw new ArgumentNullException(nameof(world));
			OrganizationId = organizationId ?? throw new ArgumentNullException(nameof(organizationId));
			if (availableCountryIds == null) {
				throw new ArgumentNullException(nameof(availableCountryIds));
			}
			if (maxControlPool <= 0) {
				throw new ArgumentOutOfRangeException(nameof(maxControlPool), maxControlPool,
					"Completion-condition control capacity must be positive.");
			}

			AvailableCountryIds = new HashSet<string>(availableCountryIds, StringComparer.Ordinal);
			MaxControlPool = maxControlPool;
		}
	}
}
