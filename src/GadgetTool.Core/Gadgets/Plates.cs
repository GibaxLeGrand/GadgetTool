namespace GadgetTool.Core.Gadgets;

/// <summary>A plate level of the game (from <c>DT_GadgetPlateData</c>), for display.</summary>
/// <param name="Id">The game's id ("Rank0"... "Rank7", "RentalPlate").</param>
/// <param name="Slots">Its slots, the Super Slot not counted.</param>
/// <param name="SuperSlot">Whether it has the Super Slot.</param>
/// <param name="Image">Its picture in the data pack ("ui/plate-6.png").</param>
/// <param name="Name">Its name in the game's languages.</param>
public sealed record PlateDefinition(string Id, int Slots, bool SuperSlot, string Image, LocalizedText Name);

/// <summary>
/// Where the game puts things on a plate picture, in the picture's pixels (see docs/DATA.md):
/// the cells' size, the first cell's centre, the distance between cells across and down, the
/// Super Slot's centre and the title band.
/// </summary>
public sealed record PlateGeometry(
    int Width, int Height, int Cell,
    int FirstX, int FirstY, int StepX, int StepY,
    int SuperX, int SuperY,
    int TitleLeft, int TitleTop, int TitleRight, int TitleBottom);
