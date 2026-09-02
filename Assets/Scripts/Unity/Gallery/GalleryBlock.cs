using System;
using UnityEngine.UIElements;

namespace GS.Unity.Gallery {
	/// <summary>
	/// One collapsible preview block in the Gallery. GalleryDocument (the host) builds a
	/// ui:Foldout titled with Title and hands the block the foldout's content area plus a
	/// GalleryBlockState looked up by Id, so the block owns its own two dropdowns and state
	/// switch while expansion and both selections still survive a domain reload. Most blocks
	/// should derive from GalleryBlockBase instead of implementing this directly - it already
	/// wires up the two dropdowns and persistence.
	/// </summary>
	public interface IGalleryBlock {
		string Id { get; }
		string Title { get; }
		void Build(VisualElement content, GalleryBlockState state);
	}

	/// <summary>
	/// Per-block-id persisted state: expansion plus both dropdown selections. Held as a flat
	/// serialized list on GalleryDocument (keyed by BlockId) so every block - not just one -
	/// survives the domain reload a script recompile causes.
	/// </summary>
	[Serializable]
	public class GalleryBlockState {
		public string BlockId;
		// Defaults collapsed: with 30+ blocks now registered, expanding everything by default
		// stacks every block's content in the viewport at once with no way to tell one block's
		// rows from the next (see Docs/Specs/26_08_28_16_ui-refactoring - reported after Batch 4).
		public bool Expanded;
		public string Selection1 = "";
		public int Selection2;
	}
}
