using System.Text.Json.Serialization;

namespace AuctionRoom.Api.Services;

/// <summary>Response shape from https://fantasy.premierleague.com/api/bootstrap-static/</summary>
internal record FplBootstrapResponse(
    [property: JsonPropertyName("elements")] List<FplElement> Elements,
    [property: JsonPropertyName("teams")] List<FplTeam> Teams,
    [property: JsonPropertyName("element_types")] List<FplElementType> ElementTypes
);

internal record FplElement(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("second_name")] string SecondName,
    [property: JsonPropertyName("web_name")] string? WebName,
    [property: JsonPropertyName("team")] int Team,
    [property: JsonPropertyName("element_type")] int ElementType,
    [property: JsonPropertyName("photo")] string? Photo,
    [property: JsonPropertyName("total_points")] int TotalPoints,
    [property: JsonPropertyName("event_points")] int EventPoints,
    // FPL publishes no birth_date for a minority of players, so this is genuinely null.
    [property: JsonPropertyName("birth_date")] string? BirthDate,
    // Arrives as a string, e.g. "4.4".
    [property: JsonPropertyName("points_per_game")] string? PointsPerGame,
    [property: JsonPropertyName("minutes")] int Minutes,
    // Nullable: a missing "starts" on one element would otherwise fail the whole
    // deserialize and silently break the nightly sync.
    [property: JsonPropertyName("starts")] int? Starts,
    [property: JsonPropertyName("goals_scored")] int GoalsScored,
    [property: JsonPropertyName("assists")] int Assists,
    [property: JsonPropertyName("now_cost")] int NowCost,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("news")] string? News
);

internal record FplTeam(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("short_name")] string ShortName
);

internal record FplElementType(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("singular_name")] string SingularName
);
