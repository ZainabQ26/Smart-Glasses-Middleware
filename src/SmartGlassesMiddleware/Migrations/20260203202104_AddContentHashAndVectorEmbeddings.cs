using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGlassesMiddleware.Migrations
{
    /// <inheritdoc />
    public partial class AddContentHashAndVectorEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RagEntries_FileId",
                table: "RagEntries");

            migrationBuilder.AddColumn<float[]>(
                name: "EmbeddingVector",
                table: "RagEntries",
                type: "real[]",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "ManagedFiles",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RagEntries_FileId",
                table: "RagEntries",
                column: "FileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RagEntries_StorageKey",
                table: "RagEntries",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagedFiles_ContentHash",
                table: "ManagedFiles",
                column: "ContentHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RagEntries_FileId",
                table: "RagEntries");

            migrationBuilder.DropIndex(
                name: "IX_RagEntries_StorageKey",
                table: "RagEntries");

            migrationBuilder.DropIndex(
                name: "IX_ManagedFiles_ContentHash",
                table: "ManagedFiles");

            migrationBuilder.DropColumn(
                name: "EmbeddingVector",
                table: "RagEntries");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "ManagedFiles");

            migrationBuilder.CreateIndex(
                name: "IX_RagEntries_FileId",
                table: "RagEntries",
                column: "FileId");
        }
    }
}
