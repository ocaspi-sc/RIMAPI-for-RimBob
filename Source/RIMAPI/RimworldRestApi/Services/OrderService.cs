using System.Collections.Generic;
using System.Linq;
using RIMAPI.Core;
using RIMAPI.Helpers;
using RIMAPI.Models;
using RIMAPI.Services;
using RimWorld;
using Verse;

public class OrderService : IOrderService
{
    private const int MaxUnforbidBatchSize = 50;

    public ApiResult DesignateArea(DesignateRequestDto request)
    {
        if (request == null) return ApiResult.Fail("validation failed: request body is required");

        var map = MapHelper.GetMapByID(request.MapId);
        if (map == null) return ApiResult.Fail($"Map with id: {request.MapId} not found");

        string requestedType = FirstNonEmpty(request.Type, request.Designation);
        if (string.IsNullOrEmpty(requestedType))
            return ApiResult.Fail("validation failed: type or designation is required");

        if (!TryGetDesignateRect(request, out IntVec3 start, out IntVec3 end))
            return ApiResult.Fail("validation failed: point_a/point_b or rect is required");

        CellRect rect = CellRect.FromLimits(start, end);

        int count = 0;
        string type = requestedType.ToLower(); // Cache lowercase type

        foreach (IntVec3 c in rect)
        {
            if (!c.InBounds(map)) continue;

            if (type == "mine")
            {
                // Add Mining Designation
                var edifice = c.GetEdifice(map);
                if (edifice != null && edifice.def.mineable && map.designationManager.DesignationAt(c, DesignationDefOf.Mine) == null)
                {
                    map.designationManager.AddDesignation(new Designation(c, DesignationDefOf.Mine));
                    count++;
                }
            }
            else if (type == "deconstruct")
            {
                // Find buildings to deconstruct
                var things = c.GetThingList(map).ToList();
                foreach (var t in things)
                {
                    if (t.def.category == ThingCategory.Building)
                    {
                        map.designationManager.AddDesignation(new Designation(t, DesignationDefOf.Deconstruct));
                        count++;
                    }
                }
            }
            else if (type == "harvest")
            {
                var plants = c.GetThingList(map).Where(t => t.def.category == ThingCategory.Plant).Cast<Plant>();
                foreach (var p in plants)
                {
                    if (p.HarvestableNow && map.designationManager.DesignationOn(p, DesignationDefOf.HarvestPlant) == null)
                    {
                        map.designationManager.AddDesignation(new Designation(p, DesignationDefOf.HarvestPlant));
                        count++;
                    }
                }
            }
            else if (type == "hunt")
            {
                // Find animals to hunt
                var pawns = c.GetThingList(map).OfType<Pawn>();
                foreach (var p in pawns)
                {
                    // Check if it's an animal, not our colony pet, and not already marked
                    if (p.RaceProps.Animal && p.Faction != Faction.OfPlayer && map.designationManager.DesignationOn(p, DesignationDefOf.Hunt) == null)
                    {
                        map.designationManager.AddDesignation(new Designation(p, DesignationDefOf.Hunt));
                        count++;
                    }
                }
            }
            else if (type == "remove-all")
            {
                var things = c.GetThingList(map);
                foreach (var t in things)
                {
                    map.designationManager.RemoveAllDesignationsOn(t);
                }
                count++;
            }
            else
            {
                return ApiResult.Fail($"validation failed: unsupported designation type '{requestedType}'");
            }
        }

        return ApiResult.Ok();
    }

    public ApiResult<UnforbidThingsResponseDto> UnforbidThings(UnforbidThingsRequestDto request)
    {
        if (request == null)
            return ApiResult<UnforbidThingsResponseDto>.Fail("validation failed: request body is required");

        if (request.ThingIds == null || request.ThingIds.Count == 0)
            return ApiResult<UnforbidThingsResponseDto>.Fail("validation failed: thing_ids must contain at least one id");

        if (request.ThingIds.Count > MaxUnforbidBatchSize)
            return ApiResult<UnforbidThingsResponseDto>.Fail($"validation failed: thing_ids cannot contain more than {MaxUnforbidBatchSize} ids");

        var map = MapHelper.GetMapByID(request.MapId);
        if (map == null) return ApiResult<UnforbidThingsResponseDto>.Fail($"Map with id: {request.MapId} not found");

        HashSet<int> requestedIds = new HashSet<int>();
        foreach (string idText in request.ThingIds)
        {
            if (!int.TryParse(idText, out int id))
                return ApiResult<UnforbidThingsResponseDto>.Fail($"validation failed: thing id '{idText}' is not an integer");

            requestedIds.Add(id);
        }

        var response = new UnforbidThingsResponseDto
        {
            Requested = request.ThingIds.Count
        };

        Dictionary<int, Thing> haulableById = map
            .listerThings
            .ThingsInGroup(ThingRequestGroup.HaulableEver)
            .Where(t => !t.Destroyed)
            .GroupBy(t => t.thingIDNumber)
            .ToDictionary(g => g.Key, g => g.First());

        Dictionary<int, Thing> mapThingsById = map
            .listerThings
            .AllThings
            .Where(t => !t.Destroyed)
            .GroupBy(t => t.thingIDNumber)
            .ToDictionary(g => g.Key, g => g.First());

        HashSet<int> otherMapIds = new HashSet<int>(
            Find.Maps
                .Where(m => m.uniqueID != request.MapId)
                .SelectMany(m => m.listerThings.AllThings)
                .Where(t => !t.Destroyed)
                .Select(t => t.thingIDNumber));

        foreach (int id in requestedIds)
        {
            if (haulableById.TryGetValue(id, out Thing thing))
            {
                response.Matched++;
                if (thing.IsForbidden(Faction.OfPlayer))
                {
                    thing.SetForbidden(false, false);
                    response.Changed++;
                }
                else
                {
                    response.AlreadyAllowed++;
                }
            }
            else if (mapThingsById.ContainsKey(id))
            {
                response.NonItemTargets++;
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

        if (response.Missing > 0 || response.NonMapTargets > 0 || response.NonItemTargets > 0)
        {
            return new ApiResult<UnforbidThingsResponseDto>
            {
                Success = false,
                Data = response,
                Errors =
                {
                    "validation failed: all thing_ids must refer to haulable things on the requested map"
                }
            };
        }

        return ApiResult<UnforbidThingsResponseDto>.Ok(response);
    }

    private static bool TryGetDesignateRect(DesignateRequestDto request, out IntVec3 start, out IntVec3 end)
    {
        if (request.PointA != null && request.PointB != null)
        {
            start = new IntVec3(request.PointA.X, 0, request.PointA.Z);
            end = new IntVec3(request.PointB.X, 0, request.PointB.Z);
            return true;
        }

        if (request.Rect != null)
        {
            start = new IntVec3(request.Rect.X1, 0, request.Rect.Z1);
            end = new IntVec3(request.Rect.X2, 0, request.Rect.Z2);
            return true;
        }

        start = IntVec3.Invalid;
        end = IntVec3.Invalid;
        return false;
    }

    private static string FirstNonEmpty(string first, string second)
    {
        if (!string.IsNullOrEmpty(first)) return first;
        if (!string.IsNullOrEmpty(second)) return second;
        return string.Empty;
    }
}
