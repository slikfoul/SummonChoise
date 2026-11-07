using System.Collections.Generic;

namespace SkeletonChoice
{
    public enum SpiritMode { Missing, Random, Bear, Moose, Wolf, Boar }

    internal static class SpiritSelectionPolicy
    {
        internal const int Vanilla = -2;
        internal const int Unavailable = -1;

        internal static int Choose(SpiritMode mode, IReadOnlyList<SpiritMode> candidates, IReadOnlyDictionary<SpiritMode, int> occupied, int capacity)
        {
            if (mode == SpiritMode.Random) return Vanilla;
            int best = Unavailable;
            int lowest = capacity;
            for (int i = 0; i < candidates.Count; i++)
            {
                SpiritMode species = candidates[i];
                if (species < SpiritMode.Bear || species > SpiritMode.Boar) continue;
                if (mode != SpiritMode.Missing)
                {
                    if (species == mode) return i;
                    continue;
                }
                int count = occupied.TryGetValue(species, out int current) ? current : 0;
                if (count < lowest || (best >= 0 && count == lowest && species < candidates[best]))
                {
                    best = i;
                    lowest = count;
                }
            }
            return best;
        }

        internal static void Add(IDictionary<SpiritMode, int> occupied, SpiritMode species)
        {
            occupied[species] = occupied.TryGetValue(species, out int current) ? current + 1 : 1;
        }
    }
}
