using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLabResultLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LabResultLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabResultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ParameterName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ParameterKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ReferenceLow = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ReferenceHigh = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabResultLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LabResultLines_LabResults_LabResultId",
                        column: x => x.LabResultId,
                        principalTable: "LabResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LabResultLines_LabResultId_ParameterKey",
                table: "LabResultLines",
                columns: new[] { "LabResultId", "ParameterKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LabResultLines");
        }
    }
}
