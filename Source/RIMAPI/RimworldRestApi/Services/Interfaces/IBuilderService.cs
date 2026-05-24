using RIMAPI.Core;
using RIMAPI.Models;

namespace RIMAPI.Services
{
    public interface IBuilderService
    {
        ApiResult<BlueprintDto> CopyArea(CopyAreaRequestDto request);
        ApiResult PasteArea(PasteAreaRequestDto request);
        ApiResult PlaceBlueprints(PasteAreaRequestDto request);
        ApiResult<BlueprintValidateResultDto> ValidateBlueprint(BlueprintValidateRequestDto request);
        ApiResult<BlueprintPlaceResultDto> PlaceBlueprint(BlueprintPlaceRequestDto request);
        ApiResult<BlueprintBatchResponseDto> SetBlueprintAllowedState(BlueprintAllowedStateRequestDto request);
        ApiResult<BlueprintBatchResponseDto> CancelBlueprints(BlueprintThingIdsRequestDto request);
    }
}
