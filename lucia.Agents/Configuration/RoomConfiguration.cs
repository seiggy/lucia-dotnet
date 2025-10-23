namespace lucia.Agents.Configuration;

using System.Collections.Generic;

/// <summary>
/// Defines the known rooms/locations in the home for context extraction.
/// This centralizes location knowledge and makes it easy to extend.
/// </summary>
public sealed class RoomConfiguration
{
    /// <summary>
    /// Gets the collection of known room names in the home.
    /// </summary>
    public static readonly HashSet<string> KnownRooms = new(StringComparer.OrdinalIgnoreCase)
    {
        // Primary living spaces
        "bedroom", "living room", "kitchen", "bathroom", "office",
        
        // Secondary spaces
        "garage", "patio", "hallway", "basement", "attic",
        "laundry room", "dining room", "foyer", "entryway",
        
        // Aliases and variations
        "main bedroom", "master bedroom", "guest bedroom",
        "living", "lounge", "den", "family room",
        "bath", "powder room", "guest bath",
        "deck", "porch", "balcony",
        "mudroom", "pantry", "closet",
        
        // Outdoor areas
        "backyard", "front yard", "yard"
    };

    /// <summary>
    /// Normalizes a room name by finding the best match in known rooms.
    /// </summary>
    /// <param name="roomName">The room name to normalize</param>
    /// <returns>The normalized room name, or null if not recognized</returns>
    public static string? NormalizeRoom(string? roomName)
    {
        if (string.IsNullOrWhiteSpace(roomName))
            return null;

        roomName = roomName.Trim().ToLowerInvariant();

        // Direct match
        if (KnownRooms.Contains(roomName))
            return roomName;

        // Try to find a partial match (e.g., "main bed" -> "main bedroom")
        var candidates = new List<string>();
        foreach (var knownRoom in KnownRooms)
        {
            if (knownRoom.Contains(roomName, StringComparison.OrdinalIgnoreCase) ||
                roomName.Contains(knownRoom, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(knownRoom);
            }
        }

        // Return the best match (shortest one if multiple candidates)
        if (candidates.Count > 0)
        {
            return candidates.OrderBy(c => c.Length).First();
        }

        return null;
    }
}
