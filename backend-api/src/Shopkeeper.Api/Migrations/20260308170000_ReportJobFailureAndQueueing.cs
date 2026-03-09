using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shopkeeper.Api.Migrations
{
    public partial class ReportJobFailureAndQueueing : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "ReportJobs",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "ReportJobs");
        }
    }
}
