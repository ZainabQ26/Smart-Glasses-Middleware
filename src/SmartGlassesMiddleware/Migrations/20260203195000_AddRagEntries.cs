using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGlassesMiddleware.Migrations
{
    /// <inheritdoc />
    public partial class AddRagEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManagedFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StorageKey = table.Column<string>(type: "text", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManagedPrompts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PromptText = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedPrompts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RagEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StorageKey = table.Column<string>(type: "text", nullable: false),
                    Transcription = table.Column<string>(type: "text", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IndexedContent = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RagEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequestHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InputText = table.Column<string>(type: "text", nullable: false),
                    DetectedTask = table.Column<string>(type: "text", nullable: true),
                    Response = table.Column<string>(type: "text", nullable: true),
                    UsedAdvancedModel = table.Column<bool>(type: "boolean", nullable: false),
                    MediaStorageKey = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManagedFileGroup",
                columns: table => new
                {
                    ManagedFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceGroupId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedFileGroup", x => new { x.ManagedFileId, x.ResourceGroupId });
                    table.ForeignKey(
                        name: "FK_ManagedFileGroup_ManagedFiles_ManagedFileId",
                        column: x => x.ManagedFileId,
                        principalTable: "ManagedFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ManagedFileGroup_ResourceGroups_ResourceGroupId",
                        column: x => x.ResourceGroupId,
                        principalTable: "ResourceGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_ManagedFileGroup_ResourceGroupId",
                table: "ManagedFileGroup",
                column: "ResourceGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedPromptGroup_ResourceGroupId",
                table: "ManagedPromptGroup",
                column: "ResourceGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_RagEntries_FileId",
                table: "RagEntries",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_RagEntries_ProcessedAt",
                table: "RagEntries",
                column: "ProcessedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManagedFileGroup");

            migrationBuilder.DropTable(
                name: "ManagedPromptGroup");

            migrationBuilder.DropTable(
                name: "RagEntries");

            migrationBuilder.DropTable(
                name: "RequestHistories");

            migrationBuilder.DropTable(
                name: "ManagedFiles");

            migrationBuilder.DropTable(
                name: "ManagedPrompts");

            migrationBuilder.DropTable(
                name: "ResourceGroups");
        }
    }
}
