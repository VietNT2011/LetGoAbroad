using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PathChoiceActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PathChoice_Values",
                table: "PathChoiceRevisions");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActivatedAt",
                table: "PathChoiceRevisions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PathChoice_Values",
                table: "PathChoiceRevisions",
                sql: "[Number] >= 1 AND [EffectiveAt] >= [RecordedAt] AND ([ActivatedAt] IS NULL OR [ActivatedAt] >= [EffectiveAt]) AND (([PathVersionId] IS NULL AND [PathStartedOn] IS NULL) OR ([PathVersionId] IS NOT NULL AND [PathStartedOn] IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PathChoice_Values",
                table: "PathChoiceRevisions");

            migrationBuilder.DropColumn(
                name: "ActivatedAt",
                table: "PathChoiceRevisions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PathChoice_Values",
                table: "PathChoiceRevisions",
                sql: "[Number] >= 1 AND [EffectiveAt] >= [RecordedAt] AND (([PathVersionId] IS NULL AND [PathStartedOn] IS NULL) OR ([PathVersionId] IS NOT NULL AND [PathStartedOn] IS NOT NULL))");
        }
    }
}
