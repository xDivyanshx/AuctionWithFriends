using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuctionRoom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuctionNomination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1, not EF's 0: rounds are 1-based, so existing rooms must land in
            // round 1 rather than a round that does not exist.
            migrationBuilder.AddColumn<int>(
                name: "AuctionRound",
                table: "Rooms",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentNominationPlayerId",
                table: "Rooms",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuctionPasses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    PassedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionPasses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuctionPasses_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuctionPasses_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_CurrentNominationPlayerId",
                table: "Rooms",
                column: "CurrentNominationPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_AuctionPasses_PlayerId",
                table: "AuctionPasses",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_AuctionPasses_RoomId_PlayerId_Round",
                table: "AuctionPasses",
                columns: new[] { "RoomId", "PlayerId", "Round" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuctionPasses_RoomId_Round",
                table: "AuctionPasses",
                columns: new[] { "RoomId", "Round" });

            migrationBuilder.AddForeignKey(
                name: "FK_Rooms_Players_CurrentNominationPlayerId",
                table: "Rooms",
                column: "CurrentNominationPlayerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Rooms_Players_CurrentNominationPlayerId",
                table: "Rooms");

            migrationBuilder.DropTable(
                name: "AuctionPasses");

            migrationBuilder.DropIndex(
                name: "IX_Rooms_CurrentNominationPlayerId",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "AuctionRound",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "CurrentNominationPlayerId",
                table: "Rooms");
        }
    }
}
