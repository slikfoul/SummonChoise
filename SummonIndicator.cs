using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace SkeletonChoice
{
    // Render through the vanilla effect list without registering a gameplay effect,
    // changing stats, or saving unknown effect IDs into the player's save.
    public sealed class SummonedSkeletonsEffect : StatusEffect
    {
        internal int Count;
        internal string Caption;
        public override string GetIconText() { return Count.ToString(CultureInfo.InvariantCulture); }
        public override string GetTooltipString() { return Caption + ": " + GetIconText(); }
    }

    internal static class SummonIndicator
    {
        internal static readonly int SummonerKey = "Slikfoul.SkeletonChoice.Summoner".GetStableHashCode();
        private static readonly List<SummonSnapshot> snapshots = new List<SummonSnapshot>();
        private static readonly List<SummonSnapshot> spiritSnapshots = new List<SummonSnapshot>();
        private static readonly HashSet<ZDOID> unsummoning = new HashSet<ZDOID>();
        private static readonly HashSet<ZDOID> loadedIds = new HashSet<ZDOID>();
        private static SummonedSkeletonsEffect effect;
        private static SummonedSkeletonsEffect spiritEffect;
        private static Player player;
        private static ZDOMan world;
        private static float nextScan;
        private static int count;
        private static int spiritCount;

        internal static void MarkSummon(ZDO zdo, Player caster)
        {
            if (zdo == null || caster == null) return;
            long id = caster.GetPlayerID();
            if (id == 0) return;
            zdo.Set(SummonerKey, id);
            nextScan = 0f;
        }

        internal static void Update()
        {
            Player local = Player.m_localPlayer;
            if (local != player || world != ZDOMan.instance)
            {
                count = 0;
                spiritCount = 0;
                snapshots.Clear();
                spiritSnapshots.Clear();
                unsummoning.Clear();
                loadedIds.Clear();
                nextScan = 0f;
                player = local;
                world = ZDOMan.instance;
            }
            Plugin plugin = Plugin.Instance;
            if (plugin == null || !plugin.ModEnabled || (!plugin.ShowSummonBuff && !plugin.ShowSpiritBuff) || local == null || local.IsDead() || world == null)
            {
                count = 0;
                spiritCount = 0;
                nextScan = 0f;
                return;
            }
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.5f;
            long playerId = local.GetPlayerID();
            snapshots.Clear();
            spiritSnapshots.Clear();
            loadedIds.Clear();
            // Vanilla Tameable.UnsummonMaxInstances also enumerates loaded characters.
            // Cached ZDOs alone do not prove that a creature is still present/alive.
            foreach (Character character in Character.GetAllCharacters())
            {
                if (!TryRead(character, playerId, out ZDO zdo, out SummonSnapshot snapshot)) continue;
                loadedIds.Add(zdo.m_uid);
                if (SpiritCaller.TryGetSpecies(zdo.GetPrefab(), out _)) spiritSnapshots.Add(snapshot);
                else snapshots.Add(snapshot);
            }
            unsummoning.IntersectWith(loadedIds);
            count = SummonCountPolicy.Count(playerId, snapshots);
            spiritCount = SummonCountPolicy.Count(playerId, spiritSnapshots);
        }

        internal static void CollectSpirits(Player caster, IDictionary<SpiritMode, int> occupied)
        {
            long playerId = caster.GetPlayerID();
            var seen = new HashSet<ZDOID>();
            foreach (Character character in Character.GetAllCharacters())
                if (TryRead(character, playerId, out ZDO zdo, out SummonSnapshot snapshot) &&
                    SpiritCaller.TryGetSpecies(zdo.GetPrefab(), out SpiritMode species) && SummonCountPolicy.IsActive(playerId, snapshot) && seen.Add(zdo.m_uid))
                    SpiritSelectionPolicy.Add(occupied, species);
        }

        private static bool TryRead(Character character, long playerId, out ZDO zdo, out SummonSnapshot snapshot)
        {
            zdo = null;
            snapshot = null;
            if (playerId == 0 || character == null || character.IsPlayer() || !character.gameObject.activeInHierarchy) return false;
            ZNetView view = character.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;
            zdo = view.GetZDO();
            if (zdo == null || !zdo.IsValid() || zdo.GetLong(SummonerKey) != playerId) return false;
            Tameable tameable = character.GetComponent<Tameable>();
            MonsterAI ai = character.GetComponent<MonsterAI>();
            GameObject followTarget = ai != null ? ai.GetFollowTarget() : null;
            bool following = followTarget != null;
            float distance = following ? Vector3.Distance(followTarget.transform.position, character.transform.position) : 0f;
            snapshot = new SummonSnapshot(zdo.m_uid.ToString(), playerId, true, character.IsDead() ? 0f : character.GetHealth(),
                unsummoning: unsummoning.Contains(zdo.m_uid), hasFollowTarget: following, followDistance: distance,
                unsummonDistance: tameable != null ? tameable.m_unsummonDistance : 0f);
            return true;
        }

        private static SummonedSkeletonsEffect GetEffect(ref SummonedSkeletonsEffect status, string title, string staffName, int amount)
        {
            if (status == null)
            {
                status = ScriptableObject.CreateInstance<SummonedSkeletonsEffect>();
                status.name = "SkeletonChoice_Summoned" + title;
                status.m_name = title;
                status.Caption = "Your summoned " + title.ToLowerInvariant();
                status.m_ttl = 0f;
                status.m_isNew = false;
            }
            if (status.m_icon == null && ObjectDB.instance != null)
            {
                GameObject staff = ObjectDB.instance.GetItemPrefab(staffName);
                ItemDrop item = staff != null ? staff.GetComponent<ItemDrop>() : null;
                if (item != null && item.m_itemData.m_shared.m_icons.Length > 0)
                    status.m_icon = item.m_itemData.m_shared.m_icons[0];
            }
            status.Count = amount;
            return status;
        }

        internal static void Clear()
        {
            if (effect != null) Object.Destroy(effect);
            if (spiritEffect != null) Object.Destroy(spiritEffect);
            effect = null;
            spiritEffect = null;
            player = null;
            world = null;
            nextScan = 0f;
            count = 0;
            spiritCount = 0;
            snapshots.Clear();
            spiritSnapshots.Clear();
            unsummoning.Clear();
            loadedIds.Clear();
        }

        [HarmonyPatch(typeof(Tameable), "RPC_UnSummon")]
        private static class TrackUnsummon
        {
            private static void Prefix(Tameable __instance)
            {
                Player local = Player.m_localPlayer;
                if (local == null) return;
                ZNetView view = __instance.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) return;
                ZDO zdo = view.GetZDO();
                if (zdo == null || zdo.GetLong(SummonerKey) != local.GetPlayerID()) return;
                unsummoning.Add(zdo.m_uid);
                nextScan = 0f;
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.GetHUDStatusEffects))]
        private static class AddIndicator
        {
            private static void Postfix(SEMan __instance, List<StatusEffect> effects)
            {
                Plugin plugin = Plugin.Instance;
                Player local = Player.m_localPlayer;
                if (plugin == null || local == null || local != player || world != ZDOMan.instance || __instance != local.GetSEMan()) return;
                bool alive = !local.IsDead();
                if (SummonCountPolicy.Visible(plugin.ModEnabled, plugin.ShowSummonBuff, alive, count))
                {
                    SummonedSkeletonsEffect status = GetEffect(ref effect, "Skeletons", "StaffSkeleton", count);
                    if (status.m_icon != null && !effects.Contains(status)) effects.Add(status);
                }
                if (SummonCountPolicy.Visible(plugin.ModEnabled, plugin.ShowSpiritBuff, alive, spiritCount))
                {
                    SummonedSkeletonsEffect status = GetEffect(ref spiritEffect, "Spirits", SpiritCaller.Staff, spiritCount);
                    if (status.m_icon != null && !effects.Contains(status)) effects.Add(status);
                }
            }
        }
    }
}
