using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace BnlPlugins.CombatNumbers
{
    [BepInPlugin("bnl.community.combatnumbers", "BNL Combat Numbers", "0.1.1")]
    public sealed class CombatNumbersPlugin : BaseUnityPlugin
    {
        internal static CombatNumbersPlugin Instance = null!;
        private const string HarmonyId = "bnl.community.combatnumbers";

        internal static ConfigEntry<bool> Enabled = null!;
        internal static ConfigEntry<float> Alpha = null!;

        // Damage
        internal static ConfigEntry<string> DamageColorHex = null!;
        internal static ConfigEntry<string> CritColorHex = null!;
        internal static ConfigEntry<float> DamageSizeMultiplier = null!;
        internal static ConfigEntry<bool> CombineDamage = null!;
        internal static ConfigEntry<bool> ShowAllDamage = null!;

        // Healing
        internal static ConfigEntry<string> HealColorHex = null!;
        internal static ConfigEntry<float> HealSizeMultiplier = null!;
        internal static ConfigEntry<float> MinimumHeal = null!;
        internal static ConfigEntry<bool> ShowFriendlyHealing = null!;
        internal static ConfigEntry<bool> ShowSelfHealing = null!;
        internal static ConfigEntry<bool> CombineHealing = null!;

        // Self-heal display
        internal static ConfigEntry<float> SelfHealSizeMultiplier = null!;
        internal static ConfigEntry<float> SelfHealX = null!;
        internal static ConfigEntry<float> SelfHealY = null!;

        private Harmony? _harmony;

        private void Awake()
        {
            Instance = this;

            Enabled = Config.Bind("General", "Enabled", true,
                new ConfigDescription("Enable combat number overrides.", null,
                    new ConfigurationManagerAttributes { Order = 100 }));
            Alpha = Config.Bind("General", "Alpha", 1f,
                FloatConfig.Range("Overall opacity of all combat numbers.", 0.05f, 1f, 99));

            DamageColorHex = Config.Bind("Damage Numbers", "DamageColor", "#FFFFFF",
                MakeColorDesc("Damage number color.", 90, "Damage Color"));
            CritColorHex = Config.Bind("Damage Numbers", "CritColor", "#FF6600",
                MakeColorDesc("Critical hit number color.", 89, "Crit Color"));
            DamageSizeMultiplier = Config.Bind("Damage Numbers", "SizeMultiplier", 2f,
                FloatConfig.Range("Damage number size multiplier.", 0.25f, 5f, 88));
            CombineDamage = Config.Bind("Damage Numbers", "CombineDamage", false,
                new ConfigDescription("Keep accumulating hits on the same number until it fades.",
                    null, new ConfigurationManagerAttributes { Order = 87 }));
            ShowAllDamage = Config.Bind("Damage Numbers", "ShowAllDamage", false,
                new ConfigDescription("Show damage numbers from all sources instead of only your own damage.",
                    null, new ConfigurationManagerAttributes { Order = 86 }));

            HealColorHex = Config.Bind("Healing Numbers", "HealColor", "#91ED78",
                MakeColorDesc("Heal number color.", 90, "Heal Color"));
            HealSizeMultiplier = Config.Bind("Healing Numbers", "SizeMultiplier", 2f,
                FloatConfig.Range("Heal number size multiplier.", 0.25f, 5f, 89));
            MinimumHeal = Config.Bind("Healing Numbers", "MinimumHeal", 0.5f,
                FloatConfig.Range("Minimum heal amount to show a number.", 0f, 100f, 88));
            ShowFriendlyHealing = Config.Bind("Healing Numbers", "ShowFriendlyHealing", false,
                new ConfigDescription("Show heal numbers on friendly units.",
                    null, new ConfigurationManagerAttributes { Order = 87 }));
            ShowSelfHealing = Config.Bind("Healing Numbers", "ShowSelfHealing", false,
                new ConfigDescription("Show heal numbers when you heal yourself.",
                    null, new ConfigurationManagerAttributes { Order = 86 }));
            CombineHealing = Config.Bind("Healing Numbers", "CombineHealing", false,
                new ConfigDescription("Accumulate rapid heals into a single number until it fades.",
                    null, new ConfigurationManagerAttributes { Order = 85 }));

            SelfHealSizeMultiplier = Config.Bind("Self Heal", "SizeMultiplier", 0.7f,
                FloatConfig.Range("Size multiplier for self-heal numbers.", 0.1f, 5f, 90));
            SelfHealX = Config.Bind("Self Heal", "OffsetX", 0f,
                FloatConfig.Range("Horizontal screen offset for self-heal numbers (pixels).", -1000f, 1000f, 89));
            SelfHealY = Config.Bind("Self Heal", "OffsetY", 0f,
                FloatConfig.Range("Vertical screen offset for self-heal numbers (pixels).", -1000f, 1000f, 88));

            FloatConfig.BindRound(Alpha, 0.05f, 1f);
            FloatConfig.BindRound(DamageSizeMultiplier, 0.25f, 5f);
            FloatConfig.BindRound(HealSizeMultiplier, 0.25f, 5f);
            FloatConfig.BindRound(MinimumHeal, 0f, 100f);
            FloatConfig.BindRound(SelfHealSizeMultiplier, 0.1f, 5f);
            FloatConfig.BindRound(SelfHealX, -1000f, 1000f);
            FloatConfig.BindRound(SelfHealY, -1000f, 1000f);

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(CombatNumbersPlugin).Assembly);
            Logger.LogInfo("[BNL Combat Numbers] Loaded");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchAll(HarmonyId);
        }

        private static ConfigDescription MakeColorDesc(string description, int order, string displayName)
        {
            return new ConfigDescription(description, null, new ConfigurationManagerAttributes
            {
                CustomDrawer = ColorDrawer.Draw,
                Order = order,
                DispName = displayName,
            });
        }
    }

    // Attached to each GuiDamageNumberDetector — handles heal spawning, combine-damage,
    // and styling of game-spawned damage numbers.
    internal sealed class CombatNumbersRuntime : MonoBehaviour
    {
        private const float CollectTime = 0.15f;
        private const float HealContinueGrace = 2.5f;
        private const float StandardDisplayHold = 1.0f;

        private static readonly FieldInfo HealthbarMakerField =
            typeof(GuiHealthbar).GetField("maker", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private GuiDamageNumberDetector _detector = null!;
        private Action? _unsubscribeHealth;
        private static CombatNumbersRuntime? _current;
        private bool _suppressDamageValueHook;
        private readonly Dictionary<uint, List<PendingDamageInfo>> _pendingDamage = new Dictionary<uint, List<PendingDamageInfo>>();

        // Per-unit combine tracking (keyed by unit instance ID)
        private readonly Dictionary<uint, ActiveHealNumber> _activeHeals = new Dictionary<uint, ActiveHealNumber>();
        private readonly Dictionary<uint, ActiveDamageNumber> _activeDamage = new Dictionary<uint, ActiveDamageNumber>();

        // Track already-styled damage numbers
        private readonly HashSet<int> _styledDamage = new HashSet<int>();
        private readonly HashSet<int> _attachedNumbers = new HashSet<int>();

        // Original CollectHitTime so we can restore it
        private float _originalCollectHitTime = -1f;

        internal void Init(GuiDamageNumberDetector detector)
        {
            _detector = detector;
            _originalCollectHitTime = detector.CollectHitTime;
            _current = this;
        }

        private void Start()
        {
            _current = this;
            var messenger = Singleton<ZoneMessenger>.Instance;
            if (messenger != null)
            {
                Action<GlobalUnitHealthChangeArgs> handler = OnHealthChange;
                messenger.OnGlobalUnitHealthChange.Subscribe(handler, ref _unsubscribeHealth);
            }
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(_current, this))
                _current = null;
            _unsubscribeHealth?.Invoke();
            if (_detector != null && _originalCollectHitTime >= 0f)
                _detector.CollectHitTime = _originalCollectHitTime;
        }

        private void Update()
        {
            if (!CombatNumbersPlugin.Enabled.Value) return;

            // Combine damage: keep collect window open indefinitely by setting a huge CollectHitTime
            if (_detector != null && _originalCollectHitTime >= 0f)
            {
                _detector.CollectHitTime = CombatNumbersPlugin.CombineDamage.Value ? 99999f : _originalCollectHitTime;
            }

            CleanupPendingDamage();
        }

        internal static void NotifyGlobalDamage(DamageInfo args)
        {
            if (_current == null || args == null)
                return;
            _current.RecordDamageInfo(args);
        }

        internal static void NotifyDamageNumberStart(GuiDamageNumber number)
        {
            if (_current == null || !CombatNumbersPlugin.Enabled.Value || number == null)
                return;
            _current.HandleDamageNumber(number, number.DamageValue);
        }

        internal static void NotifyDamageValueChanged(GuiDamageNumber number, float value)
        {
            if (_current == null || !CombatNumbersPlugin.Enabled.Value || number == null)
                return;
            _current.HandleDamageNumber(number, value);
        }

        internal static void NotifyDamageNumberDestroyed(GuiDamageNumber number)
        {
            if (_current == null || number == null)
                return;
            _current.RemoveDestroyedDamageNumber(number);
        }

        private void HandleDamageNumber(GuiDamageNumber number, float value)
        {
            if (_suppressDamageValueHook || number == null || number.Damage == null)
                return;
            if (number.gameObject.GetComponent<HealingMarker>() != null)
                return;

            Unit unit = number.Unit;
            if (unit == null)
                return;

            PendingDamageInfo pending;
            bool hasPending = TryTakePendingDamage(unit.Id, out pending);
            bool crit = hasPending ? pending.Crit : IsCritNumber(number);
            bool shouldShow = hasPending ? pending.ShouldShow : ShouldShowDamageNumber(number);
            if (!shouldShow)
            {
                RemoveTrackedDamageNumber(number);
                Destroy(number.gameObject);
                return;
            }
            uint key = unit.Id;

            if (!CombatNumbersPlugin.CombineDamage.Value)
            {
                RefreshDamageHold(number);
                ApplyDamageNumber(number, crit);
                _activeDamage[key] = new ActiveDamageNumber
                {
                    Number = number,
                    Value = value,
                    IsCrit = crit,
                    LastTime = Time.time
                };
                return;
            }

            ActiveDamageNumber active;
            if (_activeDamage.TryGetValue(key, out active) && active.Number != null)
            {
                if (active.Number == number)
                {
                    active.Value = value;
                    active.IsCrit = crit;
                    active.LastTime = Time.time;
                    RefreshDamageHold(active.Number);
                    ApplyDamageNumber(active.Number, active.IsCrit);
                    return;
                }

                float combined = active.Value + value;
                if (active.Number.gameObject != null)
                    Destroy(active.Number.gameObject);

                active.Number = number;
                active.Value = combined;
                active.IsCrit = crit;
                active.LastTime = Time.time;

                try
                {
                    _suppressDamageValueHook = true;
                    number.DamageValue = combined;
                }
                finally
                {
                    _suppressDamageValueHook = false;
                }

                if (number.Damage != null)
                    number.Damage.text = Mathf.RoundToInt(combined).ToString();

                RefreshDamageHold(number);
                ApplyDamageNumber(number, active.IsCrit);
                return;
            }

            if (_activeDamage.ContainsKey(key))
                _activeDamage.Remove(key);

            RefreshDamageHold(number);
            ApplyDamageNumber(number, crit);
            _activeDamage[key] = new ActiveDamageNumber
            {
                Number = number,
                Value = value,
                IsCrit = crit,
                LastTime = Time.time
            };
        }

        private void RemoveDestroyedDamageNumber(GuiDamageNumber number)
        {
            if (number == null)
                return;

            RemoveTrackedDamageNumber(number);
        }

        private void RemoveTrackedDamageNumber(GuiDamageNumber number)
        {
            uint? keyToRemove = null;
            foreach (var pair in _activeDamage)
            {
                if (pair.Value != null && ReferenceEquals(pair.Value.Number, number))
                {
                    keyToRemove = pair.Key;
                    break;
                }
            }

            if (keyToRemove.HasValue)
                _activeDamage.Remove(keyToRemove.Value);
        }

        private void RecordDamageInfo(DamageInfo args)
        {
            bool shouldShow = ShouldShowDamageInfo(args);
            List<PendingDamageInfo> queue;
            if (!_pendingDamage.TryGetValue(args.TargetUnitId, out queue))
            {
                queue = new List<PendingDamageInfo>();
                _pendingDamage[args.TargetUnitId] = queue;
            }

            queue.Add(new PendingDamageInfo
            {
                Crit = args.Crit,
                ShouldShow = shouldShow,
                Time = Time.time
            });
        }

        private void CleanupPendingDamage()
        {
            if (_pendingDamage.Count == 0)
                return;

            List<uint>? removeKeys = null;
            foreach (var pair in _pendingDamage)
            {
                pair.Value.RemoveAll(info => (Time.time - info.Time) > 1f);
                if (pair.Value.Count == 0)
                {
                    if (removeKeys == null)
                        removeKeys = new List<uint>();
                    removeKeys.Add(pair.Key);
                }
            }

            if (removeKeys == null)
                return;

            for (int i = 0; i < removeKeys.Count; i++)
                _pendingDamage.Remove(removeKeys[i]);
        }

        private bool TryTakePendingDamage(uint unitId, out PendingDamageInfo pending)
        {
            List<PendingDamageInfo> queue;
            if (_pendingDamage.TryGetValue(unitId, out queue) && queue.Count > 0)
            {
                pending = queue[0];
                queue.RemoveAt(0);
                if (queue.Count == 0)
                    _pendingDamage.Remove(unitId);
                return true;
            }

            pending = null!;
            return false;
        }

        private static bool ShouldShowDamageInfo(DamageInfo args)
        {
            if (args == null)
                return false;
            if (CombatNumbersPlugin.ShowAllDamage.Value)
                return true;
            if (args.SourceUnitId == null)
                return false;

            Unit source = Singleton<UnitsRegistry>.Instance.Get(args.SourceUnitId.Value);
            if (source == null)
                return false;
            if (source.IsMyPlayer)
                return true;

            return source.UnitCard != null &&
                   source.UnitCard.TreatHitsAsOwnerHits &&
                   source.OwnerPlayerId == Singleton<PlayerData>.Instance.Id;
        }

        private static bool ShouldShowDamageNumber(GuiDamageNumber number)
        {
            if (number == null)
                return false;
            if (CombatNumbersPlugin.ShowAllDamage.Value)
                return true;

            return number.IsMeCaster;
        }

        private void ApplyDamageNumber(GuiDamageNumber number, bool crit)
        {
            Color damageColor, critColor;
            if (!ColorHelper.TryParseHex(CombatNumbersPlugin.DamageColorHex.Value, out damageColor))
                damageColor = Color.white;
            if (!ColorHelper.TryParseHex(CombatNumbersPlugin.CritColorHex.Value, out critColor))
                critColor = new Color(1f, 0.4f, 0f);
            damageColor.a = Mathf.Clamp(CombatNumbersPlugin.Alpha.Value, 0.05f, 1f);
            critColor.a = Mathf.Clamp(CombatNumbersPlugin.Alpha.Value, 0.05f, 1f);
            float scale = Mathf.Clamp(CombatNumbersPlugin.DamageSizeMultiplier.Value, 0.25f, 5f);

            ApplyDamageNumber(number, crit, damageColor, critColor, scale);
        }

        private void ApplyDamageNumber(GuiDamageNumber number, bool crit, Color damageColor, Color critColor, float scale)
        {
            if (number == null || number.Damage == null)
                return;

            AttachToHealthbarUi(number, number.Unit, 65f);
            int id = number.GetInstanceID();
            if (!_styledDamage.Contains(id))
            {
                _styledDamage.Add(id);
                number.Damage.fontSize = Mathf.Max(1, Mathf.RoundToInt(number.Damage.fontSize * scale));
                if (Math.Abs(scale - 1f) > 0.001f)
                    number.transform.localScale = number.transform.localScale * scale;
            }

            Color color = crit ? critColor : damageColor;
            number.Damage.color = color;
            ApplyGraphics(number.gameObject, color);
        }

        // ── Heal number spawning ────────────────────────────────────────────────

        private void OnHealthChange(GlobalUnitHealthChangeArgs args)
        {
            if (!CombatNumbersPlugin.Enabled.Value) return;
            if (_detector == null || _detector.Prefab == null) return;
            if (args == null || args.unit == null) return;

            float amount = args.newHealth - args.oldHealth;
            if (amount < Mathf.Max(0f, CombatNumbersPlugin.MinimumHeal.Value)) return;

            // Skip spawn-artifact: first health update on a brand-new unit
            if (args.oldHealth == 0f && Time.time - args.unit.CreationTime < 1f) return;
            // Only units with a PlayerId (players, not blocks/objectives)
            if (args.unit.PlayerId == null) return;

            var unit = args.unit;
            bool isSelf = unit.IsMyPlayer;
            bool isFriendly = !isSelf && unit.Team.IsMy();

            if (isSelf && !CombatNumbersPlugin.ShowSelfHealing.Value) return;
            if (isFriendly && !CombatNumbersPlugin.ShowFriendlyHealing.Value) return;
            if (!isSelf && !isFriendly) return;

            SpawnOrUpdateHeal(unit, amount);
        }

        private void SpawnOrUpdateHeal(Unit unit, float amount)
        {
            uint uid = unit.Id;
            ActiveHealNumber active;
            if (_activeHeals.TryGetValue(uid, out active) && ShouldCombineHeal(active))
            {
                active.Value += amount;
                active.LastTime = Time.time;
                if (active.Number == null)
                {
                    active.Number = CreateHealNumber(unit, active.Value);
                }
                else
                {
                    ApplyHealTextAndColor(active.Number, active.Value, false);
                    RefreshHealHold(active.Number);
                }
                return;
            }

            var number = CreateHealNumber(unit, amount);
            _activeHeals[uid] = new ActiveHealNumber { Number = number, Value = amount, LastTime = Time.time };
        }

        private GuiDamageNumber CreateHealNumber(Unit unit, float amount)
        {
            var go = _detector.transform.AddChild(_detector.Prefab.gameObject, false);
            var number = go.GetComponent<GuiDamageNumber>();
            if (number == null) { Destroy(go); return null!; }
            number.gameObject.GetOrAddComponent<HealingMarker>();

            number.IsMeCaster = true;
            number.Unit = unit;
            number.DamageValue = amount;

            if (unit.IsMyPlayer)
            {
                // Self-heal: fixed HUD position, no world-follow
                var follow = number.GetComponent<GuiFollow>();
                if (follow != null) { follow.enabled = false; Destroy(follow); }
                number.transform.localPosition = new Vector3(
                    CombatNumbersPlugin.SelfHealX.Value,
                    CombatNumbersPlugin.SelfHealY.Value, 0f);
                float selfScale = Mathf.Clamp(CombatNumbersPlugin.SelfHealSizeMultiplier.Value, 0.1f, 5f);
                if (selfScale != 1f) number.transform.localScale *= selfScale;
            }
            else
            {
                AttachToHealthbarUi(number, unit, 65f);
            }

            float healScale = Mathf.Clamp(CombatNumbersPlugin.HealSizeMultiplier.Value, 0.25f, 5f);
            if (healScale != 1f) number.transform.localScale *= healScale;

            ApplyHealTextAndColor(number, amount, true);
            RefreshHealHold(number);

            return number;
        }

        private void ApplyHealTextAndColor(GuiDamageNumber number, float amount, bool applySize)
        {
            if (number == null || number.Damage == null) return;
            number.Damage.text = "+" + Mathf.RoundToInt(amount).ToString();

            Color healColor;
            if (!ColorHelper.TryParseHex(CombatNumbersPlugin.HealColorHex.Value, out healColor))
                healColor = _detector.HealColor;
            healColor.a = Mathf.Clamp(CombatNumbersPlugin.Alpha.Value, 0.05f, 1f);
            number.Damage.color = healColor;
            if (applySize)
            {
                float healScale = Mathf.Clamp(CombatNumbersPlugin.HealSizeMultiplier.Value, 0.25f, 5f);
                if (Math.Abs(healScale - 1f) > 0.001f)
                    number.Damage.fontSize = Mathf.Max(1, Mathf.RoundToInt(number.Damage.fontSize * healScale));
            }
            ApplyGraphics(number.gameObject, healColor);
        }

        private bool ShouldCombineHeal(ActiveHealNumber active)
        {
            if (active == null) return false;
            float grace = CombatNumbersPlugin.CombineHealing.Value ? HealContinueGrace : CollectTime;
            return Time.time - active.LastTime <= grace;
        }

        // ── Hold controller helpers ─────────────────────────────────────────────
        // Removes the UiTemporary auto-destroy component and stops animations so
        // combined numbers stay visible instead of fading out mid-accumulation.

        private static void RefreshHealHold(GuiDamageNumber number)
        {
            if (number == null) return;
            var hold = number.gameObject.GetComponent<HealingNumberHoldController>();
            if (hold == null)
            {
                foreach (var t in number.gameObject.GetComponentsInChildren<UiTemporary>(true))
                    Destroy(t);
                hold = number.gameObject.AddComponent<HealingNumberHoldController>();
            }
            hold.Extend(Time.time + (CombatNumbersPlugin.CombineHealing.Value ? HealContinueGrace : StandardDisplayHold));
        }

        private static void RefreshDamageHold(GuiDamageNumber number)
        {
            if (number == null) return;
            var hold = number.gameObject.GetComponent<DamageNumberHoldController>();
            if (hold == null)
            {
                foreach (var t in number.gameObject.GetComponentsInChildren<UiTemporary>(true))
                    Destroy(t);
                foreach (var animation in number.gameObject.GetComponentsInChildren<Animation>(true))
                    animation.Stop();
                foreach (var animator in number.gameObject.GetComponentsInChildren<Animator>(true))
                    animator.enabled = false;
                hold = number.gameObject.AddComponent<DamageNumberHoldController>();
            }
            hold.Extend(Time.time + StandardDisplayHold);
        }

        // ── Attach to healthbar canvas ──────────────────────────────────────────
        // Parents the number directly into the healthbar's Content RectTransform so
        // it moves with the health bar naturally. Falls back to GuiFollow if not found.

        private void AttachToHealthbarUi(GuiDamageNumber number, Unit unit, float offsetY)
        {
            if (number == null || unit == null) return;
            int id = number.GetInstanceID();
            if (_attachedNumbers.Contains(id))
            {
                if (number.transform.parent != null)
                    number.transform.localPosition = new Vector3(0f, offsetY, 0f);
                return;
            }

            var maker = unit.GetComponentInChildren<GuiHealthBarMaker>();
            if (maker == null)
            {
                number.GetOrAddComponent<GuiFollow>().WorldTarget = unit.transform;
                _attachedNumbers.Add(id);
                return;
            }

            if (HealthbarMakerField != null)
            {
                var allBars = FindObjectsOfType<GuiHealthbar>();
                foreach (var bar in allBars)
                {
                    if (bar == null) continue;
                    var barMaker = HealthbarMakerField.GetValue(bar) as GuiHealthBarMaker;
                    if (barMaker == maker && bar.Content != null)
                    {
                        number.transform.SetParent(bar.Content.transform, false);
                        number.transform.localPosition = new Vector3(0f, offsetY, 0f);
                        var follow = number.GetComponent<GuiFollow>();
                        if (follow != null) { follow.enabled = false; Destroy(follow); }
                        _attachedNumbers.Add(id);
                        return;
                    }
                }
            }

            number.GetOrAddComponent<GuiFollow>().WorldTarget = maker.transform;
            _attachedNumbers.Add(id);
        }

        private static void ApplyGraphics(GameObject go, Color color)
        {
            foreach (var g in go.GetComponentsInChildren<Graphic>(true))
                g.color = color;
        }

        private static bool IsCritNumber(GuiDamageNumber number)
        {
            return number != null &&
                   number.gameObject != null &&
                   number.gameObject.name.IndexOf("Crit", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class ActiveHealNumber
        {
            public GuiDamageNumber Number = null!;
            public float Value;
            public float LastTime;
        }

        private sealed class ActiveDamageNumber
        {
            public GuiDamageNumber Number = null!;
            public float Value;
            public bool IsCrit;
            public float LastTime;
        }

        private sealed class PendingDamageInfo
        {
            public bool Crit;
            public bool ShouldShow;
            public float Time;
        }
    }

    internal sealed class HealingMarker : MonoBehaviour { }

    // Simple MonoBehaviour that keeps a number alive until a deadline, then destroys it.
    internal sealed class HealingNumberHoldController : MonoBehaviour
    {
        private float _deadline;
        private const float FadeDuration = 0.5f;
        private float _fadeStart = -1f;

        internal void Extend(float deadline)
        {
            _deadline = Mathf.Max(_deadline, deadline);
            gameObject.SetActive(true);
            _fadeStart = -1f;
            SetAlpha(1f);
        }

        private void Update()
        {
            if (Time.time <= _deadline)
            {
                _fadeStart = -1f;
                SetAlpha(1f);
            }
            else
            {
                if (_fadeStart < 0f)
                    _fadeStart = Time.time;

                float t = Mathf.Clamp01((Time.time - _fadeStart) / FadeDuration);
                SetAlpha(1f - t);
                if (t >= 1f)
                    Destroy(gameObject);
            }
        }

        private void SetAlpha(float alpha)
        {
            foreach (var group in gameObject.GetComponentsInChildren<CanvasGroup>(true))
                group.alpha = alpha;
            foreach (var graphic in gameObject.GetComponentsInChildren<Graphic>(true))
            {
                Color color = graphic.color;
                color.a = alpha;
                graphic.color = color;
            }
        }
    }

    internal sealed class DamageNumberHoldController : MonoBehaviour
    {
        private float _deadline;
        private const float FadeDuration = 0.5f;
        private float _fadeStart = -1f;
        private GuiDamageNumber? _number;

        private void Awake()
        {
            _number = GetComponent<GuiDamageNumber>();
        }

        internal void Extend(float deadline)
        {
            _deadline = Mathf.Max(_deadline, deadline);
            gameObject.SetActive(true);
            _fadeStart = -1f;
            SetAlpha(1f);
        }

        private void Update()
        {
            if (Time.time <= _deadline)
            {
                _fadeStart = -1f;
                SetAlpha(1f);
            }
            else
            {
                if (_fadeStart < 0f)
                    _fadeStart = Time.time;

                float t = Mathf.Clamp01((Time.time - _fadeStart) / FadeDuration);
                SetAlpha(1f - t);
                if (t >= 1f)
                    Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (_number != null)
                CombatNumbersRuntime.NotifyDamageNumberDestroyed(_number);
        }

        private void SetAlpha(float alpha)
        {
            foreach (var group in gameObject.GetComponentsInChildren<CanvasGroup>(true))
                group.alpha = alpha;
            foreach (var graphic in gameObject.GetComponentsInChildren<Graphic>(true))
            {
                Color color = graphic.color;
                color.a = alpha;
                graphic.color = color;
            }
        }
    }

    // Attach CombatNumbersRuntime to each GuiDamageNumberDetector on Start.
    [HarmonyPatch(typeof(GuiDamageNumberDetector), "Start")]
    internal static class GuiDamageNumberDetectorStartPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GuiDamageNumberDetector __instance)
        {
            var runtime = __instance.gameObject.GetOrAddComponent<CombatNumbersRuntime>();
            runtime.Init(__instance);
        }
    }

    [HarmonyPatch(typeof(GuiDamageNumberDetector), "OnGlobalUnitDamage")]
    internal static class GuiDamageNumberDetectorDamagePatch
    {
        [HarmonyPrefix]
        private static void Prefix(DamageInfo args)
        {
            CombatNumbersRuntime.NotifyGlobalDamage(args);
        }
    }

    [HarmonyPatch(typeof(GuiDamageNumber), "Start")]
    internal static class GuiDamageNumberStartPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GuiDamageNumber __instance)
        {
            CombatNumbersRuntime.NotifyDamageNumberStart(__instance);
        }
    }

    [HarmonyPatch(typeof(GuiDamageNumber), "set_DamageValue")]
    internal static class GuiDamageNumberDamageValuePatch
    {
        [HarmonyPostfix]
        private static void Postfix(GuiDamageNumber __instance, float __0)
        {
            CombatNumbersRuntime.NotifyDamageValueChanged(__instance, __0);
        }
    }
}
