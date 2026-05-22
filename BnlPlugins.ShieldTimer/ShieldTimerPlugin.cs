using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace BnlPlugins.ShieldTimer
{
    [BepInPlugin("bnl.community.shieldtimer", "BNL Shield Timer", "0.1.0")]
    public sealed class ShieldTimerPlugin : BaseUnityPlugin
    {
        private const string HarmonyId = "bnl.community.shieldtimer";

        internal static ConfigEntry<bool> Enabled = null!;
        internal static ConfigEntry<string> ShieldBarColorHex = null!;
        internal static ConfigEntry<float> ShieldClockSizeMultiplier = null!;
        internal static ConfigEntry<float> ShieldClockOffsetX = null!;
        internal static ConfigEntry<float> ShieldClockOffsetY = null!;
        internal static ConfigEntry<string> ShieldTimerDisplayMode = null!;

        private Harmony? _harmony;

        private void Awake()
        {
            Enabled = Config.Bind("Shield Timer", "Enabled", true,
                new ConfigDescription("Enable the enemy shield timer and shield buff bar.", null,
                    new ConfigurationManagerAttributes { Order = 100 }));
            ShieldBarColorHex = Config.Bind("Shield Timer", "Color", "#FFF04A",
                new ConfigDescription("Shield bar and timer color.", null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = ColorDrawer.Draw,
                        Order = 99,
                        DispName = "Color"
                    }));
            ShieldClockSizeMultiplier = Config.Bind("Shield Timer", "SizeMultiplier", 1.0f,
                FloatConfig.Range("Size multiplier for the shield timer icon/text.", 0.3f, 5.0f, 98, "Size Multiplier"));
            ShieldClockOffsetX = Config.Bind("Shield Timer", "OffsetX", 0.0f,
                FloatConfig.Range("Horizontal offset for the shield timer display.", -1000f, 1000f, 97, "Offset X"));
            ShieldClockOffsetY = Config.Bind("Shield Timer", "OffsetY", 0.0f,
                FloatConfig.Range("Vertical offset for the shield timer display.", -1000f, 1000f, 96, "Offset Y"));
            ShieldTimerDisplayMode = Config.Bind("Shield Timer", "DisplayMode", "circle",
                new ConfigDescription("Choose between radial circle or numeric timer display.",
                    new AcceptableValueList<string>("circle", "numeric"),
                    new ConfigurationManagerAttributes { Order = 95, DispName = "Display Mode" }));

            FloatConfig.BindRound(ShieldClockSizeMultiplier, 0.3f, 5.0f);
            FloatConfig.BindRound(ShieldClockOffsetX, -1000f, 1000f);
            FloatConfig.BindRound(ShieldClockOffsetY, -1000f, 1000f);

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(ShieldTimerPlugin).Assembly);
            Logger.LogInfo("[BNL Shield Timer] Loaded");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchAll(HarmonyId);
        }

        internal static bool TryGetBarColor(out Color color)
        {
            return ColorHelper.TryParseHex(ShieldBarColorHex.Value, out color);
        }

        internal static bool UseNumericTimer()
        {
            string mode = ShieldTimerDisplayMode.Value ?? "circle";
            return string.Equals(mode, "text", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "number", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mode, "numeric", StringComparison.OrdinalIgnoreCase);
        }
    }

    [HarmonyPatch(typeof(GuiHealthbar), "Start")]
    internal static class GuiHealthbarStartPatch
    {
        private static void Postfix(GuiHealthbar __instance)
        {
            ShieldBuffBarRuntime.AttachShieldBuffBar(__instance);
        }
    }

    internal static class ShieldBuffBarRuntime
    {
        internal static void AttachShieldBuffBar(GuiHealthbar healthbar)
        {
            if (healthbar == null || healthbar.HealthBar == null)
                return;

            ShieldBuffBarController controller = healthbar.gameObject.GetComponent<ShieldBuffBarController>();
            if (controller == null)
                controller = healthbar.gameObject.AddComponent<ShieldBuffBarController>();
            controller.Init(healthbar);
        }
    }

    public sealed class ShieldBuffBarController : MonoBehaviour
    {
        private const string TimedShieldEffectId = "effect_status_shield_block";

        private static readonly FieldInfo UnitField =
            typeof(GuiHealthbar).GetField("unit", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static Sprite? _cachedClockSprite;

        private GuiHealthbar? _healthbar;
        private Image? _bar;
        private RectTransform? _barRect;
        private Image? _clock;
        private RectTransform? _clockRect;
        private Text? _timerText;
        private RectTransform? _timerTextRect;
        private float _observedShieldMax;

        public void Init(GuiHealthbar source)
        {
            _healthbar = source;
        }

        private void LateUpdate()
        {
            if (_healthbar == null || _healthbar.HealthBar == null)
                return;

            EnsureVisuals();

            if (_bar == null || _clock == null || _timerText == null)
                return;

            if (!ShieldTimerPlugin.Enabled.Value)
            {
                _bar.enabled = false;
                _clock.enabled = false;
                _timerText.enabled = false;
                return;
            }

            Unit unit = ReferenceEquals(UnitField, null) ? null : UnitField.GetValue(_healthbar) as Unit;
            if (unit == null)
            {
                _bar.enabled = false;
                _clock.enabled = false;
                _timerText.enabled = false;
                return;
            }

            bool isEnemy = IsEnemy(unit);
            ConstEffectInfo strongestShieldEffect;
            float strongestShieldValue;
            bool hasStrongestShieldEffect = TryGetStrongestShieldEffect(unit, out strongestShieldEffect, out strongestShieldValue);

            float shieldValue = hasStrongestShieldEffect ? Mathf.Max(strongestShieldValue, 0f) : Mathf.Max(unit.Shield, 0f);
            float configuredShieldMax = 0f;
            if (unit.UnitCard != null && unit.UnitCard.Health != null && unit.UnitCard.Health.Health != null)
                configuredShieldMax = Mathf.Max(unit.UnitCard.Health.Health.Shield, 0f);

            if (shieldValue > _observedShieldMax)
                _observedShieldMax = shieldValue;

            float shieldMax = Mathf.Max(configuredShieldMax, _observedShieldMax);
            float shieldFill = ResolveShieldFill(shieldValue, shieldMax);
            bool visible = isEnemy && shieldFill > Mathf.Epsilon && _healthbar.Content != null && _healthbar.Content.alpha > 0.01f;

            _bar.enabled = visible;
            _clock.enabled = false;
            _timerText.enabled = false;
            if (!visible)
                return;

            PositionVisuals();

            Color shieldColor;
            if (!ShieldTimerPlugin.TryGetBarColor(out shieldColor))
                shieldColor = new Color(1f, 0.94f, 0.29f, 1f);

            _bar.color = shieldColor;
            _bar.fillAmount = shieldFill;

            float timerFill;
            bool hasTimer = TryGetShieldTimerFraction(unit, strongestShieldEffect, out timerFill);
            if (!hasTimer)
                return;

            if (ShieldTimerPlugin.UseNumericTimer())
            {
                _timerText.color = shieldColor;
                _timerText.text = GetRemainingTimeText(unit, strongestShieldEffect);
                _timerText.enabled = !string.IsNullOrEmpty(_timerText.text);
            }
            else
            {
                _clock.color = shieldColor;
                _clock.fillAmount = timerFill;
                _clock.enabled = true;
            }
        }

        private void EnsureVisuals()
        {
            if (_healthbar == null || _healthbar.HealthBar == null)
                return;

            Image source = _healthbar.HealthBar;

            if (_bar == null)
            {
                GameObject barGo = new GameObject("ExperimentalShieldBuffBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                barGo.transform.SetParent(_healthbar.HealthBar.transform, false);
                _bar = barGo.GetComponent<Image>();
                _barRect = barGo.GetComponent<RectTransform>();
                _bar.enabled = false;
                _bar.sprite = source.sprite;
                _bar.type = source.type;
                _bar.fillMethod = source.fillMethod;
                _bar.fillOrigin = source.fillOrigin;
                _bar.fillClockwise = source.fillClockwise;
                _bar.preserveAspect = source.preserveAspect;
                _bar.material = source.material;
            }

            if (_clock == null)
            {
                GameObject clockGo = new GameObject("ExperimentalShieldBuffClock", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                clockGo.transform.SetParent(_healthbar.HealthBar.transform, false);
                _clock = clockGo.GetComponent<Image>();
                _clockRect = clockGo.GetComponent<RectTransform>();
                _clock.enabled = false;
                _clock.sprite = ResolveClockSprite() ?? source.sprite;
                _clock.type = Image.Type.Filled;
                _clock.fillMethod = Image.FillMethod.Radial360;
                _clock.fillOrigin = 2;
                _clock.fillClockwise = false;
                _clock.preserveAspect = true;
                _clock.material = source.material;
            }

            if (_timerText == null)
            {
                GameObject textGo = new GameObject("ExperimentalShieldBuffTimerText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                textGo.transform.SetParent(_healthbar.HealthBar.transform, false);
                _timerText = textGo.GetComponent<Text>();
                _timerTextRect = textGo.GetComponent<RectTransform>();
                _timerText.enabled = false;
                _timerText.alignment = TextAnchor.MiddleRight;
                _timerText.horizontalOverflow = HorizontalWrapMode.Overflow;
                _timerText.verticalOverflow = VerticalWrapMode.Overflow;
                _timerText.font = _healthbar.Title != null ? _healthbar.Title.font : null;
                _timerText.fontStyle = _healthbar.Title != null ? _healthbar.Title.fontStyle : FontStyle.Normal;
                _timerText.material = _healthbar.Title != null ? _healthbar.Title.material : null;
            }
        }

        private static Sprite? ResolveClockSprite()
        {
            if (_cachedClockSprite != null)
                return _cachedClockSprite;

            UnityEngine.Object[] guiBuffs = Resources.FindObjectsOfTypeAll(typeof(GuiBuff));
            for (int i = 0; i < guiBuffs.Length; i++)
            {
                GuiBuff guiBuff = guiBuffs[i] as GuiBuff;
                if (guiBuff != null && guiBuff.RoundCooldown != null)
                {
                    _cachedClockSprite = guiBuff.RoundCooldown;
                    return _cachedClockSprite;
                }
            }

            Sprite[] sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            List<string> preferredNames = new List<string>(new[] { "roundcooldown", "cooldown_round", "pie", "radial", "circle" });
            for (int i = 0; i < sprites.Length; i++)
            {
                Sprite sprite = sprites[i];
                if (sprite == null || string.IsNullOrEmpty(sprite.name))
                    continue;

                string lower = sprite.name.ToLowerInvariant();
                for (int j = 0; j < preferredNames.Count; j++)
                {
                    if (lower.Contains(preferredNames[j]))
                    {
                        _cachedClockSprite = sprite;
                        return _cachedClockSprite;
                    }
                }
            }

            return _cachedClockSprite;
        }

        private void PositionVisuals()
        {
            if (_barRect == null || _clockRect == null || _timerTextRect == null || _healthbar == null || _healthbar.HealthBar == null)
                return;

            RectTransform src = _healthbar.HealthBar.rectTransform;
            float sourceHeight = Mathf.Abs(src.rect.height);
            if (sourceHeight <= Mathf.Epsilon)
                sourceHeight = Mathf.Abs(src.sizeDelta.y);
            if (sourceHeight <= Mathf.Epsilon)
                sourceHeight = 8f;

            float shieldHeight = Mathf.Max(2f, sourceHeight * 0.35f);
            float yOffset = 2f;

            _barRect.anchorMin = new Vector2(0f, 1f);
            _barRect.anchorMax = new Vector2(1f, 1f);
            _barRect.pivot = new Vector2(0.5f, 0f);
            _barRect.anchoredPosition = new Vector2(0f, yOffset);
            _barRect.sizeDelta = new Vector2(0f, shieldHeight);
            _barRect.localScale = src.localScale;
            _barRect.localRotation = src.localRotation;

            float sizeMultiplier = Mathf.Max(0.25f, ShieldTimerPlugin.ShieldClockSizeMultiplier.Value);
            float clockSize = Mathf.Max(shieldHeight + 4f, sourceHeight * 0.8f) * sizeMultiplier;
            float baseHealthBarCenterY = -sourceHeight * 0.5f;

            _clockRect.anchorMin = new Vector2(0f, 1f);
            _clockRect.anchorMax = new Vector2(0f, 1f);
            _clockRect.pivot = new Vector2(1f, 0.5f);
            _clockRect.anchoredPosition = new Vector2(-4f + ShieldTimerPlugin.ShieldClockOffsetX.Value, baseHealthBarCenterY + ShieldTimerPlugin.ShieldClockOffsetY.Value);
            _clockRect.sizeDelta = new Vector2(clockSize, clockSize);
            _clockRect.localScale = src.localScale;
            _clockRect.localRotation = src.localRotation;

            _timerText.fontSize = Mathf.Max(10, _healthbar.Title != null
                ? Mathf.RoundToInt(_healthbar.Title.fontSize * Mathf.Max(0.5f, ShieldTimerPlugin.ShieldClockSizeMultiplier.Value))
                : Mathf.RoundToInt(12f * Mathf.Max(0.5f, ShieldTimerPlugin.ShieldClockSizeMultiplier.Value)));
            _timerTextRect.anchorMin = new Vector2(0f, 1f);
            _timerTextRect.anchorMax = new Vector2(0f, 1f);
            _timerTextRect.pivot = new Vector2(1f, 0.5f);
            _timerTextRect.anchoredPosition = new Vector2(-4f + ShieldTimerPlugin.ShieldClockOffsetX.Value, baseHealthBarCenterY + ShieldTimerPlugin.ShieldClockOffsetY.Value);
            _timerTextRect.sizeDelta = new Vector2(48f * Mathf.Max(0.75f, ShieldTimerPlugin.ShieldClockSizeMultiplier.Value), Mathf.Max(shieldHeight + 6f, sourceHeight));
            _timerTextRect.localScale = src.localScale;
            _timerTextRect.localRotation = src.localRotation;
        }

        private static bool IsEnemy(Unit unit)
        {
            if (unit == null || unit.IsMyPlayer || !unit.PlayerId.HasValue)
                return false;

            ZoneData zoneData = Singleton<ZoneData>.Instance;
            if (zoneData == null)
                return true;

            return unit.Team != zoneData.MyTeam;
        }

        private float ResolveShieldFill(float shieldValue, float shieldMax)
        {
            if (shieldValue <= 1.0001f)
                return Mathf.Clamp01(shieldValue);

            if (shieldMax > Mathf.Epsilon)
                return Mathf.Clamp01(shieldValue / shieldMax);

            return 0f;
        }

        private static bool TryGetShieldTimerFraction(Unit unit, ConstEffectInfo strongestShieldEffect, out float fillAmount)
        {
            fillAmount = 1f;
            ConstEffectInfo effectInfo = strongestShieldEffect;
            if ((effectInfo == null || !effectInfo.HasDuration) && !TryGetTimedShieldEffect(unit, out effectInfo))
                return false;

            if (effectInfo == null || !effectInfo.HasDuration || effectInfo.Card == null ||
                effectInfo.Card.Duration == null || effectInfo.Card.Duration.Value <= Mathf.Epsilon ||
                effectInfo.TimestampEnd == null || Singleton<IServerTime>.Instance == null)
            {
                return false;
            }

            float remaining = Mathf.Max(0f, Singleton<IServerTime>.Instance.TimeTill((long)effectInfo.TimestampEnd.Value));
            fillAmount = Mathf.Clamp01(remaining / effectInfo.Card.Duration.Value);
            return true;
        }

        private static string GetRemainingTimeText(Unit unit, ConstEffectInfo strongestShieldEffect)
        {
            ConstEffectInfo effectInfo = strongestShieldEffect;
            if ((effectInfo == null || !effectInfo.HasDuration) && !TryGetTimedShieldEffect(unit, out effectInfo))
                return string.Empty;

            return FormatRemainingTime(effectInfo);
        }

        private static string FormatRemainingTime(ConstEffectInfo effectInfo)
        {
            if (effectInfo == null || effectInfo.TimestampEnd == null || Singleton<IServerTime>.Instance == null)
                return string.Empty;

            float remaining = Mathf.Max(0f, Singleton<IServerTime>.Instance.TimeTill((long)effectInfo.TimestampEnd.Value));
            if (remaining <= Mathf.Epsilon)
                return string.Empty;

            return remaining.ToString("0.0") + "s";
        }

        private static bool TryGetTimedShieldEffect(Unit unit, out ConstEffectInfo match)
        {
            match = null;
            float strongestValue;
            if (TryGetStrongestShieldEffect(unit, out match, out strongestValue) && match != null && match.HasDuration)
                return true;

            match = null;
            if (unit == null || unit.ActualConstEffects == null)
                return false;

            for (int i = 0; i < unit.ActualConstEffects.Count; i++)
            {
                ConstEffectInfo effectInfo = unit.ActualConstEffects[i];
                if (effectInfo == null || effectInfo.Card == null)
                    continue;

                CardEffect card = effectInfo.Card;
                ConstEffectBuff buffEffect = card.Effect as ConstEffectBuff;
                bool grantsShield = buffEffect != null && buffEffect.Buffs != null && buffEffect.Buffs.ContainsKey(BuffType.Shield);
                if (!grantsShield || !effectInfo.HasDuration)
                    continue;

                if (string.Equals(card.Id, TimedShieldEffectId, StringComparison.Ordinal))
                {
                    match = effectInfo;
                    return true;
                }

                if (match == null)
                    match = effectInfo;
            }

            return match != null;
        }

        private static bool TryGetStrongestShieldEffect(Unit unit, out ConstEffectInfo match, out float shieldValue)
        {
            match = null;
            shieldValue = 0f;
            if (unit == null || unit.ActualConstEffects == null)
                return false;

            for (int i = 0; i < unit.ActualConstEffects.Count; i++)
            {
                ConstEffectInfo effectInfo = unit.ActualConstEffects[i];
                if (effectInfo == null || effectInfo.Card == null)
                    continue;

                CardEffect card = effectInfo.Card;
                ConstEffectBuff buffEffect = card.Effect as ConstEffectBuff;
                if (buffEffect == null || buffEffect.Buffs == null || !buffEffect.Buffs.ContainsKey(BuffType.Shield))
                    continue;

                float candidateValue = Mathf.Max(buffEffect.Buffs[BuffType.Shield], 0f);
                bool takeCandidate = false;
                if (match == null || candidateValue > shieldValue + Mathf.Epsilon)
                {
                    takeCandidate = true;
                }
                else if (Mathf.Abs(candidateValue - shieldValue) <= Mathf.Epsilon)
                {
                    bool candidatePreferred = string.Equals(card.Id, TimedShieldEffectId, StringComparison.Ordinal);
                    bool currentPreferred = match.Card != null && string.Equals(match.Card.Id, TimedShieldEffectId, StringComparison.Ordinal);
                    if (candidatePreferred && !currentPreferred)
                    {
                        takeCandidate = true;
                    }
                    else if (effectInfo.HasDuration && !match.HasDuration)
                    {
                        takeCandidate = true;
                    }
                }

                if (takeCandidate)
                {
                    match = effectInfo;
                    shieldValue = candidateValue;
                }
            }

            return match != null;
        }
    }
}
