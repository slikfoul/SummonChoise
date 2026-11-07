using System;
using System.Collections.Generic;

namespace SkeletonChoice
{
    public enum SummonMode { Random, Warrior, Archer, ShieldWarrior }
    internal enum EquipmentKind { Other, Bow, Sword, Shield, OtherWeapon }

    internal sealed class EquipmentItem
    {
        internal readonly EquipmentKind Kind;
        internal readonly int Slot;
        internal EquipmentItem(EquipmentKind kind, int slot = 0) { Kind = kind; Slot = slot; }
    }

    internal sealed class ChanceItem
    {
        internal EquipmentItem Item;
        internal float Chance;
    }

    internal sealed class EquipmentTable
    {
        internal EquipmentItem[] Defaults = new EquipmentItem[0];
        internal EquipmentItem[] Shields = new EquipmentItem[0];
        internal EquipmentItem[] Weapons = new EquipmentItem[0];
        internal EquipmentItem[] Armor = new EquipmentItem[0];
        internal EquipmentItem[][] Sets = new EquipmentItem[0][];
        internal ChanceItem[] Items = new ChanceItem[0];
    }

    internal interface ISeedRandom
    {
        void Initialize(int seed);
        int Index(int count);
        float Value();
    }

    internal struct EquipmentResult
    {
        internal int Bows, Swords, Shields, OtherWeapons;
        internal void Add(EquipmentItem item)
        {
            if (item == null) return;
            switch (item.Kind)
            {
                case EquipmentKind.Bow: Bows++; break;
                case EquipmentKind.Sword: Swords++; break;
                case EquipmentKind.Shield: Shields++; break;
                case EquipmentKind.OtherWeapon: OtherWeapons++; break;
            }
        }
        internal bool Matches(SummonMode mode)
        {
            switch (mode)
            {
                case SummonMode.Random: return true;
                case SummonMode.Archer: return Bows > 0 && Swords == 0 && OtherWeapons == 0 && Shields == 0;
                case SummonMode.Warrior: return Swords > 0 && Bows == 0 && OtherWeapons == 0 && Shields == 0;
                case SummonMode.ShieldWarrior: return Swords > 0 && Bows == 0 && OtherWeapons == 0 && Shields > 0;
                default: return false;
            }
        }
    }

    internal static class SelectionPolicy
    {
        // Evaluate the same draws, in the same order, as Humanoid.GiveDefaultItems.
        // All clients can therefore use the game's normal equipment generation.
        internal static EquipmentResult Evaluate(EquipmentTable table, ISeedRandom random)
        {
            EquipmentResult result = default(EquipmentResult);
            foreach (EquipmentItem item in table.Defaults) result.Add(item);
            AddRandom(table.Shields, random, ref result);
            AddRandom(table.Weapons, random, ref result);
            AddRandom(table.Armor, random, ref result);
            if (table.Sets.Length > 0)
                foreach (EquipmentItem item in table.Sets[random.Index(table.Sets.Length)]) result.Add(item);
            HashSet<int> filled = new HashSet<int>();
            foreach (ChanceItem item in table.Items)
            {
                // Valheim currently tests value > chance, not value < chance.
                if (item.Item != null && random.Value() > item.Chance && filled.Add(item.Item.Slot))
                    result.Add(item.Item);
            }
            return result;
        }

        private static void AddRandom(EquipmentItem[] items, ISeedRandom random, ref EquipmentResult result)
        {
            if (items.Length > 0) result.Add(items[random.Index(items.Length)]);
        }

        internal static bool TryChoose(EquipmentTable table, SummonMode mode, int originalSeed,
            ISeedRandom random, out int selectedSeed, int attempts = 4096)
        {
            selectedSeed = originalSeed;
            if (mode == SummonMode.Random) return true;
            for (int i = 0; i < attempts; i++)
            {
                int candidate = unchecked(originalSeed + i * (int)0x9E3779B9);
                if (candidate == 0) continue; // Awake treats zero as an uninitialized seed.
                random.Initialize(candidate);
                if (!Evaluate(table, random).Matches(mode)) continue;
                selectedSeed = candidate;
                return true;
            }
            return false;
        }
    }

}
