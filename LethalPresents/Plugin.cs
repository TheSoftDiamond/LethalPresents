using BepInEx;
using BepInEx.Logging;
using BrutalCompanyMinus;
using Dissonance;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Unity.Netcode;
using UnityEngine;

namespace LethalPresents
{
    [BepInPlugin(GUID, NAME, VERSION)]

    //Add a soft dependency on BCMER mod
    //[BepInDependency("SoftDiamond.BrutalCompanyMinusExtraReborn", BepInDependency.DependencyFlags.SoftDependency)]

    public class LethalPresentsPlugin : BaseUnityPlugin
    {
        private const string GUID = "LethalPresents";
        private const string NAME = "LethalPresents";
        private const string VERSION = "2.0.2";
        public static ManualLogSource mls;

        private static int spawnChance = 5;
        private static string[] disabledEnemies = new string[0];
        private static bool CloneWorkaround = false;
        private static bool IsAllowlist = false;
        private static bool ShouldSpawnMines = false;
        private static bool ShouldSpawnTurrets = false;
        private static bool increasedLogging = false;
        private static bool ShouldSpawnSpikeTrap = false;

        private static string MoonsToIgnore = "";
        private static bool MoonListIsAllowlist = false;
        private static bool FateMode = false;

        private static bool AllowInsideSpawnOutside = false;
        private static bool AllowOutsideSpawnInside = false;

        private static bool isHost => RoundManager.Instance.NetworkManager.IsHost;
        private static SelectableLevel currentLevel => RoundManager.Instance.currentLevel;

        internal static T GetPrivateField<T>(object instance, string fieldName)
        {
            const BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo field = instance.GetType().GetField(fieldName, bindFlags);
            return (T)field.GetValue(instance);
        }

        private void Awake()
        {
            // Plugin startup logic
            mls = base.Logger;
            mls.LogInfo($"Plugin {NAME} is loaded!");

            loadConfig();

            On.RoundManager.AdvanceHourAndSpawnNewBatchOfEnemies += updateCurrentLevelInfo;
            On.GiftBoxItem.OpenGiftBoxServerRpc += spawnRandomEntity;
        }

        private void TryLog(object obj)
        {
            try
            {
                mls.LogInfo(obj);
            }
            catch (Exception) { }
        }

        private void updateCurrentLevelInfo(On.RoundManager.orig_AdvanceHourAndSpawnNewBatchOfEnemies orig, RoundManager self)
        {
            orig(self);
            On.RoundManager.AdvanceHourAndSpawnNewBatchOfEnemies -= updateCurrentLevelInfo; //show once and remove

            foreach (SelectableLevel level in StartOfRound.Instance.levels)
            {
                try
                {
                    mls.LogInfo($"Moon: {level.PlanetName} ({level.name})");
                    mls.LogInfo("List of spawnable enemies (inside):");
                    level.Enemies.ForEach(e => TryLog(e.enemyType.name));
                    mls.LogInfo("List of spawnable enemies (outside):");
                    level.OutsideEnemies.ForEach(e => TryLog(e.enemyType.name));
                    level.DaytimeEnemies.ForEach(e => TryLog(e.enemyType.name));
                }
                catch (Exception)
                {
                    continue;
                }
            }
        }

        private void loadConfig()
        {
            spawnChance = Config.Bind<int>("General", "SpawnChance", 5, "Chance of spawning an enemy when opening a present [0-100]").Value;
            disabledEnemies = Config.Bind<string>("General", "EnemyBlocklist", "", "Enemy blocklist separated by , and without whitespaces").Value.Split(",");
            IsAllowlist = Config.Bind<bool>("General", "IsAllowlist", false, "Turns blocklist into allowlist, blocklist must contain at least one inside and one outside enemy, use at your own risk").Value;
            ShouldSpawnMines = Config.Bind<bool>("General", "ShouldSpawnMines", true, "Add mines to the spawn pool").Value;
            ShouldSpawnTurrets = Config.Bind<bool>("General", "ShouldSpawnTurrets", true, "Add turrets to the spawn pool").Value;
            ShouldSpawnSpikeTrap = Config.Bind<bool>("General", "ShouldSpawnSpikeTrap", true, "Add spike roof traps to the spawn pool").Value;

            MoonsToIgnore = Config.Bind<string>("General", "MoonsToIgnore", "", "List of moons to ignore present spawning events on, separated by comma. You can specify the moon with and without its numbers too").Value;
            increasedLogging = Config.Bind<bool>("Extra", "IncreasedLogging", true, "Enables increased logging to help with debugging").Value;
            MoonListIsAllowlist = Config.Bind<bool>("General", "MoonListIsAllowlist", false, "Turns moon ignore list into allowlist").Value;
            FateMode = Config.Bind<bool>("Extra", "FateMode", false, "If enabled, the spawn chance will be unpredictable and will vary.").Value;

            CloneWorkaround = Config.Bind<bool>("Extra", "CloneWorkaround", false, "Workadound against some mods (if you find such mods - please complain to their authors about it) replacing level.Enemies entries with cloned enemies without updating their names.").Value;
            AllowInsideSpawnOutside = Config.Bind<bool>("Extra", "AllowInsideSpawnOutside", true, "Allow spawning inside enemies when outside the building. CAN CAUSE LAG WITHOUT PROPER AI MOD").Value;
            AllowOutsideSpawnInside = Config.Bind<bool>("Extra", "AllowOutsideSpawnInside", true, "Allow spawning outside enemies when inside the building. CAN CAUSE LAG WITHOUT PROPER AI MOD").Value;

            if (IsAllowlist)
            {
                mls.LogInfo("Only following enemies can spawn from the gift:");
            }
            else
            {
                mls.LogInfo("Following enemies wont be spawned from the gift:");
            }
            foreach(string entry in disabledEnemies)
            {
                mls.LogInfo(entry);
            }
        }


