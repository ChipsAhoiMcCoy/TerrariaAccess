#nullable enable
using TerrariaAccess.Common.Systems.Guidance;

namespace TerrariaAccess.Tests.Tier1_PureFunctions;

public class GuidanceEntryTests
{
    #region CreateNpc Tests

    [Fact]
    public void CreateNpc_SetsCorrectCategory()
    {
        var entry = GuidanceEntry.CreateNpc(
            npcIndex: 5,
            displayName: "Zombie",
            worldPosition: new Vector2(100, 200),
            distanceTiles: 10f);

        entry.Category.Should().Be(GuidanceCategory.Npc);
    }

    [Fact]
    public void CreateNpc_SetsAllProperties()
    {
        var entry = GuidanceEntry.CreateNpc(
            npcIndex: 42,
            displayName: "Demon Eye",
            worldPosition: new Vector2(500, 600),
            distanceTiles: 25.5f);

        entry.Index.Should().Be(42);
        entry.DisplayName.Should().Be("Demon Eye");
        entry.WorldPosition.Should().Be(new Vector2(500, 600));
        entry.DistanceTiles.Should().Be(25.5f);
        entry.Anchor.Should().Be(Point.Zero);
    }

    #endregion

    #region CreatePlayer Tests

    [Fact]
    public void CreatePlayer_SetsCorrectCategory()
    {
        var entry = GuidanceEntry.CreatePlayer(
            playerIndex: 1,
            displayName: "Steve",
            worldPosition: new Vector2(100, 200),
            distanceTiles: 15f);

        entry.Category.Should().Be(GuidanceCategory.Player);
    }

    [Fact]
    public void CreatePlayer_SetsAllProperties()
    {
        var entry = GuidanceEntry.CreatePlayer(
            playerIndex: 3,
            displayName: "Alex",
            worldPosition: new Vector2(800, 400),
            distanceTiles: 50f);

        entry.Index.Should().Be(3);
        entry.DisplayName.Should().Be("Alex");
        entry.WorldPosition.Should().Be(new Vector2(800, 400));
        entry.DistanceTiles.Should().Be(50f);
        entry.Anchor.Should().Be(Point.Zero);
    }

    #endregion

    #region CreateInteractable Tests

    [Fact]
    public void CreateInteractable_SetsCorrectCategory()
    {
        var entry = GuidanceEntry.CreateInteractable(
            anchor: new Point(10, 20),
            displayName: "Workbench",
            worldPosition: new Vector2(160, 320),
            distanceTiles: 5f);

        entry.Category.Should().Be(GuidanceCategory.Interactable);
    }

    [Fact]
    public void CreateInteractable_SetsIndexToNegativeOne()
    {
        var entry = GuidanceEntry.CreateInteractable(
            anchor: new Point(10, 20),
            displayName: "Anvil",
            worldPosition: new Vector2(160, 320),
            distanceTiles: 5f);

        entry.Index.Should().Be(-1);
    }

    [Fact]
    public void CreateInteractable_SetsAnchor()
    {
        var anchor = new Point(15, 25);
        var entry = GuidanceEntry.CreateInteractable(
            anchor: anchor,
            displayName: "Furnace",
            worldPosition: new Vector2(240, 400),
            distanceTiles: 8f);

        entry.Anchor.Should().Be(anchor);
    }

    #endregion

    #region CreateDroppedItem Tests

    [Fact]
    public void CreateDroppedItem_SetsCorrectCategory()
    {
        var entry = GuidanceEntry.CreateDroppedItem(
            itemIndex: 100,
            displayName: "Gold Coin",
            worldPosition: new Vector2(300, 400),
            distanceTiles: 3f);

        entry.Category.Should().Be(GuidanceCategory.DroppedItem);
    }

    [Fact]
    public void CreateDroppedItem_SetsAllProperties()
    {
        var entry = GuidanceEntry.CreateDroppedItem(
            itemIndex: 77,
            displayName: "Copper Ore",
            worldPosition: new Vector2(150, 250),
            distanceTiles: 7.5f);

        entry.Index.Should().Be(77);
        entry.DisplayName.Should().Be("Copper Ore");
        entry.WorldPosition.Should().Be(new Vector2(150, 250));
        entry.DistanceTiles.Should().Be(7.5f);
    }

    #endregion

    #region CreateCritter Tests

    [Fact]
    public void CreateCritter_SetsCorrectCategory()
    {
        var entry = GuidanceEntry.CreateCritter(
            npcIndex: 50,
            displayName: "Bunny",
            worldPosition: new Vector2(200, 300),
            distanceTiles: 12f);

        entry.Category.Should().Be(GuidanceCategory.Critter);
    }

    [Fact]
    public void CreateCritter_SetsIndex()
    {
        var entry = GuidanceEntry.CreateCritter(
            npcIndex: 123,
            displayName: "Goldfish",
            worldPosition: new Vector2(200, 300),
            distanceTiles: 12f);

        entry.Index.Should().Be(123);
    }

    #endregion

    #region CreatePlantlife Tests

    [Fact]
    public void CreatePlantlife_SetsCorrectCategory()
    {
        var entry = GuidanceEntry.CreatePlantlife(
            anchor: new Point(5, 10),
            displayName: "Daybloom",
            worldPosition: new Vector2(80, 160),
            distanceTiles: 4f);

        entry.Category.Should().Be(GuidanceCategory.Plantlife);
    }

    [Fact]
    public void CreatePlantlife_SetsIndexToNegativeOne()
    {
        var entry = GuidanceEntry.CreatePlantlife(
            anchor: new Point(5, 10),
            displayName: "Blinkroot",
            worldPosition: new Vector2(80, 160),
            distanceTiles: 4f);

        entry.Index.Should().Be(-1);
    }

    [Fact]
    public void CreatePlantlife_SetsAnchor()
    {
        var anchor = new Point(30, 40);
        var entry = GuidanceEntry.CreatePlantlife(
            anchor: anchor,
            displayName: "Moonglow",
            worldPosition: new Vector2(480, 640),
            distanceTiles: 20f);

        entry.Anchor.Should().Be(anchor);
    }

    #endregion

    #region Category Coverage Tests

    [Fact]
    public void AllCategories_HaveFactoryMethods()
    {
        // Verify we have a factory method for each category
        var categories = Enum.GetValues<GuidanceCategory>();

        categories.Should().HaveCount(6);

        // Create one of each to verify
        var entries = new[]
        {
            GuidanceEntry.CreateNpc(0, "NPC", Vector2.Zero, 0),
            GuidanceEntry.CreatePlayer(0, "Player", Vector2.Zero, 0),
            GuidanceEntry.CreateInteractable(Point.Zero, "Interactable", Vector2.Zero, 0),
            GuidanceEntry.CreateDroppedItem(0, "Item", Vector2.Zero, 0),
            GuidanceEntry.CreateCritter(0, "Critter", Vector2.Zero, 0),
            GuidanceEntry.CreatePlantlife(Point.Zero, "Plant", Vector2.Zero, 0)
        };

        entries.Select(e => e.Category).Should().BeEquivalentTo(categories);
    }

    #endregion
}
