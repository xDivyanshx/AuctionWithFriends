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
    [property: JsonPropertyName("event_points")] int EventPoints
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
