using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGlassesMiddleware.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedFileIdToDocumentMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ManagedFileId",
                table: "DocumentMetadatas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentMetadatas_ManagedFileId",
                table: "DocumentMetadatas",
                column: "ManagedFileId");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentMetadatas_ManagedFiles_ManagedFileId",
                table: "DocumentMetadatas",
                column: "ManagedFileId",
                principalTable: "ManagedFiles",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentMetadatas_ManagedFiles_ManagedFileId",
                table: "DocumentMetadatas");

            migrationBuilder.DropIndex(
                name: "IX_DocumentMetadatas_ManagedFileId",
                table: "DocumentMetadatas");

            migrationBuilder.DropColumn(
                name: "ManagedFileId",
                table: "DocumentMetadatas");
        }
    }
}
