using System;
using System.Collections.Generic;
using System.Linq;
using RIMAPI.Core;
using RIMAPI.Helpers;
using RIMAPI.Models;
using RimWorld;
using UnityEngine;
using Verse;

namespace RIMAPI.Services
{
    public class BuilderService : IBuilderService
    {
        private const int MaxBlueprintBatchSize = 100;

        public ApiResult<BlueprintDto> CopyArea(CopyAreaRequestDto request)
        {
            try
            {
                var map = MapHelper.GetMapByID(request.MapId);
                if (map == null) return ApiResult<BlueprintDto>.Fail($"Map {request.MapId} not found.");

                // Normalize Coordinates (ensure min/max are correct regardless of A/B order)
                int minX = Mathf.Min(request.PointA.X, request.PointB.X);
                int minZ = Mathf.Min(request.PointA.Z, request.PointB.Z);
                int maxX = Mathf.Max(request.PointA.X, request.PointB.X);
                int maxZ = Mathf.Max(request.PointA.Z, request.PointB.Z);

                var blueprint = new BlueprintDto
                {
                    Width = (maxX - minX) + 1,
                    Height = (maxZ - minZ) + 1
                };

                // Track added things to avoid duplicates for multi-tile buildings (like Geothermal Generators)
                HashSet<Thing> addedThings = new HashSet<Thing>();

                CellRect rect = new CellRect(minX, minZ, blueprint.Width, blueprint.Height);

                // Iterate every cell
                foreach (IntVec3 cell in rect)
                {
                    if (!cell.InBounds(map)) continue;

                    // 1. Save Terrain (Floor)
                    TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
                    if (terrain != null && terrain.Removable) // Only copy constructed floors, not soil/sand
                    {
                        blueprint.Floors.Add(new SavedTerrainDto
                        {
                            DefName = terrain.defName,
                            RelX = cell.x - minX,
                            RelZ = cell.z - minZ
                        });
                    }

                    // 2. Save Buildings
                    List<Thing> things = cell.GetThingList(map);
                    foreach (var thing in things)
                    {
                        // We only want Buildings, created by players, that we haven't added yet
                        if (thing.def.category == ThingCategory.Building &&
                            thing.def.saveCompressible == false && // Skip filth/motes
                            !addedThings.Contains(thing))
                        {
                            // Important: For multi-tile buildings, only add them if their "InteractionCell" or "Position" is inside or near our rect.
                            // To be safe, we just check if it's the first time we see it.
                            addedThings.Add(thing);

                            blueprint.Buildings.Add(new SavedBuildingDto
                            {
                                DefName = thing.def.defName,
                                StuffDefName = thing.Stuff?.defName, // Material (WoodLog, Steel, etc)
                                RelX = thing.Position.x - minX, // Save relative to anchor
                                RelZ = thing.Position.z - minZ,
                                Rotation = thing.Rotation.AsInt
                            });
                        }
                    }
                }

                return ApiResult<BlueprintDto>.Ok(blueprint);
            }
            catch (Exception ex)
            {
                LogApi.Error($"Copy Error: {ex}");
                return ApiResult<BlueprintDto>.Fail(ex.Message);
            }
        }

