using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EstimatedStudyTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivitySessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSequence = table.Column<int>(type: "int", nullable: false),
                    WasActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivitySessions", x => x.Id);
                    table.UniqueConstraint("AK_ActivitySessions_Id_UserId", x => new { x.Id, x.UserId });
                    table.CheckConstraint("CK_ActivitySession_Values", "[LastSequence] >= 0 AND [LastSequence] <= 1000000 AND [ExpiresAt] > [CreatedAt] AND [Kind] IN ('lesson','assessment','cards')");
                    table.ForeignKey(
                        name: "FK_ActivitySessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ActivityHeartbeats",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreditedTicks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityHeartbeats", x => new { x.SessionId, x.Sequence });
                    table.UniqueConstraint("AK_ActivityHeartbeats_SessionId_Sequence_UserId", x => new { x.SessionId, x.Sequence, x.UserId });
                    table.CheckConstraint("CK_ActivityHeartbeat_Values", "[Sequence] > 0 AND [Sequence] <= 1000000 AND [CreditedTicks] >= 0 AND [CreditedTicks] <= 300000000");
                    table.ForeignKey(
                        name: "FK_ActivityHeartbeats_ActivitySessions_SessionId_UserId",
                        columns: x => new { x.SessionId, x.UserId },
                        principalTable: "ActivitySessions",
                        principalColumns: new[] { "Id", "UserId" });
                });

            migrationBuilder.CreateTable(
                name: "ActivityIntervals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DurationTicks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityIntervals", x => x.Id);
                    table.CheckConstraint("CK_ActivityInterval_Duration", "[EndAt] > [StartAt] AND [DurationTicks] > 0 AND [DurationTicks] <= 300000000 AND (DATEDIFF_BIG(nanosecond,[StartAt],[EndAt]) / 100 = [DurationTicks])");
                    table.ForeignKey(
                        name: "FK_ActivityIntervals_ActivityHeartbeats_SessionId_Sequence_UserId",
                        columns: x => new { x.SessionId, x.Sequence, x.UserId },
                        principalTable: "ActivityHeartbeats",
                        principalColumns: new[] { "SessionId", "Sequence", "UserId" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityHeartbeats_SessionId_UserId",
                table: "ActivityHeartbeats",
                columns: new[] { "SessionId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityIntervals_SessionId_Sequence",
                table: "ActivityIntervals",
                columns: new[] { "SessionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityIntervals_SessionId_Sequence_UserId",
                table: "ActivityIntervals",
                columns: new[] { "SessionId", "Sequence", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityIntervals_UserId_EndAt_StartAt",
                table: "ActivityIntervals",
                columns: new[] { "UserId", "EndAt", "StartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivitySessions_UserId_ClientSessionId",
                table: "ActivitySessions",
                columns: new[] { "UserId", "ClientSessionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityIntervals");

            migrationBuilder.DropTable(
                name: "ActivityHeartbeats");

            migrationBuilder.DropTable(
                name: "ActivitySessions");
        }
    }
}
