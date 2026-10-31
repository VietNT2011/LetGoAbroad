using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LessonBodyAnnotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LessonAnnotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlockKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AnchorStart = table.Column<int>(type: "int", nullable: false),
                    AnchorEnd = table.Column<int>(type: "int", nullable: false),
                    Quote = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    NoteText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    IsHighlighted = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonAnnotations", x => x.Id);
                    table.CheckConstraint("CK_LessonAnnotation_Anchor", "[BlockKey] = 'body' AND [AnchorStart] >= 0 AND [AnchorEnd] > [AnchorStart] AND [AnchorEnd] - [AnchorStart] <= 4000");
                    table.ForeignKey(
                        name: "FK_LessonAnnotations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LessonAnnotations_ContentVersions_ContentVersionId",
                        column: x => x.ContentVersionId,
                        principalTable: "ContentVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LessonAnnotations_ContentVersionId",
                table: "LessonAnnotations",
                column: "ContentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonAnnotations_UserId_ContentVersionId_IsDeleted",
                table: "LessonAnnotations",
                columns: new[] { "UserId", "ContentVersionId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonAnnotations_UserId_UpdatedAt_Id",
                table: "LessonAnnotations",
                columns: new[] { "UserId", "UpdatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LessonAnnotations");
        }
    }
}