        public ApiResult PasteArea(PasteAreaRequestDto request)
        {
            try
            {
                if (request.Blueprint == null) return ApiResult.Fail("Blueprint is null");

                var map = MapHelper.GetMapByID(request.MapId);
                if (map == null) return ApiResult.Fail($"Map {request.MapId} not found.");

                int anchorX = request.Position.X;
                int anchorZ = request.Position.Z;

                // 1. Paste Floors
                foreach (var floorDto in request.Blueprint.Floors)
                {
                    IntVec3 pos = new IntVec3(anchorX + floorDto.RelX, 0, anchorZ + floorDto.RelZ);
                    if (pos.InBounds(map))
                    {
                        var terrainDef = DefDatabase<TerrainDef>.GetNamedSilentFail(floorDto.DefName);
                        if (terrainDef != null)
                        {
                            map.terrainGrid.SetTerrain(pos, terrainDef);
                        }
                    }
                }

                // 2. Paste Buildings
                foreach (var buildDto in request.Blueprint.Buildings)
                {
                    IntVec3 pos = new IntVec3(anchorX + buildDto.RelX, 0, anchorZ + buildDto.RelZ);

                    if (!pos.InBounds(map)) continue;

                    // Resolve Definitions
                    ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(buildDto.DefName);
                    if (thingDef == null) continue;

                    ThingDef stuffDef = null;
                    if (!string.IsNullOrEmpty(buildDto.StuffDefName))
                    {
                        stuffDef = DefDatabase<ThingDef>.GetNamedSilentFail(buildDto.StuffDefName);
                    }

                    // Optional: Clear obstacles in the spot before spawning
                    if (request.ClearObstacles)
                    {
                        var obstacles = pos.GetThingList(map).ToList(); // Copy list
                        foreach (var obs in obstacles)
                        {
                            // Destroy items/buildings in the way. Don't destroy pawns.
                            if (obs.def.category == ThingCategory.Building || obs.def.category == ThingCategory.Item || obs.def.category == ThingCategory.Plant)
                            {
                                obs.Destroy();
                            }
                        }
                    }

                    // Create & Spawn
                    Thing thing = ThingMaker.MakeThing(thingDef, stuffDef);
                    thing.Rotation = new Rot4(buildDto.Rotation);

                    // Force the faction to be the player's
                    if (thing.def.CanHaveFaction)
                    {
                        thing.SetFaction(Faction.OfPlayer);
                    }

                    GenSpawn.Spawn(thing, pos, map, thing.Rotation);
                }

                return ApiResult.Ok();
            }
            catch (Exception ex)
            {
                LogApi.Error($"Paste Error: {ex}");
                return ApiResult.Fail(ex.Message);
            }
        }

        public ApiResult PlaceBlueprints(PasteAreaRequestDto request)
        {
            try
            {
                if (request.Blueprint == null) return ApiResult.Fail("Blueprint is null");

                var map = MapHelper.GetMapByID(request.MapId);
                if (map == null) return ApiResult.Fail($"Map {request.MapId} not found.");

                int anchorX = request.Position.X;
                int anchorZ = request.Position.Z;
                int count = 0;

                // 1. Place Floor Blueprints
                foreach (var floorDto in request.Blueprint.Floors)
                {
                    IntVec3 pos = new IntVec3(anchorX + floorDto.RelX, 0, anchorZ + floorDto.RelZ);
                    if (pos.InBounds(map))
                    {
                        TerrainDef terrainDef = DefDatabase<TerrainDef>.GetNamedSilentFail(floorDto.DefName);
                        if (terrainDef != null)
                        {
                            // PlaceBlueprintForBuild works for TerrainDefs too
                            GenConstruct.PlaceBlueprintForBuild(terrainDef, pos, map, Rot4.North, Faction.OfPlayer, null);
                            count++;
                        }
                    }
                }

                // 2. Place Building Blueprints
                foreach (var buildDto in request.Blueprint.Buildings)
                {
                    IntVec3 pos = new IntVec3(anchorX + buildDto.RelX, 0, anchorZ + buildDto.RelZ);

                    if (!pos.InBounds(map)) continue;

                    // Resolve Definitions
                    ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(buildDto.DefName);
                    if (thingDef == null) continue;

                    ThingDef stuffDef = null;
                    if (!string.IsNullOrEmpty(buildDto.StuffDefName))
                    {
                        stuffDef = DefDatabase<ThingDef>.GetNamedSilentFail(buildDto.StuffDefName);
                    }

                    // Create Blueprint
                    // Note: GenConstruct handles checking if it can be placed, checking affordance, etc.
                    GenConstruct.PlaceBlueprintForBuild(
                        thingDef,
                        pos,
                        map,
                        new Rot4(buildDto.Rotation),
                        Faction.OfPlayer,
                        stuffDef
                    );
                    count++;
                }

                return ApiResult.Ok();
            }
            catch (Exception ex)
            {
                LogApi.Error($"Blueprint Error: {ex}");
                return ApiResult.Fail(ex.Message);
            }
        }

        public ApiResult<BlueprintValidateResultDto> ValidateBlueprint(BlueprintValidateRequestDto request)
        {
            try
            {
                BlueprintTarget target;
                string error;
                if (!TryResolveBlueprintTarget(request, out target, out error))
                {
                    return ApiResult<BlueprintValidateResultDto>.Fail(error);
                }

                BlueprintValidateResultDto result = BuildValidationResult(target);
                return ApiResult<BlueprintValidateResultDto>.Ok(result);
            }
            catch (Exception ex)
            {
                LogApi.Error($"Blueprint validate error: {ex}");
                return ApiResult<BlueprintValidateResultDto>.Fail(ex.Message);
            }
        }

