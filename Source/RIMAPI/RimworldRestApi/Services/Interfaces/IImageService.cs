
using System.Collections.Generic;
using System.Threading.Tasks;
using RIMAPI.Core;
using RIMAPI.Models;

namespace RIMAPI.Services
{
    public interface IImageService
    {
        Task<ApiResult<ImageDto>> GetItemImage(string name);
        Task<ApiResult<ImageDto>> GetTerrainImage(string name);
        ApiResult SetItemImageByName(ImageUploadRequest request);
        ApiResult SetStuffColor(StuffColorRequest request);
    }
}
