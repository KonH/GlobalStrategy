using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// CharacterCard component - unifies the two hand-built card shapes CharactersView (.char-card,
	/// a player-country roster card with the name overlaid on the portrait) and OrgCharactersView
	/// (.org-char-card, an org-slot card with a separate name/role block below the portrait, plus an
	/// --empty variant for an unfilled slot) used to build independently. Both shapes already read
	/// their look from tokenized SharedStyles.uss classes (no redesign, per spec.md), so this
	/// component keeps those two classes rather than introducing a third - the unification is one
	/// builder with one BuildPlayer/BuildOrg pair instead of two copies of the same element-building
	/// code, and the --empty variant (previously org-only) is now a first-class option of the same
	/// builder. CharactersView/OrgCharactersView switch to calling this in phase 7.
	/// </summary>
	public static class CharacterCardBuilder {
		public struct PlayerElements {
			public VisualElement Card;
			public VisualElement Portrait;
			public Label Name;
			public Label Role;
			public Label Opinion;
			public VisualElement StatsBlock;
		}

		public struct OrgElements {
			public VisualElement Card;
			public VisualElement Portrait;
			public VisualElement InfoBlock;
			public Label Name;             // null when empty
			public Label Role;
			public VisualElement StatsBlock; // null when empty
			public Label Status;           // null unless empty
		}

		public static PlayerElements BuildPlayer() {
			var card = new VisualElement();
			card.AddToClassList("char-card");

			var portrait = new VisualElement();
			portrait.AddToClassList("char-portrait-area");
			card.Add(portrait);

			var nameOverlay = new VisualElement();
			nameOverlay.AddToClassList("char-name-overlay");
			var name = new Label();
			name.AddToClassList("char-name");
			nameOverlay.Add(name);
			portrait.Add(nameOverlay);

			var info = new VisualElement();
			info.AddToClassList("char-info");

			var role = new Label();
			role.AddToClassList("char-role");
			info.Add(role);

			var opinion = new Label();
			opinion.AddToClassList("char-opinion");
			info.Add(opinion);

			var statsBlock = new VisualElement();
			statsBlock.AddToClassList("char-stats");
			info.Add(statsBlock);

			card.Add(info);

			return new PlayerElements {
				Card = card, Portrait = portrait, Name = name, Role = role, Opinion = opinion, StatsBlock = statsBlock
			};
		}

		public static void BindOpinion(Label opinion, int displayOpinion) {
			opinion.text = displayOpinion >= 0 ? $"+{displayOpinion}" : $"{displayOpinion}";
			opinion.EnableInClassList("gs-color-positive", displayOpinion >= 0);
			opinion.EnableInClassList("gs-color-negative", displayOpinion < 0);
		}

		public static OrgElements BuildOrg(bool empty) {
			var card = new VisualElement();
			card.AddToClassList("org-char-card");
			card.EnableInClassList("org-char-card--empty", empty);

			var portrait = new VisualElement();
			portrait.AddToClassList("org-portrait-area");
			portrait.EnableInClassList("org-portrait-area--empty", empty);
			card.Add(portrait);

			var infoBlock = new VisualElement();
			infoBlock.AddToClassList("org-info-block");

			Label name = null;
			VisualElement statsBlock = null;
			Label status = null;

			if (!empty) {
				name = new Label();
				name.AddToClassList("org-char-name");
				infoBlock.Add(name);
			}

			var role = new Label();
			role.AddToClassList("org-char-role");
			infoBlock.Add(role);

			if (!empty) {
				statsBlock = new VisualElement();
				statsBlock.AddToClassList("org-char-stats");
				infoBlock.Add(statsBlock);
			} else {
				status = new Label();
				status.AddToClassList("gs-hint");
				infoBlock.Add(status);
			}

			card.Add(infoBlock);

			return new OrgElements {
				Card = card, Portrait = portrait, InfoBlock = infoBlock,
				Name = name, Role = role, StatsBlock = statsBlock, Status = status
			};
		}
	}
}
