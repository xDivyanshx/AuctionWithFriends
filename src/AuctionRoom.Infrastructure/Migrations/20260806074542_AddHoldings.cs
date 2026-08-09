using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuctionRoom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Holdings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquiredVia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AcquisitionPoints = table.Column<int>(type: "integer", nullable: false),
                    InheritedPoints = table.Column<int>(type: "integer", nullable: false),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holdings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Holdings_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Holdings_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Holdings_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Holdings_ParticipantId",
                table: "Holdings",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_Holdings_PlayerId",
                table: "Holdings",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Holdings_RoomId_ParticipantId",
                table: "Holdings",
                columns: new[] { "RoomId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_Holdings_RoomId_PlayerId",
                table: "Holdings",
                columns: new[] { "RoomId", "PlayerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Holdings");
        }
    }
}
