using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuctionRoom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldingAcquisitionPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AcquisitionPrice",
                table: "Holdings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcquisitionPrice",
                table: "Holdings");
        }
    }
}
