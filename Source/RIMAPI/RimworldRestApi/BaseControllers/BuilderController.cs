using System.Threading.Tasks;
using System.Net;
using RIMAPI.Core;
using RIMAPI.Models;
using RIMAPI.Services;

namespace RIMAPI.Controllers
{
    public class BuilderController
    {
        private readonly IBuilderService _builderService;

        public BuilderController(IBuilderService builderService)
        {
            _builderService = builderService;
        }

        [Post("/api/v1/builder/copy")]
        public async Task CopyArea(HttpListenerContext context)
        {
            var body = await context.Request.ReadBodyAsync<CopyAreaRequestDto>();
            var result = _builderService.CopyArea(body);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/builder/paste")]
        public async Task PasteArea(HttpListenerContext context)
        {
            var body = await context.Request.ReadBodyAsync<PasteAreaRequestDto>();
            var result = _builderService.PasteArea(body);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/builder/blueprint")]
        public async Task PlaceBlueprints(HttpListenerContext context)
        {
            var body = await context.Request.ReadBodyAsync<PasteAreaRequestDto>();
            var result = _builderService.PlaceBlueprints(body);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/builder/blueprint/validate")]
        [EndpointMetadata("Validate one blueprint placement without mutating the map")]
        public async Task ValidateBlueprint(HttpListenerContext context)
        {
            var body = await context.Request.ReadBodyAsync<BlueprintValidateRequestDto>();
            var result = _builderService.ValidateBlueprint(body);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/builder/blueprint/place")]
        [EndpointMetadata("Place one validated blueprint without destroying obstacles")]
        public async Task PlaceBlueprint(HttpListenerContext context)
        {
            var body = await context.Request.ReadBodyAsync<BlueprintPlaceRequestDto>();
            var result = _builderService.PlaceBlueprint(body);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/builder/blueprint/allowed-state")]
        [EndpointMetadata("Allow or disallow explicit pending blueprint and frame ids")]
        public async Task SetBlueprintAllowedState(HttpListenerContext context)
        {
            var body = await context.Request.ReadBodyAsync<BlueprintAllowedStateRequestDto>();
            var result = _builderService.SetBlueprintAllowedState(body);
            await context.SendJsonResponse(result);
        }

        [Post("/api/v1/builder/blueprint/cancel")]
        [EndpointMetadata("Cancel explicit pending blueprint and frame ids")]
        public async Task CancelBlueprints(HttpListenerContext context)
        {
            var body = await context.Request.ReadBodyAsync<BlueprintThingIdsRequestDto>();
            var result = _builderService.CancelBlueprints(body);
            await context.SendJsonResponse(result);
        }
    }
}
