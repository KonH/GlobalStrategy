using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	public static class ActionCardBuilder {
		const int CooldownTextureSize = 128;

		static readonly Dictionary<int, Texture2D> _cooldownTextureCache = new();

		public readonly struct RequirementRow {
			public string Text { get; }
			public bool Passed { get; }

			public RequirementRow(string text, bool passed) {
				Text = text ?? "";
				Passed = passed;
			}
		}

		public readonly struct PlayableCountryBadgeItem {
			public string CountryId { get; }
			public Sprite Flag { get; }

			public PlayableCountryBadgeItem(string countryId, Sprite flag) {
				CountryId = countryId ?? "";
				Flag = flag;
			}
		}

		public sealed class CountryCardFace {
			public string Name { get; }
			public string Description { get; }
			public string GoldCostText { get; }
			public Sprite Art { get; }
			public int? WarWinChancePercent { get; }
			public double? CooldownFractionRemaining { get; }
			public double? CooldownRemainingDays { get; }
			public IReadOnlyList<RequirementRow> Requirements { get; }
			public IReadOnlyList<PlayableCountryBadgeItem> PlayableCountries { get; }

			public CountryCardFace(
				string name,
				string description,
				string goldCostText,
				Sprite art,
				int? warWinChancePercent,
				double? cooldownFractionRemaining,
				double? cooldownRemainingDays,
				IReadOnlyList<RequirementRow> requirements,
				IReadOnlyList<PlayableCountryBadgeItem> playableCountries) {
				Name = name ?? "";
				Description = description ?? "";
				GoldCostText = goldCostText;
				Art = art;
				WarWinChancePercent = warWinChancePercent;
				CooldownFractionRemaining = cooldownFractionRemaining;
				CooldownRemainingDays = cooldownRemainingDays;
				Requirements = CopyAsReadOnly(requirements);
				PlayableCountries = CopyAsReadOnly(playableCountries);
			}

			static IReadOnlyList<T> CopyAsReadOnly<T>(IReadOnlyList<T> source) {
				if (source == null || source.Count == 0) {
					return Array.Empty<T>();
				}
				var copy = new T[source.Count];
				for (int i = 0; i < source.Count; i++) {
					copy[i] = source[i];
				}
				return Array.AsReadOnly(copy);
			}
		}

		public struct CardResult {
			public VisualElement Card;
			public VisualElement Body;
			public Label CostLabel;
			public VisualElement PlayableCountriesBadge;
			public VisualElement DiscardHint;
			public Label DiscardHintLabel;
			public Label DiscardHintPrice;
		}

		public static CardResult Build(CountryCardFace face, bool includeDiscardHint = true) {
			if (face == null) {
				throw new ArgumentNullException(nameof(face));
			}
			var card = new VisualElement();
			card.AddToClassList("action-card");
			var result = Populate(card, face, includeDiscardHint);
			result.Card = card;
			return result;
		}

		public static CardResult Build(
			string name, string desc, string goldCostText, Sprite art, int? warWinChancePercent = null,
			double? cooldownFractionRemaining = null, double? cooldownRemainingDays = null) {
			var card = new VisualElement();
			card.AddToClassList("action-card");
			var result = Populate(card, name, desc, goldCostText, art, warWinChancePercent, cooldownFractionRemaining, cooldownRemainingDays);
			result.Card = card;
			return result;
		}

		public static CardResult PopulateSlot(VisualElement slot, CountryCardFace face, bool includeDiscardHint = false) {
			if (face == null) {
				throw new ArgumentNullException(nameof(face));
			}
			slot.Clear();
			slot.RemoveFromClassList("action-card--success");
			slot.RemoveFromClassList("action-card--fail");
			var result = Populate(slot, face, includeDiscardHint);
			result.Card = slot;
			return result;
		}

		public static CardResult PopulateSlot(
			VisualElement slot, string name, string desc, string goldCostText, Sprite art, int? warWinChancePercent = null,
			double? cooldownFractionRemaining = null, double? cooldownRemainingDays = null) {
			slot.Clear();
			slot.RemoveFromClassList("action-card--success");
			slot.RemoveFromClassList("action-card--fail");
			var result = Populate(slot, name, desc, goldCostText, art, warWinChancePercent, cooldownFractionRemaining, cooldownRemainingDays);
			result.Card = slot;
			return result;
		}

		static CardResult Populate(
			VisualElement container, string name, string desc, string goldCostText, Sprite art, int? warWinChancePercent = null,
			double? cooldownFractionRemaining = null, double? cooldownRemainingDays = null) {
			return PopulateContent(
				container, name, desc, goldCostText, art, warWinChancePercent,
				cooldownFractionRemaining, cooldownRemainingDays, null, null, false);
		}

		static CardResult Populate(VisualElement container, CountryCardFace face, bool includeDiscardHint) {
			return PopulateContent(
				container, face.Name, face.Description, face.GoldCostText, face.Art,
				face.WarWinChancePercent, face.CooldownFractionRemaining, face.CooldownRemainingDays,
				face.Requirements, face.PlayableCountries, includeDiscardHint);
		}

		static CardResult PopulateContent(
			VisualElement container,
			string name,
			string desc,
			string goldCostText,
			Sprite art,
			int? warWinChancePercent,
			double? cooldownFractionRemaining,
			double? cooldownRemainingDays,
			IReadOnlyList<RequirementRow> requirements,
			IReadOnlyList<PlayableCountryBadgeItem> playableCountries,
			bool includeDiscardHint) {
			// Card face content lives in its own wrapper so the unavailable-card dimming
			// (applied via .action-card--unavailable .action-card-content) never darkens the
			// cooldown overlay's remaining-time label, which must stay fully legible.
			var content = new VisualElement();
			content.AddToClassList("action-card-content");
			container.Add(content);

			var header = new Label(name);
			header.AddToClassList("action-card-header");
			content.Add(header);
			SetupHeaderAutoSize(header);

			var artEl = new VisualElement();
			artEl.AddToClassList("action-card-art");
			var artImage = new VisualElement();
			artImage.AddToClassList("action-card-art-image");
			if (art != null) {
				artImage.style.backgroundImage = new StyleBackground(art);
			}
			artEl.Add(artImage);
			VisualElement playableCountriesBadge = null;
			if (playableCountries != null && playableCountries.Count > 0) {
				playableCountriesBadge = BuildPlayableCountriesBadge(playableCountries);
				artEl.Add(playableCountriesBadge);
			}
			if (warWinChancePercent.HasValue) {
				artEl.Add(BuildWarWinChanceBadge(warWinChancePercent.Value));
			}
			content.Add(artEl);

			var body = new VisualElement();
			body.AddToClassList("action-card-body");

			var descLabel = new Label(desc);
			descLabel.AddToClassList("action-card-desc");
			body.Add(descLabel);
			SetupDescAutoSize(descLabel);

			if (requirements != null && requirements.Count > 0) {
				body.Add(BuildRequirements(requirements));
			}

			var footer = new VisualElement();
			footer.AddToClassList("action-card-footer");

			Label costLabel = null;
			if (!string.IsNullOrEmpty(goldCostText)) {
				var costRow = new VisualElement();
				costRow.AddToClassList("action-card-cost");
				costLabel = new Label(goldCostText);
				costLabel.AddToClassList("action-card-cost-label");
				costRow.Add(costLabel);
				var costIcon = new VisualElement();
				costIcon.AddToClassList("action-card-cost-icon");
				costRow.Add(costIcon);
				footer.Add(costRow);
			}

			body.Add(footer);
			content.Add(body);

			VisualElement discardHint = null;
			Label discardHintLabel = null;
			Label discardHintPrice = null;
			if (includeDiscardHint) {
				discardHint = BuildDiscardHint(out discardHintLabel, out discardHintPrice);
			}

			if (cooldownFractionRemaining.HasValue) {
				container.Add(BuildCooldownOverlay(cooldownFractionRemaining.Value, cooldownRemainingDays));
			}
			if (discardHint != null) {
				// The interaction hint must paint above the full-card cooldown overlay.
				container.Add(discardHint);
			}

			return new CardResult {
				Body = body,
				CostLabel = costLabel,
				PlayableCountriesBadge = playableCountriesBadge,
				DiscardHint = discardHint,
				DiscardHintLabel = discardHintLabel,
				DiscardHintPrice = discardHintPrice
			};
		}

		static VisualElement BuildRequirements(IReadOnlyList<RequirementRow> requirements) {
			var block = new VisualElement();
			block.AddToClassList("action-card-requirements");
			var labels = new List<Label>(requirements.Count);
			for (int i = 0; i < requirements.Count; i++) {
				var row = requirements[i];
				var label = new Label(row.Text);
				label.AddToClassList("action-card-requirement-row");
				label.AddToClassList(row.Passed
					? "action-card-requirement-row--pass"
					: "action-card-requirement-row--fail");
				label.AddToClassList(row.Passed ? "gs-color-positive" : "gs-color-negative");
				block.Add(label);
				labels.Add(label);
			}
			SetupRequirementsAutoSize(block, labels);
			return block;
		}

		static VisualElement BuildPlayableCountriesBadge(IReadOnlyList<PlayableCountryBadgeItem> countries) {
			var badge = new VisualElement();
			badge.AddToClassList("action-card-playable-countries");
			bool stackAsPile = countries.Count > 2;
			int visibleCount = Mathf.Min(countries.Count, 2);
			for (int i = 0; i < visibleCount; i++) {
				var flag = new VisualElement { pickingMode = PickingMode.Ignore };
				flag.AddToClassList("action-card-playable-country-flag");
				if (stackAsPile) {
					flag.AddToClassList("action-card-playable-country-flag--stacked");
					flag.style.left = i * 6;
					flag.style.top = i * 4;
				} else if (i > 0) {
					flag.style.marginLeft = 3;
				}
				if (countries[i].Flag != null) {
					flag.style.backgroundImage = new StyleBackground(countries[i].Flag);
				}
				badge.Add(flag);
			}
			return badge;
		}

		static VisualElement BuildDiscardHint(out Label hintLabel, out Label priceLabel) {
			var hint = new VisualElement { pickingMode = PickingMode.Ignore };
			hint.AddToClassList("action-card-discard-hint");
			hint.AddToClassList("gs-bg-tooltip");
			hint.style.display = DisplayStyle.None;

			hintLabel = new Label { pickingMode = PickingMode.Ignore };
			hintLabel.AddToClassList("action-card-discard-hint-label");
			hintLabel.AddToClassList("gs-content");
			hintLabel.AddToClassList("gs-color-light");
			hint.Add(hintLabel);

			priceLabel = new Label { pickingMode = PickingMode.Ignore };
			priceLabel.AddToClassList("action-card-discard-hint-price");
			priceLabel.AddToClassList("gs-content");
			priceLabel.AddToClassList("gs-color-light");
			hint.Add(priceLabel);

			var icon = new VisualElement { pickingMode = PickingMode.Ignore };
			icon.AddToClassList("action-card-cost-icon");
			hint.Add(icon);

			return hint;
		}

		static void SetupRequirementsAutoSize(VisualElement block, IReadOnlyList<Label> labels, float minSize = 8f) {
			block.RegisterCallback<GeometryChangedEvent>(_ => {
				float availableHeight = block.resolvedStyle.height;
				float availableWidth = block.resolvedStyle.width;
				if (availableHeight <= 0f || availableWidth <= 0f || labels.Count == 0) {
					return;
				}

				float currentSize = labels[0].resolvedStyle.fontSize;
				if (float.IsNaN(currentSize) || currentSize <= minSize) {
					return;
				}

				float measuredHeight = 0f;
				for (int i = 0; i < labels.Count; i++) {
					var measured = labels[i].MeasureTextSize(
						labels[i].text, availableWidth, VisualElement.MeasureMode.AtMost,
						float.PositiveInfinity, VisualElement.MeasureMode.Undefined);
					measuredHeight += measured.y;
				}
				if (measuredHeight <= availableHeight + 0.5f) {
					return;
				}

				float newSize = Mathf.Max(Mathf.Floor(currentSize * availableHeight / measuredHeight), minSize);
				if (newSize >= currentSize) {
					return;
				}
				for (int i = 0; i < labels.Count; i++) {
					labels[i].style.fontSize = newSize;
				}
			});
		}

		static VisualElement BuildCooldownOverlay(double fractionRemaining, double? remainingDays) {
			var overlay = new VisualElement();
			overlay.AddToClassList("action-card-cooldown-overlay");

			var radial = new VisualElement();
			radial.AddToClassList("action-card-cooldown-radial");
			radial.style.backgroundImage = new StyleBackground(GetOrCreateCooldownTexture(fractionRemaining));
			overlay.Add(radial);
			overlay.RegisterCallback<GeometryChangedEvent>(_ => {
				float size = overlay.resolvedStyle.width * 0.5f;
				if (size <= 0f) { return; }
				radial.style.width = size;
				radial.style.height = size;
			});

			var label = new Label(FormatCooldownRemaining(remainingDays));
			label.AddToClassList("action-card-cooldown-label");
			overlay.Add(label);

			return overlay;
		}

		static Texture2D GetOrCreateCooldownTexture(double fractionRemaining) {
			int bucket = Mathf.RoundToInt(Mathf.Clamp01((float)fractionRemaining) * 100);
			if (_cooldownTextureCache.TryGetValue(bucket, out var cached)) {
				return cached;
			}

			float fraction = bucket / 100f;
			var texture = new Texture2D(CooldownTextureSize, CooldownTextureSize, TextureFormat.RGBA32, false);
			texture.filterMode = FilterMode.Bilinear;
			float center = CooldownTextureSize / 2f;
			float radius = CooldownTextureSize / 2f;
			var fillColor = new Color(0f, 0f, 0f, 0.6f);
			var clearColor = new Color(0f, 0f, 0f, 0f);
			for (int y = 0; y < CooldownTextureSize; y++) {
				for (int x = 0; x < CooldownTextureSize; x++) {
					float dx = x + 0.5f - center;
					float dy = y + 0.5f - center;
					float dist = Mathf.Sqrt(dx * dx + dy * dy);
					if (dist > radius) {
						texture.SetPixel(x, y, clearColor);
						continue;
					}
					float angleDeg = Mathf.Atan2(-dx, dy) * Mathf.Rad2Deg;
					if (angleDeg < 0f) { angleDeg += 360f; }
					bool filled = (angleDeg / 360f) < fraction;
					texture.SetPixel(x, y, filled ? fillColor : clearColor);
				}
			}
			texture.Apply();
			_cooldownTextureCache[bucket] = texture;
			return texture;
		}

		static string FormatCooldownRemaining(double? remainingDays) {
			if (!remainingDays.HasValue || remainingDays.Value <= 0) { return ""; }
			int days = (int)remainingDays.Value;
			if (days >= 365) { return $"{days / 365} year(s)"; }
			if (days >= 30) { return $"{days / 30} month(s)"; }
			if (days >= 2) { return $"{days} days"; }
			if (days == 1) { return "1 day"; }
			return "less than a day";
		}

		static Label BuildWarWinChanceBadge(int percent) {
			var badge = new Label($"{percent}%");
			badge.AddToClassList("action-card-war-win-chance");
			if (percent <= 33) {
				badge.AddToClassList("action-card-war-win-chance--low");
			} else if (percent <= 66) {
				badge.AddToClassList("action-card-war-win-chance--mid");
			} else {
				badge.AddToClassList("action-card-war-win-chance--high");
			}
			return badge;
		}

		static void SetupDescAutoSize(Label desc, float minSize = 11f) {
			desc.RegisterCallback<GeometryChangedEvent>(_ => {
				float availH = desc.resolvedStyle.height;
				float availW = desc.resolvedStyle.width;
				if (availH <= 0 || availW <= 0) { return; }
				var measured = desc.MeasureTextSize(
					desc.text, availW, VisualElement.MeasureMode.AtMost, float.PositiveInfinity, VisualElement.MeasureMode.Undefined);
				if (measured.y > availH + 0.5f) {
					float cur = desc.resolvedStyle.fontSize;
					if (cur > minSize) {
						float scale = availH / measured.y;
						float newSize = Mathf.Max(Mathf.Floor(cur * scale), minSize);
						if (newSize < cur) { desc.style.fontSize = newSize; }
					}
				}
			});
		}

		static void SetupHeaderAutoSize(Label header, float minSize = 9f) {
			header.RegisterCallback<GeometryChangedEvent>(_ => {
				float availW = header.resolvedStyle.width;
				float availH = header.resolvedStyle.height;
				if (availW <= 0 || availH <= 0) { return; }
				float cur = header.resolvedStyle.fontSize;
				if (float.IsNaN(cur) || cur <= minSize) { return; }

				var measured = header.MeasureTextSize(
					header.text, availW, VisualElement.MeasureMode.AtMost,
					float.PositiveInfinity, VisualElement.MeasureMode.Undefined);
				if (measured.y <= availH + 0.5f && measured.x <= availW + 0.5f) {
					return;
				}

				float scale = Mathf.Min(availW / Mathf.Max(measured.x, 1f), availH / Mathf.Max(measured.y, 1f));
				float newSize = Mathf.Max(Mathf.Floor(cur * scale), minSize);
				if (newSize < cur) { header.style.fontSize = newSize; }
			});
		}
	}
}
