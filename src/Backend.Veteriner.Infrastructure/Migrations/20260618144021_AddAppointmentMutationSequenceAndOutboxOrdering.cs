using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentMutationSequenceAndOutboxOrdering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AppointmentId",
                table: "OutboxMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AppointmentSequence",
                table: "OutboxMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MutationSequence",
                table: "Appointments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_AppointmentId_AppointmentSequence",
                table: "OutboxMessages",
                columns: new[] { "AppointmentId", "AppointmentSequence" },
                unique: true,
                filter: "[AppointmentId] IS NOT NULL AND [AppointmentSequence] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_AppointmentId_AppointmentSequence",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "AppointmentId",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "AppointmentSequence",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "MutationSequence",
                table: "Appointments");
        }
    }
}
