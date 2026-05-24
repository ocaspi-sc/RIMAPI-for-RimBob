using System.Collections.Generic;
using RIMAPI.Models;

namespace RIMAPI.Models
{
    // --- Request Objects ---
    public class CopyAreaRequestDto
    {
        public int MapId { get; set; }
        public PositionDto PointA { get; set; }
        public PositionDto PointB { get; set; }
    }

    public class PasteAreaRequestDto
    {
        public int MapId { get; set; }
        public PositionDto Position { get; set; } // This will be the bottom-left corner
        public BlueprintDto Blueprint { get; set; }
        public bool ClearObstacles { get; set; } = true; // Destroy existing things before pasting
    }

    public class BlueprintCellDto
    {
        public int X { get; set; }
        public int Z { get; set; }
    }

    public class BlueprintCostDto
    {
        public string DefName { get; set; }
        public int Count { get; set; }
    }

    public class BlueprintMaterialAvailabilityDto
    {
        public string DefName { get; set; }
        public int Required { get; set; }
        public int Available { get; set; }
        public int Missing { get; set; }
    }

    public class BlueprintValidateRequestDto
    {
        public int MapId { get; set; }
        public string DefName { get; set; }
        public string StuffDefName { get; set; }
        public BlueprintCellDto Cell { get; set; }
        public int Rotation { get; set; }
    }

    public class BlueprintPlaceRequestDto : BlueprintValidateRequestDto
    {
        public bool? Allowed { get; set; }
    }

    public class BlueprintValidateResultDto
    {
        public bool CanPlace { get; set; }
        public string Reason { get; set; }
        public string DefType { get; set; }
        public List<BlueprintCellDto> OccupiesCells { get; set; } = new List<BlueprintCellDto>();
        public List<BlueprintCostDto> Cost { get; set; } = new List<BlueprintCostDto>();
        public float WorkToBuild { get; set; }
        public bool AlreadyBlueprinted { get; set; }
        public bool AlreadyBuilt { get; set; }
    }

    public class BlueprintPlaceResultDto
    {
        public string Status { get; set; }
        public bool Placed { get; set; }
        public int? ThingId { get; set; }
        public string Reason { get; set; }
        public BlueprintValidateResultDto Validate { get; set; }
    }

    public class BlueprintThingIdsRequestDto
    {
        public int MapId { get; set; }
        public List<string> ThingIds { get; set; } = new List<string>();
    }

    public class BlueprintAllowedStateRequestDto : BlueprintThingIdsRequestDto
    {
        public bool Allowed { get; set; }
    }

    public class BlueprintBatchResponseDto
    {
        public int Requested { get; set; }
        public int Matched { get; set; }
        public int Changed { get; set; }
        public int AlreadyInState { get; set; }
        public int Cancelled { get; set; }
        public int AlreadyGone { get; set; }
        public int Missing { get; set; }
        public int NonMapTargets { get; set; }
        public int NonPendingBuildTargets { get; set; }
    }

    public class PendingBuildDto
    {
        public int Id { get; set; }
        public string Kind { get; set; }
        public string DefName { get; set; }
        public string DefType { get; set; }
        public string StuffDefName { get; set; }
        public BlueprintCellDto Cell { get; set; }
        public int Rotation { get; set; }
        public bool Allowed { get; set; }
        public bool IsForbidden { get; set; }
        public float? WorkLeft { get; set; }
        public int? Hp { get; set; }
        public List<BlueprintCostDto> Cost { get; set; } = new List<BlueprintCostDto>();
    }

    public class ConstructionBacklogGroupDto
    {
        public string Kind { get; set; }
        public string DefName { get; set; }
        public string StuffDefName { get; set; }
        public bool Allowed { get; set; }
        public int Count { get; set; }
        public List<int> ThingIds { get; set; } = new List<int>();
        public List<BlueprintCellDto> SampleCells { get; set; } = new List<BlueprintCellDto>();
        public float TotalWorkLeft { get; set; }
        public List<BlueprintCostDto> Cost { get; set; } = new List<BlueprintCostDto>();
        public List<BlueprintMaterialAvailabilityDto> MaterialsAvailable { get; set; } = new List<BlueprintMaterialAvailabilityDto>();
        public List<BlueprintMaterialAvailabilityDto> MaterialsMissing { get; set; } = new List<BlueprintMaterialAvailabilityDto>();
        public int BlockedCount { get; set; }
        public int DisallowedCount { get; set; }
    }

    // --- The Blueprint Data Structure ---
    public class BlueprintDto
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public List<SavedTerrainDto> Floors { get; set; } = new List<SavedTerrainDto>();
        public List<SavedBuildingDto> Buildings { get; set; } = new List<SavedBuildingDto>();
    }

    public class SavedTerrainDto
    {
        public string DefName { get; set; }
        public int RelX { get; set; }
        public int RelZ { get; set; }
    }

    public class SavedBuildingDto
    {
        public string DefName { get; set; }
        public string StuffDefName { get; set; } // Material (Wood, Steel, etc)
        public int RelX { get; set; }
        public int RelZ { get; set; }
        public int Rotation { get; set; } // 0=North, 1=East, 2=South, 3=West
    }
}
