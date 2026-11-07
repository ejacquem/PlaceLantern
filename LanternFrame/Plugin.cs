using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace LanternFrame
{
    [BepInPlugin(Guid, "Lantern Frame", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "user.lanternframe";

        internal static ConfigEntry<string> AllowedNames;
        internal static ConfigEntry<bool> LogItems;
        internal static ConfigEntry<bool> AllowVertical;
        internal static ConfigEntry<bool> LightShadows;
        internal static ConfigEntry<float> ShadowBias;
        internal static ConfigEntry<float> ShadowNormalBias;
        internal static ConfigEntry<float> LightRangeMultiplier;

        internal static BepInEx.Logging.ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            AllowedNames = Config.Bind("General", "AllowedItems", "lantern",
                "Comma-separated, case-insensitive fragments. An item can be placed on the item frame " +
                "if its prefab name or shared name contains one of them. Default matches the Dvergr lantern.");
            AllowVertical = Config.Bind("General", "AllowOnVerticalFrame", true,
                "Also allow the items on the vertical item frame (the horizontal frame is always supported).");
            LogItems = Config.Bind("General", "LogAttachAttempts", false,
                "Log the prefab/shared name of every item you try to put on an item frame (helps finding the right name).");

            LightShadows = Config.Bind("Light", "CastShadows", true,
                "true: the lantern's light casts shadows so walls block it. false: no shadows at all. " +
                "Restart the game after changing.");
            ShadowBias = Config.Bind("Light", "ShadowBias", 0.01f,
                new ConfigDescription("Shadow bias. Lower values stop light leaking through thin walls but can cause shadow acne.",
                new AcceptableValueRange<float>(0f, 2f)));
            ShadowNormalBias = Config.Bind("Light", "ShadowNormalBias", 0.02f,
                new ConfigDescription("Shadow normal bias. High values (Unity's default is 0.4) let light leak through thin walls.",
                new AcceptableValueRange<float>(0f, 3f)));
            LightRangeMultiplier = Config.Bind("Light", "RangeMultiplier", 1f,
                new ConfigDescription("Multiplies the lantern's light range while on a frame (1 = vanilla). " +
                "Lower it to reduce how far the light reaches. Restart the game after changing.",
                new AcceptableValueRange<float>(0.1f, 2f)));

            new Harmony(Guid).PatchAll();
        }

        // Horizontal item frame prefab is "itemstandh", vertical is "itemstand"
        // (clones get a "(Clone)" suffix; trophy stands are named "prop_itemstand_*" and are not matched).
        internal static bool IsHorizontal(ItemStand stand) =>
            stand.name.StartsWith("itemstandh", StringComparison.OrdinalIgnoreCase);

        internal static bool IsSupportedStand(ItemStand stand)
        {
            if (IsHorizontal(stand)) return true;
            return AllowVertical.Value && stand.name.StartsWith("itemstand", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsAllowed(ItemDrop.ItemData item)
        {
            if (item == null) return false;
            return IsAllowed(item.m_dropPrefab != null ? item.m_dropPrefab.name : "",
                             item.m_shared != null ? item.m_shared.m_name : "");
        }

        internal static bool IsAllowed(string prefab, string shared)
        {
            prefab = prefab ?? "";
            shared = shared ?? "";
            return AllowedNames.Value
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Any(s => prefab.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                       || shared.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    // The lantern's "attach" object (and its "equiped" child) is inactive in the prefab and carries the
    // joint/rigidbody meant for the hand. Hand the stand a cleaned-up copy instead, with the lantern
    // rotated and offset to sit properly on the frame. Doing it here (rather than after the visual is
    // spawned) also covers frames that are rebuilt when the world loads.
    [HarmonyPatch(typeof(ItemStand), "GetAttachPrefab")]
    internal static class ItemStandAttachPrefabPatch
    {
        private static readonly Dictionary<string, GameObject> Cache = new Dictionary<string, GameObject>();

        // Components removed from the copy, in an order that respects their dependencies.
        private static readonly string[] StripOrder =
            { "ConfigurableJoint", "ZSyncTransform", "Floating", "ItemDrop", "ZNetView", "Collider", "Rigidbody" };

        // Horizontal frame: the lantern's own orientation already looked right there, so no extra rotation.
        private static readonly Vector3 HorizontalRotation = Vector3.zero;
        private static readonly Vector3 HorizontalPosition = Vector3.zero;
        private static readonly Vector3 VerticalRotation = new Vector3(90f, -90f, 0f);
        private static readonly Vector3 VerticalPosition = new Vector3(0f, 0.56f, 0f);

        // GetAttachPrefab is static, so the stand being updated comes from the SetVisualItem patch below.
        private static void Postfix(GameObject item, ref GameObject __result)
        {
            var stand = ItemStandSetVisualItemPatch.Current;
            if (stand == null || __result == null || item == null || !Plugin.IsSupportedStand(stand)) return;
            var drop = item.GetComponent<ItemDrop>();
            if (drop == null || !Plugin.IsAllowed(item.name, drop.m_itemData?.m_shared?.m_name)) return;

            bool horizontal = Plugin.IsHorizontal(stand);
            string key = item.name + (horizontal ? "_h" : "_v");

            GameObject clone;
            if (!Cache.TryGetValue(key, out clone) || clone == null)
            {
                clone = Build(__result, key, horizontal);
                Cache[key] = clone;
            }
            __result = clone;
        }

        private static GameObject Build(GameObject source, string key, bool horizontal)
        {
            // Inactive parent => no Awake/OnEnable runs while we strip the gameplay components.
            var holder = new GameObject("LanternFrame_holder");
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);

            var clone = UnityEngine.Object.Instantiate(source, holder.transform);
            clone.name = key;

            foreach (var type in StripOrder)
                foreach (var c in clone.GetComponentsInChildren<Component>(true))
                    if (c != null && !(c is Transform) && IsKind(c, type))
                        UnityEngine.Object.DestroyImmediate(c);

            foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                if (t.name != "SFX") t.gameObject.SetActive(true);

            // Set on the copy before it is ever enabled: LightLod reads the light's range as its base range in Awake.
            bool shadows = Plugin.LightShadows.Value;
            foreach (var light in clone.GetComponentsInChildren<Light>(true))
            {
                light.range *= Plugin.LightRangeMultiplier.Value;
                light.shadows = shadows ? UnityEngine.LightShadows.Soft : UnityEngine.LightShadows.None;
                if (shadows)
                {
                    light.shadowStrength = 1f;
                    // The default biases are large enough for light to leak through thin walls.
                    light.shadowBias = Plugin.ShadowBias.Value;
                    light.shadowNormalBias = Plugin.ShadowNormalBias.Value;
                }
            }
            // LightLod turns shadows on/off by distance (m_shadowLod) and drives their strength.
            foreach (var lod in clone.GetComponentsInChildren<LightLod>(true))
            {
                var t = Traverse.Create(lod);
                t.Field("m_shadowLod").SetValue(shadows);
                if (!shadows) continue;
                t.Field("m_shadowDistance").SetValue(Mathf.Max(t.Field("m_shadowDistance").GetValue<float>(), 20f));
                t.Field("m_baseShadowStrength").SetValue(1f);
            }

            clone.transform.localPosition = Vector3.zero;
            var equiped = clone.transform.Find("equiped");
            if (equiped != null)
            {
                var mesh = equiped.Find("default");
                if (mesh != null) mesh.localPosition = Vector3.zero;
                equiped.localRotation = Quaternion.Euler(horizontal ? HorizontalRotation : VerticalRotation);
                equiped.localPosition = horizontal ? HorizontalPosition : VerticalPosition;
            }
            return clone;
        }

        private static bool IsKind(Component c, string key)
        {
            for (var t = c.GetType(); t != null; t = t.BaseType)
                if (t.Name == key) return true;
            return false;
        }
    }

    // Remembers which stand is rebuilding its visual, for the static GetAttachPrefab patch above.
    [HarmonyPatch(typeof(ItemStand), "SetVisualItem")]
    internal static class ItemStandSetVisualItemPatch
    {
        internal static ItemStand Current;

        private static void Prefix(ItemStand __instance) => Current = __instance;

        // A finalizer runs even if the original method throws, so Current is always cleared.
        private static void Finalizer() => Current = null;
    }

    [HarmonyPatch(typeof(ItemStand), "CanAttach")]
    internal static class ItemStandCanAttachPatch
    {
        private static void Postfix(ItemStand __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (!Plugin.IsSupportedStand(__instance)) return;

            if (Plugin.LogItems.Value && item != null)
                Plugin.Log.LogInfo($"Attach attempt: prefab='{item.m_dropPrefab?.name}' shared='{item.m_shared?.m_name}'");

            if (!__result && Plugin.IsAllowed(item)) __result = true;
        }
    }
}
