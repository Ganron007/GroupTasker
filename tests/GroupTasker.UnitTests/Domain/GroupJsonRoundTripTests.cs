using System.Text.Json;
using System.Text.Json.Serialization;
using GroupTasker.Domain.Entities;

namespace GroupTasker.UnitTests.Domain;

public class GroupJsonRoundTripTests
{
    // Matches the options the production JsonGroupRepository uses.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void Group_WithShortcuts_RoundTrips()
    {
        var original = new Group { Name = "Tools" };
        original.AddShortcut(new Shortcut
        {
            SourcePath = @"C:\Apps\foo.exe",
            DisplayName = "Foo",
            Type = ShortcutType.Application
        });
        original.AddShortcut(new Shortcut
        {
            SourcePath = @"C:\Apps\bar.lnk",
            DisplayName = "Bar",
            Type = ShortcutType.Link,
            Arguments = "--with --args"
        });

        var json = JsonSerializer.Serialize(original, Options);
        var roundTrip = JsonSerializer.Deserialize<Group>(json, Options)!;

        Assert.Equal(original.Id, roundTrip.Id);
        Assert.Equal(original.Name, roundTrip.Name);
        Assert.Equal(2, roundTrip.Shortcuts.Count);
        Assert.Equal(original.Shortcuts[0].Id, roundTrip.Shortcuts[0].Id);
        Assert.Equal(original.Shortcuts[1].Arguments, roundTrip.Shortcuts[1].Arguments);
        Assert.Equal(ShortcutType.Link, roundTrip.Shortcuts[1].Type);
        Assert.Equal(0, roundTrip.Shortcuts[0].SortOrder);
        Assert.Equal(1, roundTrip.Shortcuts[1].SortOrder);
    }

    [Fact]
    public void Group_DefaultType_RoundTripsAsUnknown()
    {
        // A JSON document without an explicit type field falls back to the
        // property's field initialiser (Unknown), not the enum's zero value.
        var json = """
        {
          "id": "11111111-1111-1111-1111-111111111111",
          "name": "Test",
          "shortcuts": [
            { "id": "22222222-2222-2222-2222-222222222222", "sourcePath": "x" }
          ],
          "createdAt": "2024-01-01T00:00:00Z",
          "modifiedAt": "2024-01-01T00:00:00Z",
          "iconCacheDirty": true
        }
        """;

        var group = JsonSerializer.Deserialize<Group>(json, Options)!;

        Assert.Single(group.Shortcuts);
        Assert.Equal(ShortcutType.Unknown, group.Shortcuts[0].Type);
    }

    [Fact]
    public void LegacyNumericType_StillDeserialises()
    {
        // Files produced by the original version stored the enum as an integer
        // ("type": 0 for Application). JsonStringEnumConverter's default
        // allowIntegerValues=true keeps reading these correctly.
        var json = """
        {
          "id": "11111111-1111-1111-1111-111111111111",
          "name": "AI",
          "shortcuts": [
            {
              "id": "22222222-2222-2222-2222-222222222222",
              "sourcePath": "C:\\Apps\\foo.exe",
              "displayName": "Foo",
              "type": 0,
              "isVisible": true,
              "sortOrder": 0
            }
          ],
          "createdAt": "2024-01-01T00:00:00Z",
          "modifiedAt": "2024-01-01T00:00:00Z",
          "iconCacheDirty": false
        }
        """;

        var group = JsonSerializer.Deserialize<Group>(json, Options)!;
        Assert.Equal(ShortcutType.Application, group.Shortcuts[0].Type);
    }

    [Fact]
    public void NewWrites_UseStringEnumName()
    {
        var group = new Group { Name = "X" };
        group.AddShortcut(new Shortcut { SourcePath = "a.exe", Type = ShortcutType.Application });

        var json = JsonSerializer.Serialize(group, Options);
        Assert.Contains("\"type\": \"Application\"", json);
        Assert.DoesNotContain("\"type\": 0", json);
    }

    [Fact]
    public void Group_PreservesCreatedAt_OnRoundTrip()
    {
        var original = new Group { Name = "T" };
        var json = JsonSerializer.Serialize(original, Options);
        var roundTrip = JsonSerializer.Deserialize<Group>(json, Options)!;
        Assert.Equal(original.CreatedAt, roundTrip.CreatedAt);
    }

    [Fact]
    public void LiveApplication_RoundTrips_AsStringAndInteger()
    {
        // New writes use the string form; legacy readers (and our own deserialiser)
        // must still understand the integer ordinal if an old file pre-dates the
        // string-enum upgrade and somehow contains a 5.
        var group = new Group { Name = "Live" };
        group.AddShortcut(new Shortcut
        {
            SourcePath = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
            TargetPath = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
            DisplayName = "Calculator",
            Type = ShortcutType.LiveApplication
        });

        var json = JsonSerializer.Serialize(group, Options);
        Assert.Contains("\"type\": \"LiveApplication\"", json);

        var roundTrip = JsonSerializer.Deserialize<Group>(json, Options)!;
        Assert.Equal(ShortcutType.LiveApplication, roundTrip.Shortcuts[0].Type);
        Assert.Equal("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", roundTrip.Shortcuts[0].SourcePath);
    }
}