        public ApiResult<BlueprintPlaceResultDto> PlaceBlueprint(BlueprintPlaceRequestDto request)
        {
            try
            {
                BlueprintTarget target;
                string error;
                if (!TryResolveBlueprintTarget(request, out target, out error))
                {
                    return ApiResult<BlueprintPlaceResultDto>.Fail(error);
                }

                BlueprintValidateResultDto validation = BuildValidationResult(target);
                ExistingBuildMatch existing = FindExistingBuild(target);
                if (existing.IsPending || existing.IsBuilt)
                {
                    return ApiResult<BlueprintPlaceResultDto>.Ok(new BlueprintPlaceResultDto
                    {
                        Status = "already_present",
                        Placed = false,
                        ThingId = existing.ThingId,
                        Reason = existing.IsBuilt ? "already built" : "already pending",
                        Validate = validation
                    });
                }

                if (!validation.CanPlace)
                {
                    return ApiResult<BlueprintPlaceResultDto>.Ok(new BlueprintPlaceResultDto
                    {
                        Status = "rejected",
                        Placed = false,
                        Reason = validation.Reason,
                        Validate = validation
                    });
                }

                Thing placed = GenConstruct.PlaceBlueprintForBuild(
                    target.Def,
                    target.Center,
                    target.Map,
                    target.Rotation,
                    Faction.OfPlayer,
                    target.Stuff);

                if (placed == null)
                {
                    return ApiResult<BlueprintPlaceResultDto>.Ok(new BlueprintPlaceResultDto
                    {
                        Status = "rejected",
                        Placed = false,
                        Reason = "RimWorld did not create a blueprint.",
                        Validate = validation
                    });
                }

                if (request.Allowed.HasValue && !request.Allowed.Value)
                {
                    placed.SetForbidden(true, false);
                }

                return ApiResult<BlueprintPlaceResultDto>.Ok(new BlueprintPlaceResultDto
                {
                    Status = "placed",
                    Placed = true,
                    ThingId = placed.thingIDNumber,
                    Reason = string.Empty,
                    Validate = validation
                });
            }
            catch (Exception ex)
            {
                LogApi.Error($"Blueprint place error: {ex}");
                return ApiResult<BlueprintPlaceResultDto>.Fail(ex.Message);
            }
        }

        public ApiResult<BlueprintBatchResponseDto> SetBlueprintAllowedState(BlueprintAllowedStateRequestDto request)
        {
            try
            {
                Map map;
                List<Thing> targets;
                BlueprintBatchResponseDto response;
                ApiResult<BlueprintBatchResponseDto> validation = ValidatePendingBuildBatch(
                    request,
                    out map,
                    out targets,
                    out response);
                if (validation != null)
                {
                    return validation;
                }

                bool forbidden = !request.Allowed;
                foreach (Thing target in targets)
                {
                    response.Matched++;
                    bool currentForbidden = target.IsForbidden(Faction.OfPlayer);
                    if (currentForbidden == forbidden)
                    {
                        response.AlreadyInState++;
                        continue;
                    }

                    target.SetForbidden(forbidden, false);
                    response.Changed++;
                }

                return ApiResult<BlueprintBatchResponseDto>.Ok(response);
            }
            catch (Exception ex)
            {
                LogApi.Error($"Blueprint allowed-state error: {ex}");
                return ApiResult<BlueprintBatchResponseDto>.Fail(ex.Message);
            }
        }

        public ApiResult<BlueprintBatchResponseDto> CancelBlueprints(BlueprintThingIdsRequestDto request)
        {
            try
            {
                Map map;
                List<Thing> targets;
                BlueprintBatchResponseDto response;
                ApiResult<BlueprintBatchResponseDto> validation = ValidatePendingBuildBatch(
                    request,
                    out map,
                    out targets,
                    out response);
                if (validation != null)
                {
                    return validation;
                }

                foreach (Thing target in targets)
                {
                    response.Matched++;
                    if (target.Destroyed)
                    {
                        response.AlreadyGone++;
                        continue;
                    }

                    target.Destroy(DestroyMode.Cancel);
                    response.Cancelled++;
                }

                return ApiResult<BlueprintBatchResponseDto>.Ok(response);
            }
            catch (Exception ex)
            {
                LogApi.Error($"Blueprint cancel error: {ex}");
                return ApiResult<BlueprintBatchResponseDto>.Fail(ex.Message);
            }
        }

