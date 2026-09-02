using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// DrawSlot atom - the fixed-size placeholder CardDrawView.BuildSlots lays a dealt card copy
	/// over (was ".card-draw-slot" in Assets/UI/HUD/HUD.uss, sized to match ".action-card"/
	/// ".card-draw-card-copy"). Given its own Components.uss class instead of reusing HUD.uss's
	/// private one, so the Gallery block does not need to import the whole HUD stylesheet.
	/// CardDrawView switches to calling this in phase 7.
	/// </summary>
	public static class DrawSlotBuilder {
		public static VisualElement Build() {
			var slot = new VisualElement();
			slot.AddToClassList("draw-slot");
			slot.pickingMode = PickingMode.Ignore;
			return slot;
		}
	}
}
