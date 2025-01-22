using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace NoOceanTranslocators;

[HarmonyPatch]
public class NoOceanTranslocatorsModSystem : ModSystem
{
    public static ICoreServerAPI Api;
    private Harmony _harmony;
    
    public override bool ShouldLoad(EnumAppSide side)
    {
        return side == EnumAppSide.Server;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        Api = api;

        string cfgFilename = "NoOceanTranslocators.json";
        try
        {
            NoOceanTranslocatorsConfig cfg;
            if ((cfg = api.LoadModConfig<NoOceanTranslocatorsConfig>(cfgFilename)) == null)
            {
                api.StoreModConfig(NoOceanTranslocatorsConfig.Loaded, cfgFilename);
            }
            else
            {
                NoOceanTranslocatorsConfig.Loaded = cfg;
            }
        }
        catch
        {
            api.StoreModConfig(NoOceanTranslocatorsConfig.Loaded, cfgFilename);
        }
        
        if (!Harmony.HasAnyPatches(Mod.Info.ModID))
        {
            _harmony = new Harmony(Mod.Info.ModID);
            _harmony.PatchAll();
        }
    }

    public override void Dispose()
    {
        _harmony?.UnpatchAll(Mod.Info.ModID);
    }
    
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(BlockEntityStaticTranslocator), "OnServerGameTick")]
    public static bool OnServerGameTickPrefix(ref BlockEntityStaticTranslocator __instance, bool ___canTeleport, float dt)
    {
        if (__instance.findNextChunk)
        {
            MethodInfo testForExitPoint = typeof(BlockEntityStaticTranslocator).GetMethod("TestForExitPoint", BindingFlags.NonPublic | BindingFlags.Instance);
            if (testForExitPoint == null)
            {
                Api.Logger.Error("NoOceanTranslocators: Could not resolve TestForExitPoint, falling back to original method.");
                return true; //fall back to original
            }
            
            __instance.findNextChunk = false;
    
            int addrange = __instance.MaxTeleporterRangeInBlocks - __instance.MinTeleporterRangeInBlocks;
    
            int dx = (int)(__instance.MinTeleporterRangeInBlocks + Api.World.Rand.NextDouble() * addrange) * (2 * Api.World.Rand.Next(2) - 1);
            int dz = (int)(__instance.MinTeleporterRangeInBlocks + Api.World.Rand.NextDouble() * addrange) * (2 * Api.World.Rand.Next(2) - 1);
    
            int chunkX = (__instance.Pos.X + dx) / GlobalConstants.ChunkSize;
            int chunkZ = (__instance.Pos.Z + dz) / GlobalConstants.ChunkSize;
                
            if (!Api.World.BlockAccessor.IsValidPos(__instance.Pos.X + dx, 1, __instance.Pos.Z + dz))
            {
                __instance.findNextChunk = true;
                return false;
            }
            
            var instance = __instance;
            
            ChunkPeekOptions opts = new ChunkPeekOptions()
            {
                OnGenerated = (chunks) =>
                {
                    int chunkSize = GlobalConstants.ChunkSize;
                    int regionChunkSize = Api.WorldManager.RegionSize / chunkSize;
                    var chunk = chunks[new Vec2i(chunkX, chunkZ)][0];
                    var oceanMap = chunk.MapChunk.MapRegion.OceanMap;
                    if (oceanMap == null)
                    {
                        Api.Logger.Error("NoOceanTranslocators: OceanMap is null or empty. This should not happen. Rerolling.");
                        instance.findNextChunk = true;
                        return;
                    }
                    
                    int regionX = chunkX % regionChunkSize;
                    int regionY = chunkZ % regionChunkSize;
                    float oFac = (float)oceanMap.InnerSize / regionChunkSize;
                    int oceanTl = oceanMap.GetUnpaddedInt((int)(regionX * oFac), (int)(regionY * oFac));
                    int oceanTr = oceanMap.GetUnpaddedInt((int)(regionX * oFac + oFac), (int)(regionY * oFac));
                    int oceanBl = oceanMap.GetUnpaddedInt((int)(regionX * oFac), (int)(regionY * oFac + oFac));
                    int oceanBr = oceanMap.GetUnpaddedInt((int)(regionX * oFac + oFac), (int)(regionY * oFac + oFac));

                    //If all the corners are liquid, look for a different chunk.
                    var maxOcean = NoOceanTranslocatorsConfig.Loaded.oceanThreshold;
                    if (oceanTl >= maxOcean && oceanTr >= maxOcean && oceanBl >= maxOcean && oceanBr >= maxOcean)
                    {
                        var acceptChance = NoOceanTranslocatorsConfig.Loaded.oceanAcceptChance;
                        if (acceptChance >= 0.0f)
                        {
                            var rand = Api.World.Rand.NextDouble();
                            if (acceptChance < rand)
                            {
                                var maxRangeIncrease = NoOceanTranslocatorsConfig.Loaded.failMaxRangeIncrease;
                                var minRangeIncrease = NoOceanTranslocatorsConfig.Loaded.failMinRangeIncrease;
                                if (maxRangeIncrease > 0 || minRangeIncrease > 0)
                                {
                                    var newMax = instance.MaxTeleporterRangeInBlocks + maxRangeIncrease;
                                    var newMin = instance.MinTeleporterRangeInBlocks + minRangeIncrease;
                                    newMin = Math.Min(newMin, newMax);
                                    instance.MaxTeleporterRangeInBlocks = newMax;
                                    instance.MinTeleporterRangeInBlocks = newMin;
                                    Api.Logger.VerboseDebug("NoOceanTranslocators: Rolled a chunk that looks to be in the ocean. Increasing search ranges to {0}-{1} and trying again.", newMin, newMax);
                                }
                                else
                                {
                                    Api.Logger.VerboseDebug("NoOceanTranslocators: Rolled a chunk that looks to be in the ocean. Trying again.");
                                }
                                instance.findNextChunk = true;
                                return;
                            }
                        }
                        Api.Logger.VerboseDebug("NoOceanTranslocators: Chunk looks to be in the ocean, but accepting anyway due to oceanAcceptChance.");
                    }
                    else
                    {
                        Api.Logger.VerboseDebug("NoOceanTranslocators: Chunk looks un-oceanic, proceeding.");    
                    }
                    
                    TreeAttribute genParams = new TreeAttribute();
                    TreeAttribute subtree;
                    genParams["structureChanceModifier"] = subtree = new TreeAttribute();
                    subtree.SetFloat("gates", 10);

                    genParams["structureMaxCount"] = subtree = new TreeAttribute();
                    subtree.SetInt("gates", 1);
                    
                    ChunkPeekOptions opts = new ChunkPeekOptions()
                    {
                        OnGenerated = (chunks) => testForExitPoint.Invoke(instance, new object[] {chunks, chunkX, chunkZ}),
                        UntilPass = EnumWorldGenPass.TerrainFeatures,
                        ChunkGenParams = genParams
                    };
                    Api.WorldManager.PeekChunkColumn(chunkX, chunkZ, opts);
                },
                UntilPass = EnumWorldGenPass.Terrain,
                ChunkGenParams = null
            };
            Api.WorldManager.PeekChunkColumn(chunkX, chunkZ, opts);
        }
    
        if (___canTeleport && __instance.Activated)
        {
            MethodInfo handleTeleportingServer = typeof(BlockEntityStaticTranslocator).GetMethod("HandleTeleportingServer", BindingFlags.NonPublic | BindingFlags.Instance);
            if (handleTeleportingServer == null)
            {
                Api.Logger.Error("NoOceanTranslocators: Could not resolve HandleTeleportingServer, falling back to original method.");
                return true; //fall back to original
            }
            try
            {
                handleTeleportingServer.Invoke(__instance, new object[] { dt });
            }
            catch (Exception e)
            {
                Api.Logger.Warning("Exception when ticking Static Translocator at {0}", __instance.Pos);
                Api.Logger.Error(e);
            }
        }
        
        return false; //override original
    }
}