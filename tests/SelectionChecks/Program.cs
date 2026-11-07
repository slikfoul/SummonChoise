using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SkeletonChoice;

internal static class Program
{
    private static int checks;
    private static readonly EquipmentItem Bow = new EquipmentItem(EquipmentKind.Bow, 4);
    private static readonly EquipmentItem Sword = new EquipmentItem(EquipmentKind.Sword, 3);
    private static readonly EquipmentItem Shield = new EquipmentItem(EquipmentKind.Shield, 5);
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }
    private static void Main(string[] args)
    {
        SelectionChecks();
        DrawOrderChecks();
        CounterChecks();
        SpiritChecks();

        if (args.Length != 2) throw new ArgumentException("Pass installed game assembly and built mod DLL.");
        MetadataChecks(args[0], args[1]);
        Console.WriteLine("Passed " + checks + " checks: seed selection, summon ownership/count lifecycle, display conditions and installed-game hook metadata. Unity execution and multiplayer remain untested.");
    }

    private static void SelectionChecks()
    {
        var tables = new[]
        {
            new EquipmentTable { Shields = new[] { null, Shield }, Weapons = new[] { Bow, Sword } },
            new EquipmentTable { Sets = new[] { new[] { Bow }, new[] { Sword }, new[] { Sword, Shield } } },
            new EquipmentTable
            {
                Weapons = new[] { Bow, Sword },
                Items = new[] { new ChanceItem { Item = Shield, Chance = .5f } },
                Armor = new[] { new EquipmentItem(EquipmentKind.Other), null }
            }
        };
        foreach (var table in tables)
            foreach (SummonMode mode in Enum.GetValues(typeof(SummonMode)))
                for (int initial = -50; initial <= 50; initial++)
                {
                    var rng = new SeedRandom();
                    Check(SelectionPolicy.TryChoose(table, mode, initial, rng, out int chosen), "Available equipment not found: " + mode);
                    if (mode == SummonMode.Random)
                    {
                        Check(chosen == initial && rng.Initializations == 0, "Random mode changed seed or consumed random numbers.");
                        continue;
                    }
                    Check(chosen != 0, "Selected an uninitialized seed.");
                    Check(SelectionPolicy.TryChoose(table, mode, initial, new SeedRandom(), out int repeated) && repeated == chosen, "Selection is not deterministic.");
                    rng.Initialize(chosen);
                    var actual = NativeEquipmentOracle(table, rng);
                    Check(IsRequested(actual, mode), "Native generation does not produce selected equipment.");
                }
        var unavailable = new EquipmentTable { Defaults = new[] { new EquipmentItem(EquipmentKind.OtherWeapon) }, Weapons = new[] { Bow, Sword } };
        var failing = new SeedRandom();
        Check(!SelectionPolicy.TryChoose(unavailable, SummonMode.Archer, 42, failing, out int unchanged, 10), "Unknown weapon was treated as an archer.");
        Check(unchanged == 42 && failing.Initializations == 10, "Unavailable equipment did not preserve seed and bounded search.");
        Check(!SelectionPolicy.TryChoose(new EquipmentTable { Defaults = new[] { Bow } }, SummonMode.Warrior, 12, new SeedRandom(), out _), "Missing warrior incorrectly succeeded.");
        Check(SelectionPolicy.TryChoose(new EquipmentTable { Defaults = new[] { Bow } }, SummonMode.Archer, 0, new SeedRandom(), out int nonzero) && nonzero != 0, "Zero seed was retained.");
        var shieldedArcher = new EquipmentTable { Defaults = new[] { Bow, Shield } };
        Check(!SelectionPolicy.TryChoose(shieldedArcher, SummonMode.Archer, 22, new SeedRandom(), out int keptShieldSeed, 8), "Archer mode admitted a bow with an unavoidable shield.");
        Check(keptShieldSeed == 22, "Unavailable shieldless archer changed the vanilla seed.");
        var shieldedLoadout = new EquipmentResult { Bows = 1, Shields = 1 };
        Check(!shieldedLoadout.Matches(SummonMode.Archer), "Shielded bow loadout was accepted as the requested archer.");
    }

    // Independent inventory-generation oracle based on the installed game's GiveDefaultItems.
    // System.Random is a test provider; Unity's native RNG is used by the actual mod.
    private static List<EquipmentKind> NativeEquipmentOracle(EquipmentTable table, ISeedRandom rng)
    {
        var inventory = new List<EquipmentKind>();
        Action<EquipmentItem> give = item => { if (item != null) inventory.Add(item.Kind); };
        foreach (var item in table.Defaults) give(item);
        foreach (var choices in new[] { table.Shields, table.Weapons, table.Armor })
            if (choices.Length != 0) give(choices[rng.Index(choices.Length)]);
        if (table.Sets.Length != 0)
            foreach (var item in table.Sets[rng.Index(table.Sets.Length)]) give(item);
        var occupied = new bool[64];
        foreach (var item in table.Items)
            if (item.Item != null && rng.Value() > item.Chance)
            {
                if (occupied[item.Item.Slot]) continue;
                occupied[item.Item.Slot] = true;
                give(item.Item);
            }
        return inventory;
    }

    private static bool IsRequested(List<EquipmentKind> inventory, SummonMode mode)
    {
        bool bow = inventory.Contains(EquipmentKind.Bow), sword = inventory.Contains(EquipmentKind.Sword), shield = inventory.Contains(EquipmentKind.Shield);
        if (inventory.Contains(EquipmentKind.OtherWeapon)) return false;
        return mode == SummonMode.Archer ? bow && !sword && !shield : sword && !bow && (mode == SummonMode.ShieldWarrior ? shield : !shield);
    }

    private static void DrawOrderChecks()
    {
        var table = new EquipmentTable
        {
            Shields = new[] { Shield }, Weapons = new[] { Sword }, Armor = new EquipmentItem[2],
            Sets = new[] { new EquipmentItem[0], new EquipmentItem[0], new EquipmentItem[0] },
            Items = new[]
            {
                new ChanceItem { Item = null, Chance = 0 },
                new ChanceItem { Item = Bow, Chance = .5f },
                new ChanceItem { Item = Bow, Chance = 0 },
                new ChanceItem { Item = Bow, Chance = 0 }
            }
        };
        var rng = new ScriptedRandom();
        var result = SelectionPolicy.Evaluate(table, rng);
        Check(string.Join(",", rng.Calls) == "index1,index1,index2,index3,value,value,value", "Draw sequence changed or a null prefab consumed a draw.");
        Check(result.Bows == 1 && result.Swords == 1 && result.Shields == 1, "Probability boundary or duplicate item slot changed.");
        Check(!result.Matches(SummonMode.Archer) && !result.Matches(SummonMode.ShieldWarrior), "Mixed melee/ranged inventory was accepted.");
        Check(!SelectionPolicy.TryChoose(new EquipmentTable(), (SummonMode)99, 1, new SeedRandom(), out _, 2), "Unknown selection was accepted.");
    }

    private static void CounterChecks()
    {
        var ownA = new SummonSnapshot("A", 101, true, 100);
        var ownB = new SummonSnapshot("B", 101, true, 1);
        var other = new SummonSnapshot("C", 202, true, 100);
        Check(SummonCountPolicy.Count(101, new SummonSnapshot[0]) == 0, "Empty world is not zero.");
        Check(SummonCountPolicy.Count(101, new[] { ownA }) == 1, "First own summon is not counted.");
        Check(SummonCountPolicy.Count(101, new[] { ownA, ownB, other }) == 2, "Other player's skeleton contaminated the count.");
        Check(SummonCountPolicy.Count(202, new[] { ownA, ownB, other }) == 1, "Switching player did not separate ownership.");
        Check(SummonCountPolicy.Count(101, new[] { ownA, ownA, ownB }) == 2, "Duplicate snapshots counted twice.");
        Check(SummonCountPolicy.Count(101, new[] { new SummonSnapshot("A", 101, true, 0), ownB, other }) == 1, "Death did not reduce the count.");
        Check(SummonCountPolicy.Count(101, new[] { new SummonSnapshot("A", 101, false, 100), ownB }) == 1, "Destroyed summon counted as alive.");
        Check(SummonCountPolicy.Count(101, new[] { new SummonSnapshot("A", 101, true, -1), new SummonSnapshot("B", 101, true, 0) }) == 0, "Final death did not clear count.");
        Check(SummonCountPolicy.Count(101, new[] { new SummonSnapshot("old", 0, true, 100), other }) == 0, "Legacy untagged summons were claimed.");
        Check(SummonCountPolicy.Count(0, new[] { ownA, new SummonSnapshot("old", 0, true, 100) }) == 0, "Missing player ID adopted untagged summons.");
        Check(SummonCountPolicy.Count(101, new[] { new SummonSnapshot("", 101, true, 100), null, ownA }) == 1, "Invalid ID or null snapshot affected count.");
        Check(SummonCountPolicy.Count(101, new[] { new SummonSnapshot("invalid", 101, true, float.NaN) }) == 0, "Invalid health counted as alive.");
        for (int amount = 0; amount <= 30; amount++)
        {
            var summons = new List<SummonSnapshot>();
            for (int i = 0; i < amount; i++)
            {
                summons.Add(new SummonSnapshot("mine" + i, 101, true, 1));
                summons.Add(new SummonSnapshot("theirs" + i, 202, true, 1));
            }
            Check(SummonCountPolicy.Count(101, summons) == amount, "Count does not match summon series.");
            Check(SummonCountPolicy.Visible(true, true, true, amount) == (amount > 0), "Indicator visibility does not match count.");
        }
        Check(!SummonCountPolicy.Visible(false, true, true, 2), "Disabled mod retained indicator.");
        Check(!SummonCountPolicy.Visible(true, false, true, 2), "Display toggle did not hide indicator.");
        Check(!SummonCountPolicy.Visible(true, true, false, 2), "Dead/logged-out player retained indicator.");

        // A cached live-looking ZDO remains after running out of simulation range.
        var cachedOnly = new SummonSnapshot("A", 101, true, 100, loaded: false);
        Check(SummonCountPolicy.Count(101, new[] { cachedOnly }) == 0, "Unloaded network record caused a ghost count.");
        Check(!SummonCountPolicy.Visible(true, true, true, SummonCountPolicy.Count(101, new[] { cachedOnly })), "Unloaded last summon left the icon visible.");
        Check(SummonCountPolicy.Count(101, new[] { cachedOnly, ownB }) == 1, "Unloading one summon hid another active summon.");
        Check(SummonCountPolicy.Count(101, new[] { ownA }) == 1, "A living summon did not count again after reloading.");
        var retiring = new SummonSnapshot("A", 101, true, 100, unsummoning: true);
        Check(SummonCountPolicy.Count(101, new[] { retiring }) == 0, "Unsummon RPC recipient counted during its removal animation.");
        Check(SummonCountPolicy.Count(101, new[] { retiring, ownB, other }) == 1, "Unsummoning affected unrelated summons.");
        Check(SummonCountPolicy.Count(101, new[] { new SummonSnapshot("A", 101, true, 100, hasFollowTarget: true, followDistance: 60, unsummonDistance: 60) }) == 1, "Native distance threshold must use strict greater-than.");
        Check(SummonCountPolicy.Count(101, new[] { new SummonSnapshot("A", 101, true, 100, hasFollowTarget: true, followDistance: 61, unsummonDistance: 60) }) == 0, "Summon beyond the native follow-distance threshold was counted.");
        Check(SummonCountPolicy.Count(101, new[] { new SummonSnapshot("A", 101, true, 100, hasFollowTarget: true, followDistance: 500, unsummonDistance: 0) }) == 1, "Disabled native distance limit was enforced.");
        Check(SummonCountPolicy.Count(101, new[] { new SummonSnapshot("A", 101, true, 100, hasFollowTarget: false, followDistance: 500, unsummonDistance: 60) }) == 1, "Stationary summon without a follow target was incorrectly retired by distance.");
    }

    private static void SpiritChecks()
    {
        var types = new[] { SpiritMode.Bear, SpiritMode.Moose, SpiritMode.Wolf, SpiritMode.Boar };
        for (int mask = 0; mask < 16; mask++)
        {
            var occupied = types.Where((_, i) => (mask & (1 << i)) != 0).ToDictionary(t => t, _ => 1);
            int missing = Array.FindIndex(types, t => !occupied.ContainsKey(t));
            Check(SpiritSelectionPolicy.Choose(SpiritMode.Missing, types, occupied, 1) == missing, "Quality-one staff repeated an active/reserved type or ignored the order.");
            foreach (var type in types)
                Check(SpiritSelectionPolicy.Choose(type, types, occupied, 1) == Array.IndexOf(types, type), "Fixed mode switched species because it was active.");
        }
        for (int quality = 1; quality <= 5; quality++)
        {
            var occupied = new Dictionary<SpiritMode, int>();
            for (int i = 0; i < 4 * quality; i++)
            {
                int index = SpiritSelectionPolicy.Choose(SpiritMode.Missing, types, occupied, quality);
                Check(index == i % 4, "Missing mode did not fill consecutive staff-quality rounds.");
                SpiritSelectionPolicy.Add(occupied, types[index]);
            }
            Check(SpiritSelectionPolicy.Choose(SpiritMode.Missing, types, occupied, quality) == SpiritSelectionPolicy.Unavailable, "Full roster allowed another cast.");
            occupied[SpiritMode.Wolf]--;
            Check(SpiritSelectionPolicy.Choose(SpiritMode.Missing, types, occupied, quality) == 2, "A lost wolf did not reopen its slot.");
        }
        var uneven = new Dictionary<SpiritMode, int> { [SpiritMode.Bear] = 3, [SpiritMode.Moose] = 1, [SpiritMode.Wolf] = 2, [SpiritMode.Boar] = 1 };
        var reordered = new[] { SpiritMode.Boar, SpiritMode.Wolf, SpiritMode.Moose, SpiritMode.Bear };
        Check(SpiritSelectionPolicy.Choose(SpiritMode.Missing, reordered, uneven, 3) == 2, "Uneven roster did not prioritize the least represented species in stable order.");
        Check(SpiritSelectionPolicy.Choose(SpiritMode.Random, types, null, 3) == SpiritSelectionPolicy.Vanilla, "Random mode overrode the vanilla choice.");
        var filtered = new[] { SpiritMode.Missing, SpiritMode.Moose, SpiritMode.Wolf, SpiritMode.Boar };
        Check(SpiritSelectionPolicy.Choose(SpiritMode.Missing, filtered, new Dictionary<SpiritMode, int> { [SpiritMode.Moose] = 1 }, 2) == 2, "Missing mode did not skip a capped species or prioritize an empty slot.");
        Check(SpiritSelectionPolicy.Choose(SpiritMode.Bear, filtered, null, 2) == SpiritSelectionPolicy.Unavailable, "Unavailable fixed species silently selected another one.");
    }

    private static void MetadataChecks(string gameAssembly, string modAssembly)
    {
        using (var stream = File.OpenRead(gameAssembly))
        using (var pe = new PEReader(stream))
        {
            var reader = pe.GetMetadataReader();
            var types = reader.TypeDefinitions.Select(h => reader.GetTypeDefinition(h)).ToArray();
            Func<string, TypeDefinition> get = name => types.Single(t => !t.IsNested && reader.GetString(t.Namespace) == "" && reader.GetString(t.Name) == name);
            Action<string, string> method = (type, name) => Check(get(type).GetMethods().Any(h => reader.GetString(reader.GetMethodDefinition(h).Name) == name), "Missing game method " + type + "." + name);
            method("SpawnAbility", "Setup"); method("SpawnAbility", "Spawn");
            method("Humanoid", "Awake");
            method("Humanoid", "StartAttack"); method("Character", "Awake");
            method("SpawnSystem", "GetNrOfInstances"); method("BaseAI", "Follow");
            method("SEMan", "GetHUDStatusEffects"); method("StatusEffect", "GetIconText");
            method("Hud", "UpdateStatusEffects"); method("Player", "GetPlayerID");
            method("Character", "GetAllCharacters"); method("Tameable", "RPC_UnSummon");
            method("Tameable", "UpdateSummon"); method("Tameable", "UnsummonMaxInstances");
            method("MonsterAI", "GetFollowTarget");
            Check(get("Tameable").GetFields().Any(h => reader.GetString(reader.GetFieldDefinition(h).Name) == "m_unsummonDistance"), "Missing vanilla unsummon distance field.");

            foreach (string field in new[] { "m_seed", "m_defaultItems", "m_randomShield", "m_randomWeapon", "m_randomArmor", "m_randomSets", "m_randomItems" })
                Check(get("Humanoid").GetFields().Any(h => reader.GetString(reader.GetFieldDefinition(h).Name) == field), "Missing game field " + field);
            var spawn = get("SpawnAbility");
            var iterator = spawn.GetNestedTypes().Select(h => reader.GetTypeDefinition(h)).Single(t => reader.GetString(t.Name).StartsWith("<Spawn>d__"));
            Check(iterator.GetMethods().Any(h => reader.GetString(reader.GetMethodDefinition(h).Name) == "MoveNext"), "Spawn iterator has no MoveNext.");
            Check(iterator.GetFields().Any(h => reader.GetString(reader.GetFieldDefinition(h).Name) == "<>4__this"), "Spawn iterator has no owner field.");
        }
        using (var stream = File.OpenRead(modAssembly))
        using (var pe = new PEReader(stream))
        {
            var reader = pe.GetMetadataReader();
            Check(!reader.TypeReferences.Any(h => reader.GetString(reader.GetTypeReference(h).Name).StartsWith("ValueTuple")), "Valheim plugin depends on ValueTuple.");
            Check(reader.GetAssemblyDefinition().Version == new Version(1, 0, 0, 0), "Wrong assembly version.");
            var effect = reader.TypeDefinitions.Select(h => reader.GetTypeDefinition(h)).Single(t => reader.GetString(t.Name) == "SummonedSkeletonsEffect");
            Check(effect.GetMethods().Any(h => reader.GetString(reader.GetMethodDefinition(h).Name) == "GetIconText"), "Counter effect does not supply native icon text.");
        }
    }

    private sealed class SeedRandom : ISeedRandom
    {
        private Random rng;
        internal int Initializations;
        public void Initialize(int seed) { rng = new Random(seed); Initializations++; }
        public int Index(int count) { return rng.Next(count); }
        public float Value() { return (float)rng.NextDouble(); }
    }
    private sealed class ScriptedRandom : ISeedRandom
    {
        internal readonly List<string> Calls = new List<string>();
        public void Initialize(int seed) { throw new Exception("Unexpected seed initialization."); }
        public int Index(int count) { Calls.Add("index" + count); return 0; }
        public float Value() { Calls.Add("value"); return .5f; }
    }
}
