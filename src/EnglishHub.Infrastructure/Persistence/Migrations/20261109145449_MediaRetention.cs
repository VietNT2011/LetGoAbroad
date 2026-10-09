using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MediaRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastCleanupAt",
                table: "MediaAssets",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastCleanupAt",
                table: "MediaAssets");
        }
    }
}