        private void spawnRandomEntity(On.GiftBoxItem.orig_OpenGiftBoxServerRpc orig, GiftBoxItem self)
        {
            NetworkManager networkManager = self.NetworkManager;
            
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                orig(self);
                return;
            }
            int exec_stage = GetPrivateField<int>(self, "__rpc_exec_stage");
            mls.LogDebug("IsServer:" + networkManager.IsServer + " IsHost:" + networkManager.IsHost + " __rpc_exec_stage:" + exec_stage);

            if (exec_stage != 1 || !isHost)
            {
                orig(self);
                return;
            }
            if ((IsIgnoredMoon(currentLevel.PlanetName) && !MoonListIsAllowlist) || (!IsIgnoredMoon(currentLevel.PlanetName) && MoonListIsAllowlist))
            {
                orig(self);
                return;
            }
            int fortune = UnityEngine.Random.Range(1, 100);
            mls.LogDebug("Player's fortune 1:" + fortune);

            if (FateMode)
            {
                spawnChance = UnityEngine.Random.Range(0, 101);
                if (increasedLogging)
                {
                    mls.LogDebug("Fate mode active, new spawn chance: " + spawnChance);
                }
            }
            if (fortune >= spawnChance)
            {
                orig(self);
                return;
            }
            chooseAndSpawnEnemy(self.isInFactory, self.transform.position, self.previousPlayerHeldBy.transform.position);


            orig(self);
        }

