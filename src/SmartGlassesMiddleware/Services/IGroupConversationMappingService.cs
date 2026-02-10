using System;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Services
{
    public interface IGroupConversationMappingService
    {
        Task<Guid?> GetConversationIdForGroupAsync(Guid groupId);
        Task UpsertMappingAsync(Guid groupId, Guid conversationId);
        Task DeleteMappingAsync(Guid groupId);
    }
}
