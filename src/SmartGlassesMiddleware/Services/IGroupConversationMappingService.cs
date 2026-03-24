using System;
using System.Threading.Tasks;

namespace SmartGlassesMiddleware.Services
{
    /// <summary>
    /// Manages the persistent mapping between a resource group and its associated conversation thread.
    /// </summary>
    public interface IGroupConversationMappingService
    {
        /// <summary>
        /// Returns the conversation ID currently linked to the specified resource group,
        /// or <c>null</c> if no mapping exists.
        /// </summary>
        /// <param name="groupId">The resource group identifier.</param>
        Task<Guid?> GetConversationIdForGroupAsync(Guid groupId);

        /// <summary>
        /// Creates or updates the mapping between a resource group and a conversation.
        /// </summary>
        /// <param name="groupId">The resource group identifier.</param>
        /// <param name="conversationId">The conversation to link to the group.</param>
        Task UpsertMappingAsync(Guid groupId, Guid conversationId);

        /// <summary>
        /// Removes the mapping for the specified resource group.
        /// Does nothing if no mapping exists.
        /// </summary>
        /// <param name="groupId">The resource group identifier.</param>
        Task DeleteMappingAsync(Guid groupId);
    }
}
