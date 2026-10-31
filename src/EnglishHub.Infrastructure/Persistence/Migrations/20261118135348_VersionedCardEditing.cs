using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VersionedCardEditing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContentVersion",
                table: "UserCards",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "ContentVersion",
                table: "CardReviews",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "UserCardVersions",
                columns: table => new
                {
                    UserCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Front = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Back = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCardVersions", x => new { x.UserCardId, x.Number });
                    table.CheckConstraint("CK_UserCardVersion_Number", "[Number] > 0");
                    table.ForeignKey(
                        name: "FK_UserCardVersions_UserCards_UserCardId",
                        column: x => x.UserCardId,
                        principalTable: "UserCards",
                        principalColumn: "Id");
                });

            // VI: Trước đây hai mặt bất biến, nên mọi lượt ôn cũ thuộc bản1; không tạo lượt ôn hoặc thay lịch. EN: Previously immutable card text is version1 for all existing reviews; preserve schedules and review sources.
            migrationBuilder.Sql("INSERT INTO [UserCardVersions] ([UserCardId],[Number],[Front],[Back],[CreatedAt]) SELECT [Id],1,[Front],[Back],[CreatedAt] FROM [UserCards];");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserCard_ContentVersion",
                table: "UserCards",
                sql: "[ContentVersion] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_CardReviews_UserCardId_ContentVersion",
                table: "CardReviews",
                columns: new[] { "UserCardId", "ContentVersion" });

            migrationBuilder.AddForeignKey(
                name: "FK_CardReviews_UserCardVersions_UserCardId_ContentVersion",
                table: "CardReviews",
                columns: new[] { "UserCardId", "ContentVersion" },
                principalTable: "UserCardVersions",
                principalColumns: new[] { "UserCardId", "Number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CardReviews_UserCardVersions_UserCardId_ContentVersion",
                table: "CardReviews");

            migrationBuilder.DropTable(
                name: "UserCardVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserCard_ContentVersion",
                table: "UserCards");

            migrationBuilder.DropIndex(
                name: "IX_CardReviews_UserCardId_ContentVersion",
                table: "CardReviews");

            migrationBuilder.DropColumn(
                name: "ContentVersion",
                table: "UserCards");

            migrationBuilder.DropColumn(
                name: "ContentVersion",
                table: "CardReviews");
        }
    }
}
