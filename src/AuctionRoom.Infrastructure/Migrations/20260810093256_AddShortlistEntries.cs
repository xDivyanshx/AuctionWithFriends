using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuctionRoom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShortlistEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShortlistEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortlistEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShortlistEntries_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShortlistEntries_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShortlistEntries_PlayerId",
                table: "ShortlistEntries",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ShortlistEntries_RoomId_PlayerId",
                table: "ShortlistEntries",
                columns: new[] { "RoomId", "PlayerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShortlistEntries");
        }
    }
}
