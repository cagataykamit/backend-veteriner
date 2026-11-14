using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTreatmentCancellationAndRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "Treatments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "Treatments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "Treatments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Treatments",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Treatments_Cancel_Consistent",
                table: "Treatments",
                sql: "([CancelledAtUtc] IS NULL AND [CancelReason] IS NULL AND [CancelledByUserId] IS NULL) OR ([CancelledAtUtc] IS NOT NULL AND [CancelReason] IS NOT NULL AND [CancelledByUserId] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Treatments_Cancel_Consistent",
                table: "Treatments");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "Treatments");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "Treatments");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Treatments");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Treatments");
        }
    }
}
