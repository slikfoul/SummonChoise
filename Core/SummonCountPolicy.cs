using System;
using System.Collections.Generic;

namespace SkeletonChoice
{
    internal sealed class SummonSnapshot
    {
        internal readonly string Id;
        internal readonly long Summoner;
        internal readonly bool Exists;
        internal readonly float Health;
        internal readonly bool Loaded;
        internal readonly bool Unsummoning;
        internal readonly bool HasFollowTarget;
        internal readonly float FollowDistance;
        internal readonly float UnsummonDistance;
        internal SummonSnapshot(string id, long summoner, bool exists, float health,
            bool loaded = true, bool unsummoning = false, bool hasFollowTarget = false,
            float followDistance = 0f, float unsummonDistance = 0f)
        {
            Id = id; Summoner = summoner; Exists = exists; Health = health;
            Loaded = loaded; Unsummoning = unsummoning; HasFollowTarget = hasFollowTarget;
            FollowDistance = followDistance; UnsummonDistance = unsummonDistance;
        }
    }

    internal static class SummonCountPolicy
    {
        internal static int Count(long playerId, IEnumerable<SummonSnapshot> summons)
        {
            if (playerId == 0) return 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int count = 0;
            foreach (SummonSnapshot summon in summons)
                if (IsActive(playerId, summon) && seen.Add(summon.Id)) count++;
            return count;
        }

        internal static bool IsActive(long playerId, SummonSnapshot summon)
        {
            return playerId != 0 && summon != null && summon.Exists && summon.Loaded && !summon.Unsummoning &&
                summon.Summoner == playerId && summon.Health > 0f && !string.IsNullOrEmpty(summon.Id) &&
                // Match Tameable.UpdateSummon's threshold, without deleting anything.
                !(summon.HasFollowTarget && summon.UnsummonDistance > 0f && summon.FollowDistance > summon.UnsummonDistance);
        }

        internal static bool Visible(bool enabled, bool showBuff, bool localPlayerAlive, int count)
        {
            return enabled && showBuff && localPlayerAlive && count > 0;
        }
    }
}
