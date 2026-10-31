using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersonalCardReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Front = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Back = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IntervalDays = table.Column<int>(type: "int", nullable: false),
                    SuccessStep = table.Column<int>(type: "int", nullable: false),
                    SchedulerVersion = table.Column<int>(type: "int", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCards", x => x.Id);
                    table.CheckConstraint("CK_UserCard_Schedule", "[IntervalDays] >= 0 AND [IntervalDays] <= 180 AND [SuccessStep] >= 0 AND [SchedulerVersion] = 1");
                    table.ForeignKey(
                        name: "FK_UserCards_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CardReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    SchedulerVersion = table.Column<int>(type: "int", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BeforeDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AfterDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BeforeIntervalDays = table.Column<int>(type: "int", nullable: false),
                    AfterIntervalDays = table.Column<int>(type: "int", nullable: false),
                    BeforeSuccessStep = table.Column<int>(type: "int", nullable: false),
                    AfterSuccessStep = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardReviews", x => x.Id);
                    table.CheckConstraint("CK_CardReview_Schedule", "[BeforeIntervalDays] >= 0 AND [BeforeIntervalDays] <= 180 AND [AfterIntervalDays] >= 0 AND [AfterIntervalDays] <= 180 AND [BeforeSuccessStep] >= 0 AND [AfterSuccessStep] >= 0 AND [Rating] >= 0 AND [Rating] <= 3 AND [SchedulerVersion] = 1");
                    table.ForeignKey(
                        name: "FK_CardReviews_UserCards_UserCardId",
                        column: x => x.UserCardId,
                        principalTable: "UserCards",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CardSourceContexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContextKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardSourceContexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardSourceContexts_UserCards_UserCardId",
                        column: x => x.UserCardId,
                        principalTable: "UserCards",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardReviews_UserCardId_ReviewedAt_Id",
                table: "CardReviews",
                columns: new[] { "UserCardId", "ReviewedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CardReviews_UserCardId_ReviewEventId",
                table: "CardReviews",
                columns: new[] { "UserCardId", "ReviewEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardSourceContexts_UserCardId_ContextKey",
                table: "CardSourceContexts",
                columns: new[] { "UserCardId", "ContextKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserCards_UserId_ArchivedAt_DueAt_Id",
                table: "UserCards",
                columns: new[] { "UserId", "ArchivedAt", "DueAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_UserCards_UserId_CustomKey",
                table: "UserCards",
                columns: new[] { "UserId", "CustomKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardReviews");

            migrationBuilder.DropTable(
                name: "CardSourceContexts");

            migrationBuilder.DropTable(
                name: "UserCards");
        }
    }
}
