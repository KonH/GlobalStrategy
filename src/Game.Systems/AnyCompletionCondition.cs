using System;
using System.Collections.Generic;

namespace GS.Game.Systems {
	public sealed class AnyCompletionCondition : ICompletionCondition {
		readonly IReadOnlyList<ICompletionCondition> _members;

		public AnyCompletionCondition(IReadOnlyList<ICompletionCondition> members) {
			_members = members ?? throw new ArgumentNullException(nameof(members));
			if (_members.Count == 0) {
				throw new ArgumentException("Completion condition 'any' must contain at least one member.", nameof(members));
			}
		}

		public bool IsMet(CompletionConditionContext context) {
			for (int i = 0; i < _members.Count; i++) {
				if (_members[i].IsMet(context)) {
					return true;
				}
			}
			return false;
		}
	}
}
