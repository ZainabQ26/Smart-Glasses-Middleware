using System;

namespace SmartGlassesMiddleware.Models;

public class GroupConversationMapping
{
    public Guid Id { get; set; }
    public Guid ResourceGroupId { get; set; }
    public Guid ConversationId { get; set; }
    public DateTime UpdatedAt { get; set; }
}