        public static ApiResult<List<PendingBuildDto>> GetPendingBuilds(int mapId)
        {
            try
            {
                Map map = MapHelper.GetMapByID(mapId);
                if (map == null)
                {
                    return ApiResult<List<PendingBuildDto>>.Fail($"Map {mapId} not found.");
                }

                return ApiResult<List<PendingBuildDto>>.Ok(GetPendingBuilds(map));
            }
            catch (Exception ex)
            {
                LogApi.Error($"Blueprint read error: {ex}");
                return ApiResult<List<PendingBuildDto>>.Fail(ex.Message);
            }
        }

        public static ApiResult<List<ConstructionBacklogGroupDto>> GetConstructionBacklog(int mapId)
        {
            try
            {
                Map map = MapHelper.GetMapByID(mapId);
                if (map == null)
                {
                    return ApiResult<List<ConstructionBacklogGroupDto>>.Fail($"Map {mapId} not found.");
                }

                List<PendingBuildDto> pendingBuilds = GetPendingBuilds(map);
                Dictionary<string, int> availableMaterials = CountAvailableMaterials(map);
                List<ConstructionBacklogGroupDto> groups = pendingBuilds
                    .GroupBy(build => new
                    {
                        build.Kind,
                        build.DefName,
                        build.StuffDefName,
                        build.Allowed
                    })
                    .Select(group => BuildBacklogGroup(group.ToList(), availableMaterials))
                    .OrderBy(group => group.Allowed)
                    .ThenBy(group => group.Kind)
                    .ThenBy(group => group.DefName)
                    .ThenBy(group => group.StuffDefName)
                    .ToList();

                return ApiResult<List<ConstructionBacklogGroupDto>>.Ok(groups);
            }
            catch (Exception ex)
            {
                LogApi.Error($"Construction backlog error: {ex}");
                return ApiResult<List<ConstructionBacklogGroupDto>>.Fail(ex.Message);
            }
        }

        private static bool TryResolveBlueprintTarget(
            BlueprintValidateRequestDto request,
            out BlueprintTarget target,
            out string error)
        {
            target = null;
            error = null;

            if (request == null)
            {
                error = "validation failed: request body is required";
                return false;
            }

            if (string.IsNullOrEmpty(request.DefName))
            {
                error = "validation failed: def_name is required";
                return false;
            }

            if (request.Cell == null)
            {
                error = "validation failed: cell is required";
                return false;
            }

            if (request.Rotation < 0 || request.Rotation > 3)
            {
                error = "validation failed: rotation must be 0, 1, 2, or 3";
                return false;
            }

            Map map = MapHelper.GetMapByID(request.MapId);
            if (map == null)
            {
                error = $"Map {request.MapId} not found.";
                return false;
            }

            ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(request.DefName);
            TerrainDef terrainDef = null;
            BuildableDef buildableDef = thingDef;
            string defType = "thing";
            if (buildableDef == null)
            {
                terrainDef = DefDatabase<TerrainDef>.GetNamedSilentFail(request.DefName);
                buildableDef = terrainDef;
                defType = "terrain";
            }

            if (buildableDef == null)
            {
                error = $"Buildable def not found: {request.DefName}";
                return false;
            }

            ThingDef stuff = null;
            if (!string.IsNullOrEmpty(request.StuffDefName))
            {
                if (terrainDef != null)
                {
                    error = "validation failed: stuff_def_name is only valid for thing blueprints";
                    return false;
                }

                stuff = DefDatabase<ThingDef>.GetNamedSilentFail(request.StuffDefName);
                if (stuff == null)
                {
                    error = $"Stuff def not found: {request.StuffDefName}";
                    return false;
                }
            }

            if (thingDef != null && !ValidateThingStuff(thingDef, stuff, out error))
            {
                return false;
            }

            IntVec3 center = new IntVec3(request.Cell.X, 0, request.Cell.Z);
            if (!center.InBounds(map))
            {
                error = "validation failed: cell is out of bounds";
                return false;
            }

            target = new BlueprintTarget
            {
                Map = map,
                Def = buildableDef,
                DefType = defType,
                Stuff = stuff,
                Center = center,
                Rotation = new Rot4(request.Rotation)
            };
            return true;
        }

