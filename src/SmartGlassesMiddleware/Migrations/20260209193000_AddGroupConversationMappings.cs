using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGlassesMiddleware.Migrations
{
    public partial class AddGroupConversationMappings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupConversationMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupConversationMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupConversationMappings_ResourceGroupId",
                table: "GroupConversationMappings",
                column: "ResourceGroupId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupConversationMappings");
        }
    }
}
