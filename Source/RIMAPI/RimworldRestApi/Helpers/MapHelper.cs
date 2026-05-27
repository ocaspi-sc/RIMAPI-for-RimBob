using System;
using System.Collections.Generic;
using System.Linq;
using RIMAPI.Core;
using RIMAPI.Models;
using RIMAPI.Models.Map;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RIMAPI.Helpers
{
    public static class MapHelper
    {
        private const int DefaultRegionMaxCost = 1000;
        private const int MaxPathCostBatchSize = 4096;

        private class ValidatedPathPair
        {
            public MapCellDto FromDto { get; set; }
            public MapCellDto ToDto { get; set; }
            public IntVec3 From { get; set; }
            public IntVec3 To { get; set; }
        }

        public static Thing GetThingOnMapById(int mapId, int id)
        {
            Map map = GetMapByID(mapId);
            return map.listerThings.AllThings.Where(s => s.thingIDNumber == id).FirstOrDefault();
        }

        public static Map GetMapByID(int uniqueID)
        {
            foreach (Map map in Find.Maps)
            {
                if (map.uniqueID == uniqueID)
                {
                    return map;
                }
            }
            return null;
        }

        public static List<MapDto> GetMaps()
        {
            var maps = new List<MapDto>();

            try
            {
                foreach (var map in Current.Game.Maps)
                {
                    maps.Add(
                        new MapDto
                        {
                            Id = map.uniqueID,
                            Index = map.Index,
                            Seed = map.ConstantRandSeed,
                            FactionId = map.ParentFaction.loadID.ToString(),
                            IsPlayerHome = map.IsPlayerHome,
                            IsPocketMap = map.IsPocketMap,
                            IsTempIncidentMap = map.IsTempIncidentMap,
                            Size = map.Size.ToString(),
                        }
                    );
                }

                return maps;
            }
            catch (Exception ex)
            {
                Core.LogApi.Error($"Error - {ex.Message}");
                return maps;
            }
        }

        public static MapCreaturesSummaryDto GetMapCreaturesSummary(int mapId)
        {
            try
            {
                var map = GetMapByID(mapId);
                return new MapCreaturesSummaryDto
                {
                    ColonistsCount = map.mapPawns.FreeColonistsSpawnedCount,
                    PrisonersCount = map.mapPawns.PrisonersOfColonyCount,
                    EnemiesCount = map.mapPawns.AllPawnsSpawned.Count(p =>
                        p.RaceProps.Humanlike && p.HostileTo(Faction.OfPlayer)
                    ),
                    AnimalsCount = map.mapPawns.AllPawnsSpawned.Count(p => p.RaceProps.Animal),
                    InsectoidsCount = map.mapPawns.AllPawnsSpawned.Count(p =>
                        p != null && p.Faction != null && p.Faction.def == FactionDefOf.Insect
                    ),
                    MechanoidsCount = map.mapPawns.AllPawnsSpawned.Count(p =>
                        p != null && p.RaceProps != null && p.RaceProps.IsMechanoid
                    ),
                };
            }
            catch (Exception ex)
            {
                Core.LogApi.Error($"Error - {ex}");
                Core.LogApi.Error($"Error - {ex.Message}");
                return new MapCreaturesSummaryDto();
            }
        }

        public static MapTimeDto GetDatetimeAt(int tileID)
        {
            MapTimeDto mapTimeDto = new MapTimeDto();
            try
            {
                if (Current.ProgramState != ProgramState.Playing || Find.WorldGrid == null)
                {
                    return mapTimeDto;
                }

                var vector = Find.WorldGrid.LongLatOf(GetMapTileId(Find.CurrentMap));
                mapTimeDto.Datetime = GenDate.DateFullStringWithHourAt(
                    Find.TickManager.TicksAbs,
                    vector
                );

                return mapTimeDto;
            }
            catch (Exception ex)
            {
                Core.LogApi.Error($"Error - {ex.Message}");
                return mapTimeDto;
            }
        }

        public static MapPowerInfoDto GetMapPowerInfoInternal(int mapId)
        {
            MapPowerInfoDto powerInfo = new MapPowerInfoDto();

            try
            {
                Map map = GetMapByID(mapId);

                foreach (Building building in map.listerBuildings.allBuildingsColonist)
                {
                    // Check if building is - Power Generator
                    CompPowerPlant powerPlant = building.TryGetComp<CompPowerPlant>();
                    if (powerPlant != null)
                    {
                        powerInfo.TotalPossiblePower += Mathf.RoundToInt(
                            Mathf.Abs(powerPlant.Props.PowerConsumption)
                        );
                        powerInfo.CurrentPower += Mathf.RoundToInt(powerPlant.PowerOutput);
                        powerInfo.ProducePowerBuildings.Add(building.thingIDNumber);
                        continue;
                    }

                    // Check if building is - Battery
                    CompPowerBattery powerBattery = building.TryGetComp<CompPowerBattery>();
                    if (powerBattery != null)
                    {
                        powerInfo.CurrentlyStoredPower += Mathf.RoundToInt(
                            powerBattery.StoredEnergy
                        );
                        powerInfo.TotalPowerStorage += Mathf.RoundToInt(
                            powerBattery.Props.storedEnergyMax
                        );
                        powerInfo.StorePowerBuildings.Add(building.thingIDNumber);
                    }
                }

                // Calculate power consumption
                foreach (PowerNet net in map.powerNetManager.AllNetsListForReading)
                {
                    foreach (CompPowerTrader comp in net.powerComps)
                    {
                        if (comp.Props.PowerConsumption > 0f)
                        {
                            powerInfo.TotalConsumption += Mathf.RoundToInt(
                                comp.Props.PowerConsumption
                            );
                        }
                        if (comp.PowerOn && comp.PowerOutput < 0f)
                        {
                            powerInfo.ConsumptionPowerOn += Mathf.RoundToInt(
                                Mathf.Abs(comp.PowerOutput)
                            );
                        }

                        Building building = comp.parent as Building;
                        if (building != null)
                        {
                            powerInfo.ConsumePowerBuildings.Add(building.thingIDNumber);
                        }
                    }
                }

                return powerInfo;
            }
            catch (Exception ex)
            {
                Core.LogApi.Error($"Error - {ex.Message}");
                return powerInfo;
            }
        }

        public static int GetMapTileId(Map map)
        {
            return map.Tile.tileId;
        }

        public static List<AnimalDto> GetMapAnimals(int mapId)
        {
            List<AnimalDto> animals = new List<AnimalDto>();
            try
            {
                Map map = GetMapByID(mapId);
                if (map == null)
                {
                    return animals;
                }

                animals = map
                    .mapPawns.AllPawns.Where(p => p.RaceProps?.Animal == true)
                    .Select(p => new AnimalDto
                    {
                        Id = p.thingIDNumber,
                        Name = p.LabelShortCap,
                        Def = p.def?.defName,
                        Faction = p.Faction?.ToString(),
                        Position = new PositionDto { X = p.Position.x, Y = p.Position.z },
                        Trainer = p
                            .relations?.DirectRelations.Where(r => r.def == PawnRelationDefOf.Bond)
                            .Select(r => r.otherPawn?.thingIDNumber)
                            .FirstOrDefault(),
                        Pregnant = p.health?.hediffSet?.HasHediff(HediffDefOf.Pregnant) ?? false,
                    })
                    .ToList();

                return animals;
            }
            catch (Exception ex)
            {
                Core.LogApi.Error($"Error - {ex.Message}");
                return new List<AnimalDto>();
            }
        }

        public static List<ThingDto> GetMapThings(int mapId)
        {
            List<ThingDto> things = new List<ThingDto>();
            try
            {
                Map map = GetMapByID(mapId);
                if (map == null)
                {
                    return things;
                }

                things = map
                    .listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
                    .Select(p => ResourcesHelper.ThingToDto(p))
                    .ToList();

                return things;
            }
            catch (Exception ex)
            {
                Core.LogApi.Error($"Error - {ex.Message}");
                return new List<ThingDto>();
            }
        }

        public static List<ThingDto> GetMapPlants(int mapId)
        {
            List<ThingDto> plants = new List<ThingDto>();
            try
            {
                Map map = GetMapByID(mapId);
                if (map == null)
                {
                    return plants;
                }

                // Get all plants (trees, bushes, crops, etc.)
                plants = map
                    .listerThings.ThingsInGroup(ThingRequestGroup.Plant)
                    .Select(p => ResourcesHelper.ThingToDto(p))
                    .ToList();

                return plants;
            }
            catch (Exception ex)
            {
                Core.LogApi.Error($"Error - {ex.Message}");
                return new List<ThingDto>();
            }
        }

        public static List<ZoneDto> GetMapZones(int mapId)
        {
            List<ZoneDto> zones = new List<ZoneDto>();
            try
            {
                Map map = GetMapByID(mapId);
                if (map == null)
                {
                    throw new Exception("Map with this id wasn't found");
                }

                foreach (Zone zone in map.zoneManager.AllZones)
                {
                    zones.Add(
                        new ZoneDto
                        {
                            Id = zone.ID,
                            CellsCount = zone.CellCount,
                            Label = zone.label,
                            BaseLabel = zone.BaseLabel,
                            Type = zone.GetType().Name,
                        }
                    );
                }

                return zones;
            }
            catch (Exception)
            {
                throw;
            }
        }

        public static List<ZoneDto> GetMapAreas(int mapId)
        {
            List<ZoneDto> zones = new List<ZoneDto>();
            try
            {
                Map map = GetMapByID(mapId);
                if (map == null)
                {
                    throw new Exception("Map with this id wasn't found");
                }

                foreach (Area area in map.areaManager.AllAreas)
                {
                    zones.Add(
                        new ZoneDto
                        {
                            Id = area.ID,
                            CellsCount = area.ActiveCells.Count(),
                            Label = area.Label,
                            BaseLabel = area.Label,
                            Type = area.GetType().Name,
                        }
                    );
                }

                return zones;
            }
            catch (Exception)
            {
                throw;
            }
        }

        public static List<BuildingDto> GetMapBuildings(int mapId)
        {
            List<BuildingDto> buildings = new List<BuildingDto>();
            Map map = GetMapByID(mapId);
            if (map == null)
            {
                throw new Exception("Map with this id wasn't found");
            }

            foreach (Building building in map.listerBuildings.allBuildingsColonist)
            {
                buildings.Add(
                    new BuildingDto
                    {
                        Id = building.thingIDNumber,
                        Def = building.def.defName,
                        Label = building.Label,
                        Position = new PositionDto
                        {
                            X = building.Position.x,
                            Y = building.Position.y,
                            Z = building.Position.z,
                        },
                        Rotation = building.Rotation.AsInt,
                        Size = new PositionDto
                        {
                            X = building.def.size.x,
                            Y = 0,
                            Z = building.def.size.z
                        },
                        Type = building.GetType().Name,
                    }
                );
            }

            return buildings;
        }

        public static MapRoomsDto GetRooms(Map map)
        {
            var allRooms = map.regionGrid.AllRooms;
            var mapRooms = new MapRoomsDto
            {
                Rooms = allRooms
                    .Select(s => new RoomDto
                    {
                        Id = s.ID,
                        RoleLabel = s.GetRoomRoleLabel(),
                        Temperature = s.Temperature,
                        CellsCount = s.CellCount,
                        TouchesMapEdge = s.TouchesMapEdge,
                        IsPrisonCell = s.IsPrisonCell,
                        IsDoorway = s.IsDoorway,
                        ContainedBedsIds = s.ContainedBeds.Select(b => b.thingIDNumber).ToList(),
                        OpenRoofCount = s.OpenRoofCount,
                        Impressiveness = ReadRoomStat(s, RoomStatDefOf.Impressiveness),
                        Beauty = ReadRoomStat(s, RoomStatDefOf.Beauty),
                        Cleanliness = ReadRoomStat(s, RoomStatDefOf.Cleanliness),
                        Space = ReadRoomStat(s, RoomStatDefOf.Space),
                        Wealth = ReadRoomStat(s, RoomStatDefOf.Wealth),
                    })
                    .ToList(),
            };
            return mapRooms;
        }

        private static float? ReadRoomStat(Room room, RoomStatDef stat)
        {
            try
            {
                return room.GetStat(stat);
            }
            catch (Exception ex)
            {
                Core.LogApi.Warning($"Failed to read room stat {stat?.defName}: {ex.Message}");
                return null;
            }
        }

        public static MapTerrainDto GetMapTerrain(int mapId)
        {
            var map = GetMapByID(mapId);
            if (map == null) return new MapTerrainDto();

            var terrainGrid = map.terrainGrid;
            var size = map.Size;
            int width = size.x;
            int height = size.z;

            // 1. Build Palette and Raw Index Grid
            var palette = new List<string>();
            var paletteLookup = new Dictionary<TerrainDef, int>();
            var rawIndices = new int[width * height];

            int cellIndex = 0;
            // Iterate Z then X (Standard loop order)
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    TerrainDef def = terrainGrid.TerrainAt(new IntVec3(x, 0, z));

                    if (!paletteLookup.TryGetValue(def, out int pIndex))
                    {
                        pIndex = palette.Count;
                        palette.Add(def.defName);
                        paletteLookup[def] = pIndex;
                    }

                    rawIndices[cellIndex++] = pIndex;
                }
            }

            // 2. Run-Length Encoding (RLE)
            var compressedGrid = new List<int>();
            if (rawIndices.Length > 0)
            {
                int currentVal = rawIndices[0];
                int count = 1;

                for (int i = 1; i < rawIndices.Length; i++)
                {
                    if (rawIndices[i] == currentVal)
                    {
                        count++;
                    }
                    else
                    {
                        compressedGrid.Add(count);
                        compressedGrid.Add(currentVal);
                        currentVal = rawIndices[i];
                        count = 1;
                    }
                }
                // Add final run
                compressedGrid.Add(count);
                compressedGrid.Add(currentVal);
            }

            // 3. Build Floor Palette and Grid (for constructed floors)
            var floorPalette = new List<string>();
            var floorPaletteLookup = new Dictionary<string, int>();
            var rawFloorIndices = new int[width * height];

            cellIndex = 0;
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    var cell = new IntVec3(x, 0, z);
                    var building = map.edificeGrid.InnerArray[cellIndex];

                    // Check if this is a floor (constructed floor blueprint)
                    // Floors in RimWorld are Buildings that have a graphic but no altitude (they're on the ground)
                    string floorDefName = null;
                    if (building != null && building.def.building != null)
                    {
                        // Floors typically have graphicData and no altitudeLayer set
                        // Or check if the defName contains "Floor"
                        if (building.def.graphicData != null && building.def.altitudeLayer == Verse.AltitudeLayer.Floor)
                        {
                            floorDefName = building.def.defName;
                        }
                    }

                    int fIndex = 0; // 0 = no floor (null)
                    if (floorDefName != null)
                    {
                        if (!floorPaletteLookup.TryGetValue(floorDefName, out fIndex))
                        {
                            fIndex = floorPalette.Count + 1; // +1 because 0 is reserved for null
                            floorPalette.Add(floorDefName);
                            floorPaletteLookup[floorDefName] = fIndex;
                        }
                    }

                    rawFloorIndices[cellIndex++] = fIndex;
                }
            }

            // 4. Run-Length Encoding for Floors
            var compressedFloorGrid = new List<int>();
            if (rawFloorIndices.Length > 0)
            {
                int currentVal = rawFloorIndices[0];
                int count = 1;

                for (int i = 1; i < rawFloorIndices.Length; i++)
                {
                    if (rawFloorIndices[i] == currentVal)
                    {
                        count++;
                    }
                    else
                    {
                        compressedFloorGrid.Add(count);
                        compressedFloorGrid.Add(currentVal);
                        currentVal = rawFloorIndices[i];
                        count = 1;
                    }
                }
                // Add final run
                compressedFloorGrid.Add(count);
                compressedFloorGrid.Add(currentVal);
            }

            return new MapTerrainDto
            {
                Width = width,
                Height = height,
                Palette = palette,
                Grid = compressedGrid,
                FloorPalette = floorPalette,
                FloorGrid = compressedFloorGrid
            };
        }

        public static List<ThingDto> GetMapThingsInRadius(int mapId, int centerX, int centerZ, int radius)
        {
            var results = new List<ThingDto>();
            Map map = GetMapByID(mapId);
            if (map == null) return results;

            IntVec3 center = new IntVec3(centerX, 0, centerZ);
            var processedIds = new HashSet<int>();

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map)) continue;
                List<Thing> thingsAtCell = map.thingGrid.ThingsListAt(cell);

                for (int i = 0; i < thingsAtCell.Count; i++)
                {
                    Thing t = thingsAtCell[i];
                    if (processedIds.Contains(t.thingIDNumber)) continue;

                    // Filter: Items, Buildings, Plants
                    if (t.def.category == ThingCategory.Item ||
                        t.def.category == ThingCategory.Building ||
                        t.def.category == ThingCategory.Plant)
                    {
                        // Skip invisible things
                        if (t.def.drawerType == DrawerType.None) continue;

                        // Convert to DTO
                        // Use existing helper but ensure we capture building-specifics if needed
                        var dto = ResourcesHelper.ThingToDto(t);

                        // Correction for Buildings: Size/Rotation logic in ThingToDto is generic
                        // Ensure it matches what we need

                        results.Add(dto);
                        processedIds.Add(t.thingIDNumber);
                    }
                }
            }
            return results;
        }

        public static ApiResult<MapReachResponseDto> GetMapReach(
            int mapId,
            int fromX,
            int fromZ,
            int toX,
            int toZ,
            string mode,
            string peMode
        )
        {
            Map map = GetMapByID(mapId);
            if (map == null)
            {
                return ApiResult<MapReachResponseDto>.Fail($"Map not found: {mapId}");
            }

            MapCellDto fromDto = new MapCellDto { X = fromX, Z = fromZ };
            MapCellDto toDto = new MapCellDto { X = toX, Z = toZ };
            string validationError;
            IntVec3 from;
            IntVec3 to;
            if (!TryValidateCellPair(map, fromDto, toDto, "request", out from, out to, out validationError))
            {
                return ApiResult<MapReachResponseDto>.Fail(validationError);
            }

            TraverseMode traverseMode;
            string normalizedMode;
            if (!TryParseTraverseMode(mode, out traverseMode, out normalizedMode))
            {
                return ValidationFail<MapReachResponseDto>($"unknown mode '{mode}'");
            }

            PathEndMode pathEndMode;
            string normalizedPeMode;
            if (!TryParsePathEndMode(peMode, out pathEndMode, out normalizedPeMode))
            {
                return ValidationFail<MapReachResponseDto>($"unknown peMode '{peMode}'");
            }

            TraverseParms traverseParms = BuildTraverseParms(traverseMode);
            bool canReach = map.reachability.CanReach(
                from,
                new LocalTargetInfo(to),
                pathEndMode,
                traverseParms
            );

            return ApiResult<MapReachResponseDto>.Ok(
                new MapReachResponseDto
                {
                    CanReach = canReach,
                    From = fromDto,
                    To = toDto,
                    Mode = normalizedMode,
                    PeMode = normalizedPeMode
                }
            );
        }

        public static ApiResult<MapPathCostResponseDto> GetMapPathCost(MapPathCostRequestDto request)
        {
            if (request == null)
            {
                return ValidationFail<MapPathCostResponseDto>("request body is required");
            }

            Map map = GetMapByID(request.MapId);
            if (map == null)
            {
                return ApiResult<MapPathCostResponseDto>.Fail($"Map not found: {request.MapId}");
            }

            string tier;
            if (!TryParsePathCostTier(request.Tier, out tier))
            {
                return ValidationFail<MapPathCostResponseDto>($"unknown tier '{request.Tier}'");
            }

            TraverseMode traverseMode;
            string normalizedMode;
            if (!TryParseTraverseMode(request.Mode, out traverseMode, out normalizedMode))
            {
                return ValidationFail<MapPathCostResponseDto>($"unknown mode '{request.Mode}'");
            }

            PathEndMode pathEndMode;
            string normalizedPeMode;
            if (!TryParsePathEndMode(request.PeMode, out pathEndMode, out normalizedPeMode))
            {
                return ValidationFail<MapPathCostResponseDto>($"unknown pe_mode '{request.PeMode}'");
            }

            int maxCost;
            if (!TryResolveMaxCost(request.MaxCost, out maxCost, out string maxCostError))
            {
                return ValidationFail<MapPathCostResponseDto>(maxCostError);
            }

            IntVec3 from;
            IntVec3 to;
            if (!TryValidateCellPair(map, request.From, request.To, "request", out from, out to, out string validationError))
            {
                return ApiResult<MapPathCostResponseDto>.Fail(validationError);
            }

            TraverseParms traverseParms = BuildTraverseParms(traverseMode);
            MapPathCostResultDto result = CalculatePathCost(
                map,
                request.From,
                request.To,
                from,
                to,
                tier,
                traverseParms,
                pathEndMode,
                maxCost
            );

            return ApiResult<MapPathCostResponseDto>.Ok(
                new MapPathCostResponseDto
                {
                    Reachable = result.Reachable,
                    Cost = result.Cost,
                    From = result.From,
                    To = result.To,
                    Tier = tier
                }
            );
        }

        public static ApiResult<MapPathCostBatchResponseDto> GetMapPathCostBatch(
            MapPathCostBatchRequestDto request
        )
        {
            if (request == null)
            {
                return ValidationFail<MapPathCostBatchResponseDto>("request body is required");
            }

            Map map = GetMapByID(request.MapId);
            if (map == null)
            {
                return ApiResult<MapPathCostBatchResponseDto>.Fail($"Map not found: {request.MapId}");
            }

            string tier;
            if (!TryParsePathCostTier(request.Tier, out tier))
            {
                return ValidationFail<MapPathCostBatchResponseDto>($"unknown tier '{request.Tier}'");
            }

            TraverseMode traverseMode;
            string normalizedMode;
            if (!TryParseTraverseMode(request.Mode, out traverseMode, out normalizedMode))
            {
                return ValidationFail<MapPathCostBatchResponseDto>($"unknown mode '{request.Mode}'");
            }

            PathEndMode pathEndMode;
            string normalizedPeMode;
            if (!TryParsePathEndMode(request.PeMode, out pathEndMode, out normalizedPeMode))
            {
                return ValidationFail<MapPathCostBatchResponseDto>($"unknown pe_mode '{request.PeMode}'");
            }

            int maxCost;
            if (!TryResolveMaxCost(request.MaxCost, out maxCost, out string maxCostError))
            {
                return ValidationFail<MapPathCostBatchResponseDto>(maxCostError);
            }

            if (request.Pairs == null)
            {
                return ValidationFail<MapPathCostBatchResponseDto>("pairs are required");
            }

            if (request.Pairs.Count > MaxPathCostBatchSize)
            {
                return ValidationFail<MapPathCostBatchResponseDto>(
                    $"pairs length {request.Pairs.Count} exceeds limit {MaxPathCostBatchSize}"
                );
            }

            List<ValidatedPathPair> validatedPairs = new List<ValidatedPathPair>();
            for (int i = 0; i < request.Pairs.Count; i++)
            {
                MapPathCostPairRequestDto pair = request.Pairs[i];
                if (pair == null)
                {
                    return ValidationFail<MapPathCostBatchResponseDto>($"pairs[{i}] is required");
                }

                IntVec3 from;
                IntVec3 to;
                if (!TryValidateCellPair(map, pair.From, pair.To, $"pairs[{i}]", out from, out to, out string validationError))
                {
                    return ApiResult<MapPathCostBatchResponseDto>.Fail(validationError);
                }

                validatedPairs.Add(
                    new ValidatedPathPair
                    {
                        FromDto = pair.From,
                        ToDto = pair.To,
                        From = from,
                        To = to
                    }
                );
            }

            if (tier == "astar" && validatedPairs.Count > 64)
            {
                LogApi.Warning(
                    $"[MapHelper] A* path-cost batch requested for {validatedPairs.Count} pairs; this is expensive."
                );
            }

            TraverseParms traverseParms = BuildTraverseParms(traverseMode);
            MapPathCostBatchResponseDto response = new MapPathCostBatchResponseDto();
            foreach (ValidatedPathPair pair in validatedPairs)
            {
                response.Results.Add(
                    CalculatePathCost(
                        map,
                        pair.FromDto,
                        pair.ToDto,
                        pair.From,
                        pair.To,
                        tier,
                        traverseParms,
                        pathEndMode,
                        maxCost
                    )
                );
            }

            return ApiResult<MapPathCostBatchResponseDto>.Ok(response);
        }

        private static MapPathCostResultDto CalculatePathCost(
            Map map,
            MapCellDto fromDto,
            MapCellDto toDto,
            IntVec3 from,
            IntVec3 to,
            string tier,
            TraverseParms traverseParms,
            PathEndMode pathEndMode,
            int maxCost
        )
        {
            if (tier == "astar")
            {
                return CalculateAStarPathCost(map, fromDto, toDto, from, to, traverseParms, pathEndMode);
            }

            return CalculateRegionPathCost(map, fromDto, toDto, from, to, traverseParms, maxCost);
        }

        private static MapPathCostResultDto CalculateRegionPathCost(
            Map map,
            MapCellDto fromDto,
            MapCellDto toDto,
            IntVec3 from,
            IntVec3 to,
            TraverseParms traverseParms,
            int maxCost
        )
        {
            Region startRegion = map.regionGrid.GetValidRegionAt_NoRebuild(from);
            Region endRegion = map.regionGrid.GetValidRegionAt_NoRebuild(to);
            if (startRegion == null || endRegion == null)
            {
                return UnreachablePathCost(fromDto, toDto);
            }

            if (startRegion == endRegion)
            {
                return ReachablePathCost(fromDto, toDto, 0);
            }

            if (maxCost == 0)
            {
                return UnreachablePathCost(fromDto, toDto);
            }

            bool found = false;
            int regionsTraversed = 0;
            int maxRegions = maxCost == int.MaxValue ? int.MaxValue : Math.Max(1, maxCost + 1);

            RegionTraverser.BreadthFirstTraverse(
                startRegion,
                (Region fromRegion, Region toRegion) => toRegion.Allows(traverseParms, false),
                (Region region) =>
                {
                    if (region == startRegion)
                    {
                        return false;
                    }

                    regionsTraversed++;
                    if (region == endRegion)
                    {
                        found = true;
                        return true;
                    }

                    return regionsTraversed >= maxCost;
                },
                maxRegions,
                RegionType.Set_Passable
            );

            return found
                ? ReachablePathCost(fromDto, toDto, regionsTraversed)
                : UnreachablePathCost(fromDto, toDto);
        }

        private static MapPathCostResultDto CalculateAStarPathCost(
            Map map,
            MapCellDto fromDto,
            MapCellDto toDto,
            IntVec3 from,
            IntVec3 to,
            TraverseParms traverseParms,
            PathEndMode pathEndMode
        )
        {
            PawnPath path = null;
            try
            {
                path = map.pathFinder.FindPathNow(
                    from,
                    new LocalTargetInfo(to),
                    traverseParms,
                    null,
                    pathEndMode,
                    null
                );
                if (path == null || !path.Found)
                {
                    return UnreachablePathCost(fromDto, toDto);
                }

                return ReachablePathCost(fromDto, toDto, (int)path.TotalCost);
            }
            finally
            {
                if (path != null && path != PawnPath.NotFound)
                {
                    path.Dispose();
                }
            }
        }

        private static MapPathCostResultDto ReachablePathCost(MapCellDto from, MapCellDto to, int cost)
        {
            return new MapPathCostResultDto
            {
                Reachable = true,
                Cost = cost,
                From = from,
                To = to
            };
        }

        private static MapPathCostResultDto UnreachablePathCost(MapCellDto from, MapCellDto to)
        {
            return new MapPathCostResultDto
            {
                Reachable = false,
                Cost = -1,
                From = from,
                To = to
            };
        }

        private static bool TryValidateCellPair(
            Map map,
            MapCellDto fromDto,
            MapCellDto toDto,
            string label,
            out IntVec3 from,
            out IntVec3 to,
            out string error
        )
        {
            from = IntVec3.Invalid;
            to = IntVec3.Invalid;

            if (fromDto == null)
            {
                error = $"validation: {label}.from is required";
                return false;
            }

            if (toDto == null)
            {
                error = $"validation: {label}.to is required";
                return false;
            }

            from = new IntVec3(fromDto.X, 0, fromDto.Z);
            to = new IntVec3(toDto.X, 0, toDto.Z);

            if (!from.InBounds(map))
            {
                error = $"validation: {label}.from ({fromDto.X}, {fromDto.Z}) is out of bounds";
                return false;
            }

            if (!to.InBounds(map))
            {
                error = $"validation: {label}.to ({toDto.X}, {toDto.Z}) is out of bounds";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryParsePathCostTier(string value, out string tier)
        {
            string normalized = string.IsNullOrWhiteSpace(value)
                ? "region"
                : value.Trim().ToLowerInvariant();

            switch (normalized)
            {
                case "region":
                case "astar":
                    tier = normalized;
                    return true;
                default:
                    tier = null;
                    return false;
            }
        }

        private static bool TryParseTraverseMode(
            string value,
            out TraverseMode traverseMode,
            out string normalizedMode
        )
        {
            normalizedMode = string.IsNullOrWhiteSpace(value)
                ? "pass_doors"
                : value.Trim().ToLowerInvariant();

            switch (normalizedMode)
            {
                case "pass_doors":
                    traverseMode = TraverseMode.PassDoors;
                    return true;
                case "no_pass_closed_doors":
                    traverseMode = TraverseMode.NoPassClosedDoors;
                    return true;
                case "pass_all_destroyable_things":
                    traverseMode = TraverseMode.PassAllDestroyableThings;
                    return true;
                default:
                    traverseMode = TraverseMode.PassDoors;
                    return false;
            }
        }

        private static bool TryParsePathEndMode(
            string value,
            out PathEndMode pathEndMode,
            out string normalizedPeMode
        )
        {
            normalizedPeMode = string.IsNullOrWhiteSpace(value)
                ? "on_cell"
                : value.Trim().ToLowerInvariant();

            switch (normalizedPeMode)
            {
                case "on_cell":
                    pathEndMode = PathEndMode.OnCell;
                    return true;
                case "touch":
                    pathEndMode = PathEndMode.Touch;
                    return true;
                case "closest_touch":
                    pathEndMode = PathEndMode.ClosestTouch;
                    return true;
                default:
                    pathEndMode = PathEndMode.OnCell;
                    return false;
            }
        }

        private static bool TryResolveMaxCost(int? requestedMaxCost, out int maxCost, out string error)
        {
            maxCost = requestedMaxCost ?? DefaultRegionMaxCost;
            if (maxCost < 0)
            {
                error = "max_cost cannot be negative";
                return false;
            }

            error = null;
            return true;
        }

        private static TraverseParms BuildTraverseParms(TraverseMode mode)
        {
            return TraverseParms.For(mode, Danger.Deadly, false, false, false, false, false);
        }

        private static ApiResult<T> ValidationFail<T>(string message)
        {
            return ApiResult<T>.Fail($"validation: {message}");
        }

        public static OreDataDto GetOreData(int mapId)
        {
            Map map = MapHelper.GetMapByID(mapId);
            if (map == null) return null;

            int width = map.Size.x;

            var oreData = new OreDataDto
            {
                MapWidth = width,
                Ores = new Dictionary<string, OreGroupDto>()
            };

            // Use a single loop through AllThings to populate the groups
            // This is faster than LINQ GroupBy for large datasets in Unity/RimWorld
            foreach (var thing in map.listerThings.AllThings)
            {
                if (thing.def.mineable)
                {
                    string defName = thing.def.defName;

                    // Get or Create the Group for this ore type
                    if (!oreData.Ores.TryGetValue(defName, out OreGroupDto group))
                    {
                        group = new OreGroupDto
                        {
                            MaxHp = thing.MaxHitPoints,
                            Cells = new List<int>(),
                            Hp = new List<int>()
                        };
                        oreData.Ores[defName] = group;
                    }

                    // Flatten Position (X, Z) into a single integer Index
                    // Formula: index = (z * width) + x
                    // We ignore Y because ores are always on the surface layer in RimWorld
                    int index = (thing.Position.z * width) + thing.Position.x;

                    group.Cells.Add(index);
                    group.Hp.Add(thing.HitPoints);
                }
            }

            return oreData;
        }

        public static StockpileResponseDto CreateStockpile(CreateStockpileRequestDto request)
        {
            try
            {
                var map = GetMapByID(request.MapId);
                if (map == null)
                {
                    return new StockpileResponseDto
                    {
                        Success = false,
                        Message = $"Map with ID {request.MapId} not found."
                    };
                }

                // Validate positions
                if (request.PointA == null || request.PointB == null)
                {
                    return new StockpileResponseDto
                    {
                        Success = false,
                        Message = "PointA and PointB cannot be null."
                    };
                }

                // Convert positions to IntVec3
                IntVec3 pointA = new IntVec3(request.PointA.X, request.PointA.Y, request.PointA.Z);
                IntVec3 pointB = new IntVec3(request.PointB.X, request.PointB.Y, request.PointB.Z);

                // Normalize rectangle (ensure min/max are in correct order)
                int minX = Mathf.Min(pointA.x, pointB.x);
                int maxX = Mathf.Max(pointA.x, pointB.x);
                int minZ = Mathf.Min(pointA.z, pointB.z);
                int maxZ = Mathf.Max(pointA.z, pointB.z);

                // Validate that rectangle has at least one cell
                if (minX > maxX || minZ > maxZ)
                {
                    return new StockpileResponseDto
                    {
                        Success = false,
                        Message = "Invalid rectangle: PointA and PointB form an invalid area."
                    };
                }

                // Create CellRect from normalized coordinates
                IntVec3 cellRectA = new IntVec3(minX, 0, minZ);
                IntVec3 cellRectB = new IntVec3(maxX, 0, maxZ);
                CellRect rect = CellRect.FromLimits(cellRectA, cellRectB);

                // Validate cells are in bounds
                if (!rect.All(cell => cell.InBounds(map)))
                {
                    return new StockpileResponseDto
                    {
                        Success = false,
                        Message = "Some cells in the specified rectangle are out of map bounds."
                    };
                }

                if (rect.Area == 0)
                {
                    return new StockpileResponseDto
                    {
                        Success = false,
                        Message = "No valid cells found in the specified rectangle."
                    };
                }

                // Create new stockpile zone
                Zone_Stockpile stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);

                // Set name BEFORE registering
                if (string.IsNullOrEmpty(request.Name))
                {
                    // Auto-generate name: "Stockpile #"
                    int stockpileCount = map.zoneManager.AllZones.OfType<Zone_Stockpile>().Count() + 1;
                    stockpile.label = $"Stockpile {stockpileCount}";
                }
                else
                {
                    stockpile.label = request.Name;
                }

                // Set priority BEFORE registering
                int priorityValue = request.Priority ?? 0; // Default to High (0)
                priorityValue = Mathf.Clamp(priorityValue, -1, 2);
                stockpile.settings.Priority = (StoragePriority)priorityValue;

                // Configure storage settings (filtration)
                bool hasItemDefs = request.AllowedItemDefs != null && request.AllowedItemDefs.Count > 0;
                bool hasCategories = request.AllowedItemCategories != null && request.AllowedItemCategories.Count > 0;

                if (hasItemDefs || hasCategories)
                {
                    // Disallow all first
                    foreach (ThingDef thingDef in DefDatabase<ThingDef>.AllDefs)
                    {
                        stockpile.settings.filter.SetAllow(thingDef, false);
                    }

                    // Allow specified item defs
                    if (hasItemDefs)
                    {
                        foreach (var defName in request.AllowedItemDefs)
                        {
                            ThingDef thingDef = DefDatabase<ThingDef>.GetNamed(defName, false);
                            if (thingDef != null)
                            {
                                stockpile.settings.filter.SetAllow(thingDef, true);
                            }
                        }
                    }

                    // Allow items from specified categories
                    if (hasCategories)
                    {
                        foreach (var categoryName in request.AllowedItemCategories)
                        {
                            ThingCategoryDef categoryDef = DefDatabase<ThingCategoryDef>.GetNamed(categoryName, false);
                            if (categoryDef != null)
                            {
                                foreach (ThingDef thingDef in categoryDef.DescendantThingDefs)
                                {
                                    stockpile.settings.filter.SetAllow(thingDef, true);
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Default allowance: allow all items that storage zones normally allow
                    foreach (ThingDef thingDef in DefDatabase<ThingDef>.AllDefs)
                    {
                        if (thingDef.category == ThingCategory.Item && 
                            !thingDef.IsCorpse && 
                            thingDef.alwaysHaulable)
                        {
                            stockpile.settings.filter.SetAllow(thingDef, true);
                        }
                    }
                }

                // Set hit points filter
                float minHpPercent = request.MinHitPointsPercent ?? 0.0f;
                float maxHpPercent = request.MaxHitPointsPercent ?? 1.0f;
                minHpPercent = Mathf.Clamp01(minHpPercent);
                maxHpPercent = Mathf.Clamp01(maxHpPercent);
                
                if (minHpPercent > 0.0f || maxHpPercent < 1.0f)
                {
                    stockpile.settings.filter.AllowedHitPointsPercents = new Verse.FloatRange(minHpPercent, maxHpPercent);
                    stockpile.settings.filter.allowedHitPointsConfigurable = true;
                }

                // Set quality filter
                if (!string.IsNullOrEmpty(request.MinQuality) || !string.IsNullOrEmpty(request.MaxQuality))
                {
                    QualityCategory minQuality = QualityCategory.Awful;
                    QualityCategory maxQuality = QualityCategory.Legendary;

                    if (!string.IsNullOrEmpty(request.MinQuality))
                    {
                        if (System.Enum.TryParse<QualityCategory>(request.MinQuality, out var parsedMin))
                        {
                            minQuality = parsedMin;
                        }
                    }

                    if (!string.IsNullOrEmpty(request.MaxQuality))
                    {
                        if (System.Enum.TryParse<QualityCategory>(request.MaxQuality, out var parsedMax))
                        {
                            maxQuality = parsedMax;
                        }
                    }

                    stockpile.settings.filter.AllowedQualityLevels = new QualityRange(minQuality, maxQuality);
                    stockpile.settings.filter.allowedQualitiesConfigurable = true;
                }

                // IMPORTANT: Register the zone BEFORE adding cells to avoid conflicts
                map.zoneManager.RegisterZone(stockpile);

                // NOW add all cells to the registered zone
                foreach (IntVec3 cell in rect)
                {
                    stockpile.AddCell(cell);
                }

                int cellCount = rect.Area;
                LogApi.Info($"Created stockpile '{stockpile.label}' with {cellCount} cells at priority {stockpile.settings.Priority}");

                return new StockpileResponseDto
                {
                    Success = true,
                    ZoneId = stockpile.ID,
                    Name = stockpile.label,
                    CellsCount = cellCount,
                    Priority = (int)stockpile.settings.Priority,
                    Message = $"Stockpile '{stockpile.label}' created successfully with {cellCount} cells."
                };
            }
            catch (Exception ex)
            {
                LogApi.Error($"Error creating stockpile: {ex}");
                return new StockpileResponseDto
                {
                    Success = false,
                    Message = $"Failed to create stockpile: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Helper method to find a stockpile zone by ID across all maps
        /// </summary>
        private static (Zone_Stockpile stockpile, Map map) FindStockpileById(int zoneId)
        {
            foreach (Map map in Find.Maps)
            {
                var zone = map.zoneManager.AllZones.OfType<Zone_Stockpile>()
                    .FirstOrDefault(z => z.ID == zoneId);
                
                if (zone != null)
                {
                    return (zone, map);
                }
            }
            return (null, null);
        }

        public static ApiResult DeleteStockpile(int zoneId)
        {
            try
            {
                var (stockpile, stockpileMap) = FindStockpileById(zoneId);

                if (stockpile == null)
                {
                    return ApiResult.Fail($"Stockpile with ID {zoneId} not found.");
                }

                string stockpileName = stockpile.label;
                int cellsCount = stockpile.CellCount;
                Map mapToCleanup = stockpileMap; // Capture for lambda
                Zone_Stockpile zoneToDelete = stockpile; // Capture for lambda

                // Execute on main thread to ensure proper cleanup
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    try
                    {
                        if (mapToCleanup != null && zoneToDelete != null)
                        {
                            // Clear all cells from the zone using Cells list directly
                            var cellsList = zoneToDelete.Cells;
                            if (cellsList != null && cellsList.Count > 0)
                            {
                                // Copy cells to avoid collection modification during iteration
                                var cellsToRemove = cellsList.ToList();
                                foreach (var cell in cellsToRemove)
                                {
                                    zoneToDelete.RemoveCell(cell);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!ex.Message.Contains("haul destination"))
                        {
                            LogApi.Warning($"Stockpile deletion warning: {ex.Message}");
                        }
                    }
                });

                LogApi.Info($"Deleted stockpile '{stockpileName}' (ID: {zoneId}) with {cellsCount} cells");
                return ApiResult.Ok();
            }
            catch (Exception ex)
            {
                LogApi.Error($"Error deleting stockpile: {ex}");
                return ApiResult.Fail($"Failed to delete stockpile: {ex.Message}");
            }
        }

        public static ApiResult<StockpileResponseDto> UpdateStockpile(UpdateStockpileRequestDto request)
        {
            try
            {
                if (request == null)
                {
                    return ApiResult<StockpileResponseDto>.Fail("Request cannot be null.");
                }

                var (stockpile, stockpileMap) = FindStockpileById(request.ZoneId);

                if (stockpile == null)
                {
                    return ApiResult<StockpileResponseDto>.Fail($"Stockpile with ID {request.ZoneId} not found.");
                }

                bool filterWasModified = false;

                if (!string.IsNullOrEmpty(request.Name))
                {
                    stockpile.label = request.Name;
                }

                if (request.Priority.HasValue)
                {
                    int priorityValue = Mathf.Clamp(request.Priority.Value, 0, 5);
                    stockpile.settings.Priority = (StoragePriority)priorityValue;
                }

                if (request.MinHitPointsPercent.HasValue || request.MaxHitPointsPercent.HasValue)
                {
                    float minHp = request.MinHitPointsPercent ?? stockpile.settings.filter.AllowedHitPointsPercents.min;
                    float maxHp = request.MaxHitPointsPercent ?? stockpile.settings.filter.AllowedHitPointsPercents.max;
                    
                    minHp = Mathf.Clamp01(minHp);
                    maxHp = Mathf.Clamp01(maxHp);
                    
                    stockpile.settings.filter.AllowedHitPointsPercents = new Verse.FloatRange(minHp, maxHp);
                    stockpile.settings.filter.allowedHitPointsConfigurable = true;
                    filterWasModified = true;
                }

                if (!string.IsNullOrEmpty(request.MinQuality) || !string.IsNullOrEmpty(request.MaxQuality))
                {
                    QualityCategory minQuality = stockpile.settings.filter.AllowedQualityLevels.min;
                    QualityCategory maxQuality = stockpile.settings.filter.AllowedQualityLevels.max;

                    if (!string.IsNullOrEmpty(request.MinQuality))
                    {
                        if (System.Enum.TryParse<QualityCategory>(request.MinQuality, out var parsedMin))
                        {
                            minQuality = parsedMin;
                        }
                    }

                    if (!string.IsNullOrEmpty(request.MaxQuality))
                    {
                        if (System.Enum.TryParse<QualityCategory>(request.MaxQuality, out var parsedMax))
                        {
                            maxQuality = parsedMax;
                        }
                    }

                    stockpile.settings.filter.AllowedQualityLevels = new QualityRange(minQuality, maxQuality);
                    stockpile.settings.filter.allowedQualitiesConfigurable = true;
                    filterWasModified = true;
                }

                if (request.RemoveItemDefs != null && request.RemoveItemDefs.Count > 0)
                {
                    foreach (var defName in request.RemoveItemDefs)
                    {
                        ThingDef thingDef = DefDatabase<ThingDef>.GetNamed(defName, false);
                        if (thingDef != null)
                        {
                            stockpile.settings.filter.SetAllow(thingDef, false);
                            filterWasModified = true;
                        }
                    }
                }

                if (request.RemoveItemCategories != null && request.RemoveItemCategories.Count > 0)
                {
                    foreach (var categoryName in request.RemoveItemCategories)
                    {
                        ThingCategoryDef categoryDef = DefDatabase<ThingCategoryDef>.GetNamed(categoryName, false);
                        if (categoryDef != null)
                        {
                            foreach (ThingDef thingDef in categoryDef.DescendantThingDefs)
                            {
                                stockpile.settings.filter.SetAllow(thingDef, false);
                                filterWasModified = true;
                            }
                        }
                    }
                }

                if (request.AddItemDefs != null && request.AddItemDefs.Count > 0)
                {
                    foreach (var defName in request.AddItemDefs)
                    {
                        ThingDef thingDef = DefDatabase<ThingDef>.GetNamed(defName, false);
                        if (thingDef != null)
                        {
                            stockpile.settings.filter.SetAllow(thingDef, true);
                            filterWasModified = true;
                        }
                    }
                }

                if (request.AddItemCategories != null && request.AddItemCategories.Count > 0)
                {
                    foreach (var categoryName in request.AddItemCategories)
                    {
                        ThingCategoryDef categoryDef = DefDatabase<ThingCategoryDef>.GetNamed(categoryName, false);
                        if (categoryDef != null)
                        {
                            foreach (ThingDef thingDef in categoryDef.DescendantThingDefs)
                            {
                                stockpile.settings.filter.SetAllow(thingDef, true);
                                filterWasModified = true;
                            }
                        }
                    }
                }

                // If filter was modified, resolve references to apply changes
                if (filterWasModified)
                {
                    try
                    {
                        stockpile.settings.filter.ResolveReferences();
                        LogApi.Info($"Applied filter changes to stockpile '{stockpile.label}'");
                    }
                    catch (Exception ex)
                    {
                        LogApi.Error($"Failed to apply filter changes: {ex.Message}");
                    }
                }

                LogApi.Info($"Updated stockpile '{stockpile.label}' (ID: {request.ZoneId})");

                return ApiResult<StockpileResponseDto>.Ok(new StockpileResponseDto
                {
                    Success = true,
                    ZoneId = stockpile.ID,
                    Name = stockpile.label,
                    CellsCount = stockpile.CellCount,
                    Priority = (int)stockpile.settings.Priority,
                    Message = $"Stockpile '{stockpile.label}' updated successfully."
                });
            }
            catch (Exception ex)
            {
                LogApi.Error($"Error updating stockpile: {ex}");
                return ApiResult<StockpileResponseDto>.Fail($"Failed to update stockpile: {ex.Message}");
            }
        }
    }
}