        private static bool ValidateThingStuff(ThingDef thingDef, ThingDef stuff, out string error)
        {
            error = null;

            if (!thingDef.MadeFromStuff)
            {
                if (stuff != null)
                {
                    error = $"validation failed: def_name '{thingDef.defName}' does not accept stuff_def_name";
                    return false;
                }

                return true;
            }

            if (stuff == null)
            {
                error = $"validation failed: stuff_def_name is required for def_name '{thingDef.defName}'";
                return false;
            }

            if (!GenStuff.AllowedStuffsFor(thingDef).Contains(stuff))
            {
                error = $"validation failed: stuff_def_name '{stuff.defName}' is not valid for def_name '{thingDef.defName}'";
                return false;
            }

            return true;
        }

        private static BlueprintValidateResultDto BuildValidationResult(BlueprintTarget target)
        {
            AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(
                target.Def,
                target.Center,
                target.Rotation,
                target.Map,
                false,
                null,
                null,
                target.Stuff);

            ExistingBuildMatch existing = FindExistingBuild(target);
            return new BlueprintValidateResultDto
            {
                CanPlace = report.Accepted,
                Reason = report.Accepted ? string.Empty : report.Reason,
                DefType = target.DefType,
                OccupiesCells = OccupiedCells(target.Center, target.Rotation, target.Def.Size),
                Cost = CostFor(target.Def, target.Stuff),
                WorkToBuild = WorkToBuild(target.Def, target.Stuff),
                AlreadyBlueprinted = existing.IsPending,
                AlreadyBuilt = existing.IsBuilt
            };
        }

