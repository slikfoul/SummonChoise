using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SkeletonChoice
{
    internal sealed class SpiritCast
    {
        internal GameObject[] Prefabs;
        internal SpiritMode Species;
        internal SpawnAbility Ability;
        internal Player Caster;
        internal float Expires;
    }

    internal static class SpiritCaller
    {
        internal const string Staff = "StaffSpiritCaller";
        private static readonly Dictionary<int, SpiritMode> types = new Dictionary<int, SpiritMode>
        {
            { "Bjorn_spiritcaller".GetStableHashCode(), SpiritMode.Bear },
            { "Moose_spiritcaller".GetStableHashCode(), SpiritMode.Moose },
            { "Wolf_spiritcaller".GetStableHashCode(), SpiritMode.Wolf },
            { "Boar_spiritcaller".GetStableHashCode(), SpiritMode.Boar }
        };
        private static readonly List<SpiritCast> pending = new List<SpiritCast>();

        internal static bool IsStaff(ItemDrop.ItemData item)
        {
            return item != null && item.m_dropPrefab != null && item.m_dropPrefab.name == Staff;
        }

        internal static bool TryGetSpecies(int prefabHash, out SpiritMode species)
        {
            return types.TryGetValue(prefabHash, out species);
        }

        private static SpiritMode[] Candidates(GameObject[] prefabs)
        {
            if (prefabs == null) return new SpiritMode[0];
            var result = new SpiritMode[prefabs.Length];
            for (int i = 0; i < prefabs.Length; i++)
                if (prefabs[i] != null) TryGetSpecies(prefabs[i].name.GetStableHashCode(), out result[i]);
            return result;
        }

        private static Dictionary<SpiritMode, int> Occupied(Player caster)
        {
            var occupied = new Dictionary<SpiritMode, int>();
            SummonIndicator.CollectSpirits(caster, occupied);
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                SpiritCast reservation = pending[i];
                if (reservation.Ability == null || reservation.Caster != caster || Time.time > reservation.Expires)
                    pending.RemoveAt(i);
                else SpiritSelectionPolicy.Add(occupied, reservation.Species);
            }
            return occupied;
        }

        private static int Capacity(SpawnAbility ability, ItemDrop.ItemData weapon)
        {
            return ability.m_setMaxInstancesFromWeaponLevel ? Mathf.Max(1, weapon.m_quality) : 1;
        }

        internal static bool Prepare(SpawnAbility ability, Player caster, ItemDrop.ItemData weapon, SpiritMode mode, out SpiritCast cast)
        {
            cast = null;
            SpiritMode[] candidates = Candidates(ability.m_spawnPrefab);
            if (!Array.Exists(candidates, x => x >= SpiritMode.Bear && x <= SpiritMode.Boar)) return true;
            var occupied = mode == SpiritMode.Missing ? Occupied(caster) : null;
            int capacity = Capacity(ability, weapon);
            int index = SpiritSelectionPolicy.Choose(mode, candidates, occupied, capacity);
            if (index == SpiritSelectionPolicy.Unavailable)
            {
                Notify(caster, mode);
                return false;
            }
            cast = new SpiritCast { Ability = ability, Caster = caster };
            if (index == SpiritSelectionPolicy.Vanilla) return true;
            if (occupied != null && ability.m_maxSpawned > 0)
            {
                // Prefer another missing species if the first has hit the game's shared cap.
                // When all are capped, leave the stock cap check and message in charge.
                var available = (SpiritMode[])candidates.Clone();
                for (int i = 0; i < available.Length; i++)
                    if (available[i] >= SpiritMode.Bear &&
                        SpawnSystem.GetNrOfInstances(ability.m_spawnPrefab[i], caster.transform.position, 0f) >= ability.m_maxSpawned)
                        available[i] = SpiritMode.Missing;
                int eligible = SpiritSelectionPolicy.Choose(mode, available, occupied, capacity);
                if (eligible >= 0) index = eligible;
            }
            cast.Prefabs = new[] { ability.m_spawnPrefab[index] };
            cast.Species = candidates[index];
            cast.Expires = Time.time + Mathf.Max(0f, ability.m_initialSpawnDelay) + Mathf.Max(0f, ability.m_preSpawnDelay) + 15f;
            pending.Add(cast);
            return true;
        }

        private static void Notify(Player caster, SpiritMode mode)
        {
            caster.Message(MessageHud.MessageType.Center, mode == SpiritMode.Missing
                ? "All spirit slots for this staff are filled or reserved."
                : "The selected spirit is not available for this staff.");
        }

        internal static void Release(SpiritCast cast)
        {
            if (cast != null) pending.Remove(cast);
        }

        internal static void Clear() { pending.Clear(); }

        // Check before the normal attack starts, so a full roster does not spend a cast.
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static class GuardFullRoster
        {
            private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
            {
                Plugin plugin = Plugin.Instance;
                Player caster = Player.m_localPlayer;
                if (secondaryAttack || plugin == null || !plugin.ModEnabled || caster == null || __instance != caster) return true;
                SpiritMode mode = plugin.SelectedSpirit;
                if (mode == SpiritMode.Random) return true;
                ItemDrop.ItemData weapon = caster.GetCurrentWeapon();
                if (!IsStaff(weapon)) return true;
                GameObject projectile = weapon.m_shared.m_attack.m_attackProjectile;
                SpawnAbility ability = projectile != null ? projectile.GetComponent<SpawnAbility>() : null;
                if (ability == null) return true;
                SpiritMode[] candidates = Candidates(ability.m_spawnPrefab);
                if (!Array.Exists(candidates, x => x >= SpiritMode.Bear && x <= SpiritMode.Boar)) return true;
                if (SpiritSelectionPolicy.Choose(mode, candidates, mode == SpiritMode.Missing ? Occupied(caster) : null, Capacity(ability, weapon)) != SpiritSelectionPolicy.Unavailable) return true;
                Notify(caster, mode);
                __result = false;
                return false;
            }
        }
    }
}
