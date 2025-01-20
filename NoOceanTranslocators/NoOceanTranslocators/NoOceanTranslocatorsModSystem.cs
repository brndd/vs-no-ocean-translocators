using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Metadata;
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

        if (!Harmony.HasAnyPatches(Mod.Info.ModID))
        {
            _harmony = new Harmony(Mod.Info.ModID);
            _harmony.PatchAll();
        }
        
        api.ChatCommands.Create("oceandetect")
            .WithDescription("debug ocean test")
            .RequiresPrivilege(Privilege.chat)
            .RequiresPlayer()
            .WithArgs(api.ChatCommands.Parsers.WorldPosition("target chunk position"))
            .HandleWith((args) =>
            {
                var byEntity = args.Caller.Entity;
                var byPlayer = args.Caller.Player;

                var pos = (args[0] as Vec3d).AsBlockPos;
                const int chunksize = GlobalConstants.ChunkSize;
                Vec2i chunkPos = new((int)pos.X / chunksize, (int)pos.Z / chunksize);

                ChunkPeekOptions peekOptions = new ChunkPeekOptions()
                {
                    OnGenerated = (chunks) =>
                    {
                        int seaLevel = api.World.SeaLevel - 1;
                        int chunkY = seaLevel / chunksize;
                        int localY = seaLevel % chunksize;
                        var chunk = chunks[chunkPos][chunkY];
                        int saltwaterBlockId = api.World.GetBlock(new AssetLocation("saltwater-still-7")).BlockId;
                        var get_index = (int localX, int localZ) => (localY * chunksize + localZ) * chunksize + localX;
                        var blockTl = chunk.Data.GetBlockId(get_index(0, 0), BlockLayersAccess.Fluid);
                        var blockTr = chunk.Data.GetBlockId(get_index(chunksize - 1, 0), BlockLayersAccess.Fluid);
                        var blockBl = chunk.Data.GetBlockId(get_index(0, chunksize - 1), BlockLayersAccess.Fluid);
                        var blockBr = chunk.Data.GetBlockId(get_index(0, chunksize - 1), BlockLayersAccess.Fluid);
                        
                        api.BroadcastMessageToAllGroups($"Top left: {blockTl}, top right: {blockTr}, bottom left: {blockBl}, bottom right: {blockBr}", EnumChatType.AllGroups);
                    },
                    UntilPass = EnumWorldGenPass.Terrain,
                    ChunkGenParams = null
                };
                
                api.WorldManager.PeekChunkColumn(chunkPos.X, chunkPos.Y, peekOptions);
                
                // IMapChunk mapChunk = api.World.BlockAccessor.GetMapChunk(chunkPos);
                // BlockPos chunkTl = new(chunkPos.X * chunksize, api.World.SeaLevel, chunkPos.Y * chunksize, pos.Dimension);
                // BlockPos chunkTr = new(chunkTl.X + chunksize - 1, api.World.SeaLevel, chunkTl.Z, pos.Dimension);
                // BlockPos chunkBl = new(chunkTl.X, api.World.SeaLevel - 1, chunkTl.Z + chunksize - 1, pos.Dimension);
                // BlockPos chunkBr = new(chunkTl.X + chunksize - 1, api.World.SeaLevel + 1, chunkTl.Z + chunksize - 1, pos.Dimension);
                //
                // int saltwaterBlockId = api.World.GetBlock(new AssetLocation("saltwater-still-7")).BlockId;
                //
                // Block blockTl = api.World.BlockAccessor.GetBlock(chunkTl, BlockLayersAccess.Fluid);
                // Block blockTr = api.World.BlockAccessor.GetBlock(chunkTr, BlockLayersAccess.Fluid);
                // Block blockBl = api.World.BlockAccessor.GetBlock(chunkBl, BlockLayersAccess.Fluid);
                // Block blockBr = api.World.BlockAccessor.GetBlock(chunkBr, BlockLayersAccess.Fluid);
                // api.BroadcastMessageToAllGroups($"Top left (y={chunkTl.Y}): {blockTl.Code.Path}", EnumChatType.AllGroups);
                // api.BroadcastMessageToAllGroups($"Top right (y={chunkTr.Y}): {blockTr.Code.Path}", EnumChatType.AllGroups);
                // api.BroadcastMessageToAllGroups($"Bottom left (y={chunkBl.Y}): {blockBl.Code.Path}", EnumChatType.AllGroups);
                // api.BroadcastMessageToAllGroups($"Bottom right (y={chunkBr.Y}): {blockBr.Code.Path}", EnumChatType.AllGroups);
                
                
                return TextCommandResult.Success();
            });
    }

    public override void Dispose()
    {
        _harmony?.UnpatchAll(Mod.Info.ModID);
    }
    
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(BlockEntityStaticTranslocator), "OnServerGameTick")]
    public static bool OnServerGameTickPrefix(ref BlockEntityStaticTranslocator __instance, bool ___canTeleport, float dt)
    {
        MethodInfo testForExitPoint = __instance.GetType()
            .GetMethod("TestForExitPoint", BindingFlags.NonPublic | BindingFlags.Instance);
        if (testForExitPoint == null)
        {
            Api.Logger.Error("NoOceanTranslocators: Could not resolve TestForExitPoint, falling back to original method.");
            return true; //fall back to original
        }
        
        MethodInfo handleTeleportingServer = __instance.GetType()
            .GetMethod("HandleTeleportingServer", BindingFlags.NonPublic | BindingFlags.Instance);
        if (handleTeleportingServer == null)
        {
            Api.Logger.Error("NoOceanTranslocators: Could not resolve HandleTeleportingServer, falling back to original method.");
            return true; //fall back to original
        }
        
        if (__instance.findNextChunk)
        {
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
                    const int chunksize = GlobalConstants.ChunkSize;
                    int seaLevel = Api.World.SeaLevel - 1;
                    int chunkY = seaLevel / chunksize;
                    int localY = seaLevel % chunksize;
                    var chunk = chunks[new Vec2i(chunkX, chunkZ)][chunkY];
                    int saltwaterBlockId = Api.World.GetBlock(new AssetLocation("saltwater-still-7")).BlockId;
                    var getIndex = (int localX, int localZ) => (localY * chunksize + localZ) * chunksize + localX;
                    var blockTl = chunk.Data.GetBlockId(getIndex(0, 0), BlockLayersAccess.Fluid);
                    var blockTr = chunk.Data.GetBlockId(getIndex(chunksize - 1, 0), BlockLayersAccess.Fluid);
                    var blockBl = chunk.Data.GetBlockId(getIndex(0, chunksize - 1), BlockLayersAccess.Fluid);
                    var blockBr = chunk.Data.GetBlockId(getIndex(0, chunksize - 1), BlockLayersAccess.Fluid);
                    var blockC = chunk.Data.GetBlockId(getIndex(chunksize / 2, chunksize / 2), BlockLayersAccess.Fluid);

                    //If all the corners and the center are liquid, look for a different chunk.
                    if (blockTl == saltwaterBlockId && blockTr == saltwaterBlockId && blockBl == saltwaterBlockId && blockBr == saltwaterBlockId && blockC == saltwaterBlockId)
                    {
                        Api.Logger.Debug("NoOceanTranslocators: Rolled a chunk with all liquid corners and center. Rerolling.");
                        instance.findNextChunk = true;
                        return;
                    }
                    
                    //This chunk looks valid, so proceed to next pass.
                    Api.Logger.Debug("NoOceanTranslocators: Chunk looks un-oceanic, proceeding.");
                    
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
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(BlockEntityStaticTranslocator), "HasExitPoint", new Type[] { typeof(BlockPos) })]
    public static bool CheckExitPoint(ref BlockPos __result, BlockPos nearpos)
    {
        Api.BroadcastMessageToAllGroups($"Old function is looking for exit point around {nearpos}!", EnumChatType.AllGroups);
        
        
        return true;
    }
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(BlockEntityStaticTranslocator), "HasExitPoint",
        new Type[] { typeof(Dictionary<Vec2i, IServerChunk[]>), typeof(int), typeof(int) })]
    public static bool CheckExitPoint(ref BlockPos __result, Dictionary<Vec2i, IServerChunk[]> columnsByChunkCoordinate,
        int centerCx, int centerCz)
    {
        Api.BroadcastMessageToAllGroups($"New function is looking for exit point around {centerCx}, {centerCz}!", EnumChatType.AllGroups);
    
        var chunk = columnsByChunkCoordinate[new Vec2i(centerCx, centerCz)][0];
        
        int saltwaterBlockId = Api.World.GetBlock(new AssetLocation("saltwater-still-7")).BlockId;
        const int chunkSize = GlobalConstants.ChunkSize;
        int seaLevel = Api.World.SeaLevel - 1;
        BlockPos chunkTl = new(centerCx * chunkSize, seaLevel, centerCz * chunkSize);
        BlockPos chunkTr = new(chunkTl.X + chunkSize - 1, seaLevel, chunkTl.Z);
        BlockPos chunkBl = new(chunkTl.X, seaLevel, chunkTl.Z + chunkSize - 1);
        BlockPos chunkBr = new(chunkTl.X + chunkSize - 1, seaLevel, chunkTl.Z + chunkSize - 1);
        
        //Block blockTl = chunk.getlocalbl
        
        return true;
    }
    
    [HarmonyPostfix]
    [HarmonyPatch(typeof(BlockEntityStaticTranslocator), MethodType.Constructor)]
    public static void ConstructorPostfix(ref BlockEntityStaticTranslocator __instance)
    {
        __instance.MinTeleporterRangeInBlocks = 10000;
        __instance.MaxTeleporterRangeInBlocks = 20000;
    }

}