namespace GS.Game.Components {
	// Marker: makes ResourceSystem recompute+apply this resource-effect entity's collector on
	// the next Update call regardless of its PayType's normal Daily/Monthly gate. One-shot —
	// removed by ResourceSystem after use. Not [Savable]: purely a same-tick signal, never
	// meant to persist across a save/load boundary.
	public struct ForceResourceRecompute { }
}