        static void chooseAndSpawnEnemy(bool inside, Vector3 pos, Vector3 player_pos)
        {
            if (increasedLogging)
            {
                mls.LogDebug($"Player pos {player_pos} moon: {currentLevel.PlanetName} ({currentLevel.name}), IsAllowList: {IsAllowlist}");
            }

            List<SpawnableEnemyWithRarity> InsideEnemies = currentLevel.Enemies.Where(e =>
            {
                var name = e.enemyType.name;
                if (CloneWorkaround) name = name.Replace("(Clone)", "").Trim();
                if (disabledEnemies.Contains(name)) //if enemy is in the list
                {
                    mls.LogDebug($"{name} [{e.enemyType.name}] is in the list");
                    return IsAllowlist;     //if its in allowlist, we can spawn that enemy, otherwise, we cant
                }
                else                        //if enemy isnt in the list
                {
                    mls.LogDebug($"{name} [{e.enemyType.name}] is NOT in the list");
                    return !IsAllowlist;    //if its not in blacklist, we can spawn it, otherwise, we cant
                }
            }).ToList();

            List<SpawnableEnemyWithRarity> OutsideEnemies = currentLevel.OutsideEnemies.Concat(currentLevel.DaytimeEnemies).Where(e =>
            {
                var name = e.enemyType.name;
                if (CloneWorkaround) name = name.Replace("(Clone)", "").Trim();
                if (disabledEnemies.Contains(name))
                {
                    mls.LogDebug($"{name} [{e.enemyType.name}] is in the list");
                    return IsAllowlist;
                }
                else
                {
                    mls.LogDebug($"{name} [{e.enemyType.name}] is NOT in the list");
                    return !IsAllowlist;
                }
            }).ToList();


            int fortune = UnityEngine.Random.Range(1, 3/*3*/ + (OutsideEnemies.Count + InsideEnemies.Count) / 3/*3*/); //keep the mine/turrent % equal to the regular monster pool
            mls.LogDebug("Choosing what to spawn; fortune 2: " + fortune + "; OutsideEnemiesCount: " + OutsideEnemies.Count + "; InsideEnemiesCount: " + InsideEnemies.Count);
            if ((fortune == 1) && (!ShouldSpawnTurrets)) fortune++;
            if ((fortune == 2) && (!ShouldSpawnMines)) fortune++;
            if ((fortune == 3) && (!ShouldSpawnSpikeTrap)) fortune++;
            if (increasedLogging)
            {
                foreach (var obj in currentLevel.spawnableMapObjects)
                {
                    mls.LogDebug("SpawnableMapObject: " + obj.prefabToSpawn.name);
                }
            }

            switch (fortune)
            {
                case 1: // turret
                    foreach (SpawnableMapObject obj in currentLevel.spawnableMapObjects)
                    {
                        if (obj.prefabToSpawn.GetComponentInChildren<Turret>() == null) continue;
                        pos -= Vector3.up * 1.8f;
                        var turret = Instantiate<GameObject>(obj.prefabToSpawn, pos, Quaternion.identity);
                        turret.transform.position = pos;
                        turret.transform.forward = (player_pos - pos).normalized;// new Vector3(1, 0, 0);
                        turret.GetComponent<NetworkObject>().Spawn(true);
                        mls.LogInfo("Tried spawning a turret at " + pos);
                        break;
                    }
                    break;
                case 2: //mine
                    foreach (SpawnableMapObject obj in currentLevel.spawnableMapObjects)
                    {
                        if (obj.prefabToSpawn.GetComponentInChildren<Landmine>() == null) continue;
                        pos -= Vector3.up * 1.8f;
                        var mine = Instantiate<GameObject>(obj.prefabToSpawn, pos, Quaternion.identity);
                        mine.transform.position = pos;
                        mine.transform.forward = new Vector3(1, 0, 0);
                        mine.GetComponent<NetworkObject>().Spawn(true);
                        mls.LogInfo("Tried spawning a mine at " + pos);
                        break;
                    }
                    break;
                case 3: //SpikeRoofTrap
                    foreach (SpawnableMapObject obj in currentLevel.spawnableMapObjects)
                    {
                        if (obj.prefabToSpawn.GetComponentInChildren<SpikeRoofTrap>() == null) continue;
                        pos -= Vector3.up * 1.8f;
                        var trap = Instantiate<GameObject>(obj.prefabToSpawn, pos, Quaternion.identity);
                        trap.transform.position = pos;
                        trap.transform.forward = (player_pos - pos).normalized;
                        trap.GetComponent<NetworkObject>().Spawn(true);
                        mls.LogInfo("Tried spawning a spike trap at " + pos);
                        break;
                    }
                    break;
                default: //enemy

                    SpawnableEnemyWithRarity enemy;
                    if (inside) //inside
                    {
                        if (AllowOutsideSpawnInside)
                        {
                            InsideEnemies.AddRange(OutsideEnemies);
                        }

                        if (InsideEnemies.Count < 1)
                        {
                            mls.LogInfo("Cant spawn enemy - no other enemies present to copy from");
                            return;
                        }

                        enemy = InsideEnemies[UnityEngine.Random.Range(0, InsideEnemies.Count - 1)];
                    }
                    else  //outside + ship
                    {
                        if (AllowInsideSpawnOutside)
                        {
                            OutsideEnemies.AddRange(InsideEnemies);
                        }

                        if (OutsideEnemies.Count < 1)
                        {
                            mls.LogInfo("Cant spawn enemy - no other enemies present to copy from");
                            return;
                        }

                        enemy = OutsideEnemies[UnityEngine.Random.Range(0, OutsideEnemies.Count - 1)];
                    }

                    pos += Vector3.up * 0.25f;
                    if (increasedLogging)
                    {
                        mls.LogInfo("Spawning " + enemy.enemyType.enemyName + " at " + pos);
                    }
                    SpawnEnemy(enemy, pos, 0);
                    break;
            }
        }
        private static void SpawnEnemy(SpawnableEnemyWithRarity enemy, Vector3 pos, float rot)
        {
            RoundManager.Instance.SpawnEnemyGameObject(pos, rot, -1, enemy.enemyType);
        }

        internal static bool IsIgnoredMoon(string moonName)
        {
            string moonsToIgnore = MoonsToIgnore;

            string[] ignoredMoons = string.IsNullOrEmpty(moonsToIgnore)
                ? new string[0]
                : moonsToIgnore.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(moon => moon.Trim())
                              .ToArray();

            bool skipEventActivation = false;

            foreach (string moon in ignoredMoons)
            {
                string moonNameNoNumbers = Regex.Replace(moonName, @"\d", string.Empty).Trim();
                string ignoredMoonNoNumbers = Regex.Replace(moon, @"\d", string.Empty).Trim();
                if (moonName == moon || moonNameNoNumbers == ignoredMoonNoNumbers)
                {
                    skipEventActivation = true;
                    mls.LogInfo("Moon is on list of moons to ignore events. Skipping Events");
                    break;
                }
            }

            return skipEventActivation;
        }

    }
}