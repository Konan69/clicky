namespace Clicky.App.Models;

public sealed record ParsedPointerResponse(string CleanedResponseText, IReadOnlyList<PointInstruction> PointInstructions);

