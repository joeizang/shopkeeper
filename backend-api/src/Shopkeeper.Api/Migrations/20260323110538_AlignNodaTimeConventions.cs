using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Shopkeeper.Api.Migrations
{
    /// <inheritdoc />
    public partial class AlignNodaTimeConventions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ExpiryDate",
                table: "InventoryItems",
                type: "text",
                nullable: true,
                oldClrType: typeof(LocalDate),
                oldType: "date",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<LocalDate>(
                name: "ExpiryDate",
                table: "InventoryItems",
                type: "date",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
