using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Models;

namespace SmartGlassesMiddleware.Services
{
    public class GroupConversationMappingService : IGroupConversationMappingService
    {
        private readonly ApplicationDbContext _context;

        public GroupConversationMappingService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Guid?> GetConversationIdForGroupAsync(Guid groupId)
        {
            var mapping = await _context.GroupConversationMappings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ResourceGroupId == groupId);
            return mapping?.ConversationId;
        }

        public async Task UpsertMappingAsync(Guid groupId, Guid conversationId)
        {
            var existing = await _context.GroupConversationMappings
                .FirstOrDefaultAsync(m => m.ResourceGroupId == groupId);
            if (existing == null)
            {
                existing = new GroupConversationMapping
                {
                    Id = Guid.NewGuid(),
                    ResourceGroupId = groupId,
                    ConversationId = conversationId,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.GroupConversationMappings.Add(existing);
            }
            else
            {
                existing.ConversationId = conversationId;
                existing.UpdatedAt = DateTime.UtcNow;
                _context.GroupConversationMappings.Update(existing);
            }

            await _context.SaveChangesAsync();
        }

        public async Task DeleteMappingAsync(Guid groupId)
        {
            var existing = await _context.GroupConversationMappings.FirstOrDefaultAsync(m => m.ResourceGroupId == groupId);
            if (existing != null)
            {
                _context.GroupConversationMappings.Remove(existing);
                await _context.SaveChangesAsync();
            }
        }
    }
}
