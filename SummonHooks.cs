using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SkeletonChoice
{
    internal sealed class CastRequest
    {
        internal readonly SummonMode Mode;
        internal readonly Player Caster;
        internal SpiritCast Spirit;
        internal int SelectedCount;
        internal CastRequest(SummonMode mode, Player caster) { Mode = mode; Caster = caster; }
    }

    internal static class SummonHooks
    {
        private static ConditionalWeakTable<SpawnAbility, CastRequest> requests = new ConditionalWeakTable<SpawnAbility, CastRequest>();
        [ThreadStatic] private static CastRequest current;
        private static readonly FieldInfo seedField = AccessTools.Field(typeof(Humanoid), "m_seed");
        internal static MethodInfo SpawnStep { get; private set; }
        private static FieldInfo abilityField;
        private static int reportedSelections;

        internal static void VerifyHooks()
        {
            SpawnStep = AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(SpawnAbility), "Spawn"));
            if (SpawnStep == null || seedField == null) throw new MissingMethodException("Valheim summoning hooks could not be found.");
            abilityField = AccessTools.Field(SpawnStep.DeclaringType, "<>4__this");
            if (abilityField == null) throw new MissingFieldException("Spawn coroutine does not expose its owner.");
        }

        internal static void Clear()
        {
            current = null;
            requests = new ConditionalWeakTable<SpawnAbility, CastRequest>();
            reportedSelections = 0;
            SpiritCaller.Clear();
        }

        internal static bool IsDeadRaiser(ItemDrop.ItemData item)
        {
            return item != null && item.m_dropPrefab != null && item.m_dropPrefab.name == "StaffSkeleton";
        }

        [HarmonyPatch(typeof(SpawnAbility), "Setup")]
        private static class CaptureCast
        {
            private static bool Prefix(SpawnAbility __instance, Character owner, ItemDrop.ItemData item)
            {
                if (requests.TryGetValue(__instance, out CastRequest old)) SpiritCaller.Release(old.Spirit);
                requests.Remove(__instance);
                Plugin plugin = Plugin.Instance;
                if (plugin == null || !plugin.ModEnabled || owner == null || owner != Player.m_localPlayer) return true;
                // Area attacks may pass null as Setup's weapon argument.
                ItemDrop.ItemData weapon = item ?? Player.m_localPlayer.GetCurrentWeapon();
                if (SpiritCaller.IsStaff(weapon))
                {
                    if (!SpiritCaller.Prepare(__instance, (Player)owner, weapon, plugin.SelectedSpirit, out SpiritCast spirit))
                    {
                        Object.Destroy(__instance.gameObject);
                        return false;
                    }
                    if (spirit != null) requests.Add(__instance, new CastRequest(SummonMode.Random, (Player)owner) { Spirit = spirit });
                    return true;
                }
                if (!IsDeadRaiser(weapon) || !SpawnsFriendlySkeleton(__instance)) return true;
                SummonMode mode = plugin.SelectedMode;
                requests.Add(__instance, new CastRequest(mode, (Player)owner));
                if (plugin.DebugLogging)
                    plugin.Log.LogInfo("Captured Dead Raiser cast: " + mode + "; weapon supplied=" + (item != null));
                return true;
            }
        }

        private struct SpawnStepState
        {
            internal CastRequest Previous;
            internal SpawnAbility Ability;
            internal GameObject[] OriginalPrefabs;
        }

        [HarmonyPatch]
        private static class SpawnStepScope
        {
            private static MethodBase TargetMethod() { return SpawnStep; }
            private static void Prefix(object __instance, out SpawnStepState __state)
            {
                __state = new SpawnStepState { Previous = current };
                current = null;
                SpawnAbility ability = abilityField.GetValue(__instance) as SpawnAbility;
                CastRequest request;
                if (ability == null || !requests.TryGetValue(ability, out request)) return;
                current = request;
                if (request.Spirit != null && request.Spirit.Prefabs != null)
                {
                    // Only narrow this cast's instance while native spawning code executes.
                    __state.Ability = ability;
                    __state.OriginalPrefabs = ability.m_spawnPrefab;
                    ability.m_spawnPrefab = request.Spirit.Prefabs;
                }
            }
            // The scope surrounds only one MoveNext, never time spent waiting between frames.
            private static void Finalizer(Exception __exception, SpawnStepState __state)
            {
                if (__exception != null && current != null) SpiritCaller.Release(current.Spirit);
                if (__state.Ability != null) __state.Ability.m_spawnPrefab = __state.OriginalPrefabs;
                current = __state.Previous;
            }
            private static void Postfix(bool __result)
            {
                CastRequest request = current;
                if (!__result && request != null && request.Spirit != null) SpiritCaller.Release(request.Spirit);
                if (!__result && request != null && request.Mode != SummonMode.Random && request.SelectedCount == 0 && Plugin.Instance.DebugLogging)
                    Plugin.Instance.Log.LogWarning("Dead Raiser cast finished without selecting equipment (the summon may have been blocked by its limit or spawn point).");
            }
        }

        [HarmonyPatch(typeof(Character), "Awake")]
        private static class TrackSpiritSpawn
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Character __instance)
            {
                CastRequest request = current;
                if (request == null || request.Spirit == null || __instance == null || __instance.IsPlayer()) return;
                ZNetView view = __instance.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner()) return;
                ZDO zdo = view.GetZDO();
                if (!SpiritCaller.TryGetSpecies(zdo.GetPrefab(), out SpiritMode species)) return;
                if (request.Spirit.Prefabs != null && request.Spirit.Species != species) return;
                SummonIndicator.MarkSummon(zdo, request.Caster);
                SpiritCaller.Release(request.Spirit);
                Plugin plugin = Plugin.Instance;
                if (plugin.DebugLogging || reportedSelections++ < 12)
                    plugin.Log.LogInfo("Summoned Spirit Caller spirit: " + species + ".");
            }
        }

        [HarmonyPatch(typeof(Humanoid), "Awake")]
        private static class SelectEquipmentSeed
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Humanoid __instance)
            {
                CastRequest request = current;
                if (request == null || request.Spirit != null || __instance == null || __instance.IsPlayer()) return;
                if (!__instance.gameObject.name.StartsWith("Skeleton_Friendly", StringComparison.Ordinal)) return;
                ZNetView view = __instance.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner()) return;
                SummonIndicator.MarkSummon(view.GetZDO(), request.Caster);
                if (request.Mode == SummonMode.Random) return;
                Random.State saved = Random.state;
                try
                {
                    EquipmentTable table = ReadTable(__instance);
                    int original = (int)seedField.GetValue(__instance);
                    int chosen;
                    if (!SelectionPolicy.TryChoose(table, request.Mode, original, new UnitySeedRandom(), out chosen))
                    {
                        Warn(request, "The summoned skeleton has no matching vanilla equipment combination.");
                        return;
                    }
                    view.GetZDO().Set(ZDOVars.s_seed, chosen);
                    seedField.SetValue(__instance, chosen);
                    request.SelectedCount++;
                    // Retain a short startup trace even when unlimited summon logging is disabled.
                    if (Plugin.Instance.DebugLogging || reportedSelections++ < 12)
                        Plugin.Instance.Log.LogInfo("Selected " + request.Mode + " for " + __instance.gameObject.name + "; synchronized vanilla equipment seed.");
                }
                catch (Exception error)
                {
                    Plugin.Instance.Log.LogError("Equipment selection failed; vanilla summon retained. " + error);
                    Warn(request, "Could not select equipment; vanilla summon retained.");
                }
                finally { Random.state = saved; }
            }
        }

        private static bool SpawnsFriendlySkeleton(SpawnAbility ability)
        {
            if (ability.m_spawnPrefab == null) return false;
            foreach (GameObject prefab in ability.m_spawnPrefab)
                if (prefab != null && prefab.name.StartsWith("Skeleton_Friendly", StringComparison.Ordinal)) return true;
            return false;
        }

        private static void Warn(CastRequest request, string detail)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null) return;
            plugin.Log.LogWarning(detail);
            if (request.Caster != null)
                request.Caster.Message(MessageHud.MessageType.Center, "Skeleton type could not be selected. See the log.");
        }

        private static EquipmentTable ReadTable(Humanoid humanoid)
        {
            EquipmentTable table = new EquipmentTable
            {
                Defaults = ReadItems(humanoid.m_defaultItems),
                Shields = ReadItems(humanoid.m_randomShield),
                Weapons = ReadItems(humanoid.m_randomWeapon),
                Armor = ReadItems(humanoid.m_randomArmor)
            };
            Humanoid.ItemSet[] sets = humanoid.m_randomSets ?? new Humanoid.ItemSet[0];
            table.Sets = new EquipmentItem[sets.Length][];
            for (int i = 0; i < sets.Length; i++) table.Sets[i] = ReadItems(sets[i].m_items);
            Humanoid.RandomItem[] items = humanoid.m_randomItems ?? new Humanoid.RandomItem[0];
            table.Items = new ChanceItem[items.Length];
            for (int i = 0; i < items.Length; i++)
                table.Items[i] = new ChanceItem { Item = ReadItem(items[i].m_prefab), Chance = items[i].m_chance };
            return table;
        }

        private static EquipmentItem[] ReadItems(GameObject[] prefabs)
        {
            if (prefabs == null) return new EquipmentItem[0];
            EquipmentItem[] items = new EquipmentItem[prefabs.Length];
            for (int i = 0; i < items.Length; i++) items[i] = ReadItem(prefabs[i]);
            return items;
        }

        private static EquipmentItem ReadItem(GameObject prefab)
        {
            if (prefab == null) return null;
            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            if (drop == null) throw new InvalidOperationException("Equipment prefab has no ItemDrop: " + prefab.name);
            ItemDrop.ItemData item = drop.m_itemData;
            EquipmentKind kind = EquipmentKind.Other;
            if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield) kind = EquipmentKind.Shield;
            else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow) kind = EquipmentKind.Bow;
            else if (item.IsWeapon())
                kind = item.m_shared.m_skillType == Skills.SkillType.Swords ? EquipmentKind.Sword : EquipmentKind.OtherWeapon;
            return new EquipmentItem(kind, (int)item.m_shared.m_itemType);
        }

        private sealed class UnitySeedRandom : ISeedRandom
        {
            public void Initialize(int seed) { Random.InitState(seed); }
            public int Index(int count) { return Random.Range(0, count); }
            public float Value() { return Random.value; }
        }
    }
}
