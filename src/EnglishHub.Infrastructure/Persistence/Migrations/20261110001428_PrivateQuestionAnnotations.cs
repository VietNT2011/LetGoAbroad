using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrivateQuestionAnnotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_AttemptItems_Id_QuestionId",
                table: "AttemptItems",
                columns: new[] { "Id", "QuestionId" });

            migrationBuilder.CreateTable(
                name: "QuestionAnnotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAttemptItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsBookmarked = table.Column<bool>(type: "bit", nullable: false),
                    NoteText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    AnchorBlock = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AnchorStart = table.Column<int>(type: "int", nullable: false),
                    AnchorEnd = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionAnnotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionAnnotations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuestionAnnotations_AttemptItems_SourceAttemptItemId_QuestionId",
                        columns: x => new { x.SourceAttemptItemId, x.QuestionId },
                        principalTable: "AttemptItems",
                        principalColumns: new[] { "Id", "QuestionId" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionAnnotations_SourceAttemptItemId_QuestionId",
                table: "QuestionAnnotations",
                columns: new[] { "SourceAttemptItemId", "QuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionAnnotations_UserId_QuestionId",
                table: "QuestionAnnotations",
                columns: new[] { "UserId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionAnnotations_UserId_UpdatedAt_Id",
                table: "QuestionAnnotations",
                columns: new[] { "UserId", "UpdatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestionAnnotations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AttemptItems_Id_QuestionId",
                table: "AttemptItems");
        }
    }
}
