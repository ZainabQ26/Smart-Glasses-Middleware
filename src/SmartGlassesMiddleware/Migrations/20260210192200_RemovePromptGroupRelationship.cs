using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGlassesMiddleware.Migrations
{
    /// <inheritdoc />
    public partial class RemovePromptGroupRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManagedPromptGroup");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManagedPromptGroup",
                columns: table => new
                {
                    ManagedPromptId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceGroupId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedPromptGroup", x => new { x.ManagedPromptId, x.ResourceGroupId });
                    table.ForeignKey(
                        name: "FK_ManagedPromptGroup_ManagedPrompts_ManagedPromptId",
                        column: x => x.ManagedPromptId,
                        principalTable: "ManagedPrompts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ManagedPromptGroup_ResourceGroups_ResourceGroupId",
                        column: x => x.ResourceGroupId,
                        principalTable: "ResourceGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedPromptGroup_ResourceGroupId",
                table: "ManagedPromptGroup",
                column: "ResourceGroupId");
        }
    }
}
