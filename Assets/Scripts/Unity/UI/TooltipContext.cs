using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	class TooltipContext {
		readonly TooltipSystem _system;
		readonly HashSet<string> _ancestors;

		public TooltipContext(TooltipSystem system, HashSet<string> ancestors) {
			_system = system;
			_ancestors = ancestors;
		}

		public void RegisterInnerTrigger(VisualElement trigger, string id, Func<TooltipContext, VisualElement> buildContent) {
			_system.RegisterTrigger(trigger, id, buildContent, _ancestors);
		}
	}
}