        private static ExistingBuildMatch FindExistingBuild(BlueprintTarget target)
        {
            List<PendingBuildDto> pendingBuilds = GetPendingBuilds(target.Map);
            PendingBuildDto pending = pendingBuilds.FirstOrDefault(build =>
                string.Equals(build.DefName, target.Def.defName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(build.StuffDefName ?? string.Empty, target.Stuff?.defName ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                build.Rotation == target.Rotation.AsInt &&
                build.Cell != null &&
                build.Cell.X == target.Center.x &&
                build.Cell.Z == target.Center.z);

            if (pending != null)
            {
                return new ExistingBuildMatch
                {
                    IsPending = true,
                    ThingId = pending.Id
                };
            }

            if (target.Def is TerrainDef terrainDef)
            {
                TerrainDef currentTerrain = target.Map.terrainGrid.TerrainAt(target.Center);
                if (currentTerrain == terrainDef)
                {
                    return new ExistingBuildMatch
                    {
                        IsBuilt = true
                    };
                }
            }
            else
            {
                List<Thing> things = target.Center.GetThingList(target.Map);
                foreach (Thing thing in things)
                {
                    if (thing.def == target.Def &&
                        string.Equals(thing.Stuff?.defName ?? string.Empty, target.Stuff?.defName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        return new ExistingBuildMatch
                        {
                            IsBuilt = true,
                            ThingId = thing.thingIDNumber
                        };
                    }
                }
            }

            return new ExistingBuildMatch();
        }

        private static ApiResult<BlueprintBatchResponseDto> ValidatePendingBuildBatch(
            BlueprintThingIdsRequestDto request,
            out Map map,
            out List<Thing> targets,
            out BlueprintBatchResponseDto response)
        {
            map = null;
            targets = new List<Thing>();
            response = new BlueprintBatchResponseDto();

            if (request == null)
            {
                return ApiResult<BlueprintBatchResponseDto>.Fail("validation failed: request body is required");
            }

            if (request.ThingIds == null || request.ThingIds.Count == 0)
            {
                return ApiResult<BlueprintBatchResponseDto>.Fail("validation failed: thing_ids must contain at least one id");
            }

            if (request.ThingIds.Count > MaxBlueprintBatchSize)
            {
                return ApiResult<BlueprintBatchResponseDto>.Fail($"validation failed: thing_ids cannot contain more than {MaxBlueprintBatchSize} ids");
            }

            map = MapHelper.GetMapByID(request.MapId);
            if (map == null)
            {
                return ApiResult<BlueprintBatchResponseDto>.Fail($"Map {request.MapId} not found.");
            }

            response.Requested = request.ThingIds.Count;
            Dictionary<int, Thing> requestedMapThings = map
                .listerThings
                .AllThings
                .Where(thing => !thing.Destroyed)
                .GroupBy(thing => thing.thingIDNumber)
                .ToDictionary(group => group.Key, group => group.First());

            HashSet<int> otherMapIds = new HashSet<int>(
                Find.Maps
                    .Where(candidateMap => candidateMap.uniqueID != request.MapId)
                    .SelectMany(candidateMap => candidateMap.listerThings.AllThings)
                    .Where(thing => !thing.Destroyed)
                    .Select(thing => thing.thingIDNumber));

            HashSet<int> requestedIds = new HashSet<int>();
            foreach (string idText in request.ThingIds)
            {
                int id;
                if (!int.TryParse(idText, out id))
                {
                    return ApiResult<BlueprintBatchResponseDto>.Fail($"validation failed: thing id '{idText}' is not an integer");
                }

                requestedIds.Add(id);
            }

            foreach (int id in requestedIds)
            {
                Thing thing;
                if (requestedMapThings.TryGetValue(id, out thing))
                {
                    if (IsPendingBuildThing(thing))
                    {
                        targets.Add(thing);
                        response.Matched++;
                    }
                    else
                    {
                        response.NonPendingBuildTargets++;
                    }
                }
                else if (otherMapIds.Contains(id))
                {
                    response.NonMapTargets++;
                }
                else
                {
                    response.Missing++;
                }
            }

            if (response.Missing > 0 ||
                response.NonMapTargets > 0 ||
                response.NonPendingBuildTargets > 0)
            {
                return new ApiResult<BlueprintBatchResponseDto>
                {
                    Success = false,
                    Data = response,
                    Errors =
                    {
                        "validation failed: all thing_ids must refer to pending blueprints or frames on the requested map"
                    }
                };
            }

            response.Matched = 0;
            return null;
        }

        private static List<PendingBuildDto> GetPendingBuilds(Map map)
        {
            List<PendingBuildDto> result = new List<PendingBuildDto>();
            HashSet<int> seen = new HashSet<int>();

            AddPendingBuildsFromGroup(map, ThingRequestGroup.Blueprint, result, seen);
            AddPendingBuildsFromGroup(map, ThingRequestGroup.BuildingFrame, result, seen);

            return result
                .OrderBy(build => build.Kind)
                .ThenBy(build => build.DefName)
                .ThenBy(build => build.Id)
                .ToList();
        }

        private static void AddPendingBuildsFromGroup(
            Map map,
            ThingRequestGroup group,
            List<PendingBuildDto> result,
            HashSet<int> seen)
        {
            List<Thing> things = map.listerThings.ThingsInGroup(group);
            foreach (Thing thing in things)
            {
                if (!seen.Add(thing.thingIDNumber))
                {
                    continue;
                }

                PendingBuildDto dto = PendingBuildToDto(thing);
                if (dto != null)
                {
                    result.Add(dto);
                }
            }
        }

        private static PendingBuildDto PendingBuildToDto(Thing thing)
        {
            Blueprint_Build blueprint = thing as Blueprint_Build;
            Frame frame = thing as Frame;
            if (blueprint == null && frame == null)
            {
                return null;
            }

            BuildableDef buildableDef = blueprint != null
                ? blueprint.def.entityDefToBuild
                : frame.def.entityDefToBuild;
            ThingDef stuff = blueprint != null
                ? blueprint.EntityToBuildStuff()
                : frame.Stuff;

            bool isForbidden = thing.IsForbidden(Faction.OfPlayer);
            return new PendingBuildDto
            {
                Id = thing.thingIDNumber,
                Kind = blueprint != null ? "blueprint" : "frame",
                DefName = buildableDef?.defName,
                DefType = buildableDef is TerrainDef ? "terrain" : "thing",
                StuffDefName = stuff?.defName,
                Cell = new BlueprintCellDto
                {
                    X = thing.Position.x,
                    Z = thing.Position.z
                },
                Rotation = thing.Rotation.AsInt,
                Allowed = !isForbidden,
                IsForbidden = isForbidden,
                WorkLeft = frame != null ? frame.WorkLeft : WorkToBuild(buildableDef, stuff),
                Hp = frame != null ? (int?)frame.HitPoints : null,
                Cost = CostFor(buildableDef, stuff)
            };
        }

        private static ConstructionBacklogGroupDto BuildBacklogGroup(
            List<PendingBuildDto> builds,
            Dictionary<string, int> availableMaterials)
        {
            PendingBuildDto first = builds.First();
            List<BlueprintCostDto> totalCost = builds
                .SelectMany(build => build.Cost)
                .GroupBy(cost => cost.DefName)
                .Select(group => new BlueprintCostDto
                {
                    DefName = group.Key,
                    Count = group.Sum(cost => cost.Count)
                })
                .OrderBy(cost => cost.DefName)
                .ToList();

            List<BlueprintMaterialAvailabilityDto> materialRows = totalCost
                .Select(cost =>
                {
                    int available = availableMaterials.ContainsKey(cost.DefName)
                        ? availableMaterials[cost.DefName]
                        : 0;
                    return new BlueprintMaterialAvailabilityDto
                    {
                        DefName = cost.DefName,
                        Required = cost.Count,
                        Available = available,
                        Missing = Math.Max(0, cost.Count - available)
                    };
                })
                .ToList();

            int disallowedCount = builds.Count(build => !build.Allowed);
            bool hasMaterialGap = materialRows.Any(row => row.Missing > 0);
            return new ConstructionBacklogGroupDto
            {
                Kind = first.Kind,
                DefName = first.DefName,
                StuffDefName = first.StuffDefName,
                Allowed = first.Allowed,
                Count = builds.Count,
                ThingIds = builds.Select(build => build.Id).ToList(),
                SampleCells = builds
                    .Where(build => build.Cell != null)
                    .Take(5)
                    .Select(build => build.Cell)
                    .ToList(),
                TotalWorkLeft = builds.Sum(build => build.WorkLeft ?? 0f),
                Cost = totalCost,
                MaterialsAvailable = materialRows.Where(row => row.Available > 0).ToList(),
                MaterialsMissing = materialRows.Where(row => row.Missing > 0).ToList(),
                BlockedCount = hasMaterialGap || disallowedCount > 0 ? builds.Count : 0,
                DisallowedCount = disallowedCount
            };
        }

        private static Dictionary<string, int> CountAvailableMaterials(Map map)
        {
            return map
                .listerThings
                .ThingsInGroup(ThingRequestGroup.HaulableEver)
                .Where(thing => !thing.Destroyed && !thing.IsForbidden(Faction.OfPlayer))
                .GroupBy(thing => thing.def.defName)
                .ToDictionary(group => group.Key, group => group.Sum(thing => thing.stackCount));
        }

        private static bool IsPendingBuildThing(Thing thing)
        {
            return thing is Blueprint_Build || thing is Frame;
        }

        private static List<BlueprintCellDto> OccupiedCells(IntVec3 center, Rot4 rotation, IntVec2 size)
        {
            CellRect rect = GenAdj.OccupiedRect(center, rotation, size);
            List<BlueprintCellDto> cells = new List<BlueprintCellDto>();
            foreach (IntVec3 cell in rect)
            {
                cells.Add(new BlueprintCellDto
                {
                    X = cell.x,
                    Z = cell.z
                });
            }

            return cells;
        }

        private static List<BlueprintCostDto> CostFor(BuildableDef def, ThingDef stuff)
        {
            List<BlueprintCostDto> cost = new List<BlueprintCostDto>();
            if (def == null)
            {
                return cost;
            }

            List<ThingDefCountClass> costList = def.CostListAdjusted(stuff);
            foreach (ThingDefCountClass entry in costList)
            {
                if (entry.thingDef == null || entry.count <= 0)
                {
                    continue;
                }

                cost.Add(new BlueprintCostDto
                {
                    DefName = entry.thingDef.defName,
                    Count = entry.count
                });
            }

            return cost;
        }

        private static float WorkToBuild(BuildableDef def, ThingDef stuff)
        {
            if (def == null)
            {
                return 0f;
            }

            return def.GetStatValueAbstract(StatDefOf.WorkToBuild, stuff);
        }

        private class BlueprintTarget
        {
            public Map Map { get; set; }
            public BuildableDef Def { get; set; }
            public string DefType { get; set; }
            public ThingDef Stuff { get; set; }
            public IntVec3 Center { get; set; }
            public Rot4 Rotation { get; set; }
        }

        private class ExistingBuildMatch
        {
            public bool IsPending { get; set; }
            public bool IsBuilt { get; set; }
            public int? ThingId { get; set; }
        }
    }
}
