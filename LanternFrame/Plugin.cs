using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace LanternFrame
{
    [BepInPlugin(Guid, "Lantern Frame", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "user.lanternframe";

        internal static ConfigEntry<string> AllowedNames;
        internal static ConfigEntry<bool> LogItems;

        internal static BepInEx.Logging.ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            AllowedNames = Config.Bind("General", "AllowedItems", "lantern",
                "Comma-separated, case-insensitive fragments. An item can be placed on the horizontal item frame " +
                "if its prefab name or shared name contains one of them. Default matches the Dvergr lantern.");
            LogItems = Config.Bind("General", "LogAttachAttempts", false,
                "Log the prefab/shared name of every item you try to put on a horizontal item frame (helps finding the right name).");

            new Harmony(Guid).PatchAll();
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

    // Items without an "attach" child have no model on the stand: build one from the item prefab.
    [HarmonyPatch(typeof(ItemStand), "GetAttachPrefab")]
    internal static class ItemStandAttachPrefabPatch
    {
        private static readonly System.Collections.Generic.Dictionary<string, UnityEngine.GameObject> Cache =
            new System.Collections.Generic.Dictionary<string, UnityEngine.GameObject>();

        private static readonly string[] StripOrder =
            { "ZSyncTransform", "Floating", "ItemDrop", "ZNetView", "Rigidbody", "Collider" };

        private static void Postfix(UnityEngine.GameObject item, ref UnityEngine.GameObject __result)
        {
            if (Plugin.LogItems.Value)
                Plugin.Log.LogInfo($"GetAttachPrefab: item='{(item != null ? item.name : "null")}' result='{(__result != null ? __result.name : "null")}'");
            if (Plugin.LogItems.Value && __result != null && item != null && item.name == "Lantern")
            {
                Plugin.Log.LogInfo("Lantern attach hierarchy:");
                LogHierarchy(__result.transform, "  ");
            }
            if (__result != null || item == null) return;
            var drop = item.GetComponent<ItemDrop>();
            if (drop == null || !Plugin.IsAllowed(item.name, drop.m_itemData?.m_shared?.m_name)) return;

            if (Plugin.LogItems.Value) LogHierarchy(item.transform, "");

            UnityEngine.GameObject clone;
            if (!Cache.TryGetValue(item.name, out clone) || clone == null)
            {
                // Inactive parent => no Awake/OnEnable runs while we strip the gameplay components.
                var holder = new UnityEngine.GameObject("LanternFrame_holder");
                holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(holder);
                clone = UnityEngine.Object.Instantiate(item, holder.transform);
                clone.name = item.name + "_attach";

                var all = clone.GetComponentsInChildren<UnityEngine.Component>(true);
                foreach (var key in StripOrder)
                    foreach (var c in all)
                        if (c != null && !(c is UnityEngine.Transform) && IsKind(c, key))
                            UnityEngine.Object.DestroyImmediate(c);

                Cache[item.name] = clone;
            }
            __result = clone;
        }

        internal static void LogHierarchyPublic(UnityEngine.Transform t) { LogHierarchy(t, "    "); }

        private static bool IsKind(UnityEngine.Component c, string key)
        {
            for (var t = c.GetType(); t != null; t = t.BaseType)
                if (t.Name == key) return true;
            return false;
        }

        private static void LogHierarchy(UnityEngine.Transform t, string indent)
        {
            Plugin.Log.LogInfo(indent + t.name + " active=" + t.gameObject.activeSelf + " pos=" + t.localPosition +
                " scale=" + t.localScale + " [" + string.Join(", ",
                t.GetComponents<UnityEngine.Component>().Select(c => c.GetType().Name).ToArray()) + "]");
            foreach (UnityEngine.Transform child in t) LogHierarchy(child, indent + "  ");
        }
    }

    // The lantern's "attach" (and its "equiped" child) are inactive in the prefab and carry a
    // joint/rigidbody meant for the hand. Activate them, freeze physics and centre the model.
    [HarmonyPatch(typeof(ItemStand), "UpdateAttach")]
    internal static class ItemStandUpdateAttachPatch
    {
        private static void FixLantern(UnityEngine.GameObject visual)
        {
            foreach (var t in visual.GetComponentsInChildren<UnityEngine.Transform>(true))
            {
                if (t.name == "SFX") continue;
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            }
            foreach (var c in visual.GetComponentsInChildren<UnityEngine.Component>(true))
            {
                if (c == null) continue;
                var n = c.GetType().Name;
                if (n == "ConfigurableJoint") UnityEngine.Object.Destroy(c);
            }
            foreach (var c in visual.GetComponentsInChildren<UnityEngine.Component>(true))
            {
                if (c == null) continue;
                var n = c.GetType().Name;
                if (n.EndsWith("Collider")) UnityEngine.Object.Destroy(c);
                else if (n == "Rigidbody") UnityEngine.Object.Destroy(c);
            }
            // Cancel the prefab's hand offsets so the lantern sits on the attach point.
            visual.transform.localPosition = UnityEngine.Vector3.zero;
            var equiped = visual.transform.Find("equiped");
            if (equiped != null)
            {
                equiped.localPosition = UnityEngine.Vector3.zero;
                var mesh = equiped.Find("default");
                if (mesh != null) mesh.localPosition = UnityEngine.Vector3.zero;
            }
        }

        private static void Postfix(ItemStand __instance)
        {
            var visual = HarmonyLib.Traverse.Create(__instance).Field("m_visualItem").GetValue<UnityEngine.GameObject>();
            if (visual != null && __instance.name.StartsWith("itemstandh", StringComparison.OrdinalIgnoreCase))
            {
                var current = HarmonyLib.Traverse.Create(__instance).Field("m_currentItemName").GetValue<string>();
                if (Plugin.IsAllowed(current, null)) FixLantern(visual);
            }
            if (!Plugin.LogItems.Value) return;
            var other = __instance.m_attachOther;
            Plugin.Log.LogInfo("UpdateAttach: stand='" + __instance.name + "' visual=" + (visual != null ? visual.name : "null") +
                " attachOther=" + (other != null ? other.name + " pos=" + other.position : "null"));
            if (visual == null) return;
            Plugin.Log.LogInfo("  visual worldPos=" + visual.transform.position + " lossyScale=" + visual.transform.lossyScale);
            foreach (var r in visual.GetComponentsInChildren<UnityEngine.Renderer>(true))
                Plugin.Log.LogInfo("  renderer " + r.name + " enabled=" + r.enabled + " type=" + r.GetType().Name + " bounds=" + r.bounds.size);
            ItemStandAttachPrefabPatch.LogHierarchyPublic(visual.transform);
        }
    }

    [HarmonyPatch(typeof(ItemStand), "CanAttach")]
    internal static class ItemStandCanAttachPatch
    {
        private static void Postfix(ItemStand __instance, ItemDrop.ItemData item, ref bool __result)
        {
            // Horizontal item frame prefab is "itemstandh" (clones are named "itemstandh(Clone)").
            if (!__instance.name.StartsWith("itemstandh", StringComparison.OrdinalIgnoreCase)) return;

            if (Plugin.LogItems.Value && item != null)
                Plugin.Log.LogInfo($"Attach attempt: prefab='{item.m_dropPrefab?.name}' shared='{item.m_shared?.m_name}'");

            if (!__result && Plugin.IsAllowed(item)) __result = true;
        }
    }
}
