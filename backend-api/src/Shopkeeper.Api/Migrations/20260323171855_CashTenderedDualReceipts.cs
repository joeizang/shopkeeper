using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shopkeeper.Api.Migrations
{
    /// <inheritdoc />
    public partial class CashTenderedDualReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CashTendered",
                table: "SalePayments",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CashTendered",
                table: "SalePayments");
        }
    }
}
